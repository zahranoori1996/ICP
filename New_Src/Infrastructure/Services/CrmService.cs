using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using Application.DTOs;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Wrapper;

namespace Infrastructure.Services;

/// <summary>
/// Implementation of ICrmService. 
/// Handles CRM data management and difference calculations.
/// Equivalent to CRM. py and crm_manager.py in Python code.
/// </summary>
public class CrmService : ICrmService
{
    private readonly IsatisDbContext _db;
    private readonly ILogger<CrmService> _logger;

    // Default CRM patterns to search for (from Python code)
    private static readonly string[] DefaultCrmPatterns = { "258", "252", "906", "506", "233", "255", "263", "260" };

    public CrmService(IsatisDbContext db, ILogger<CrmService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<PaginatedResult<CrmListItemDto>>> GetCrmListAsync(
        string? analysisMethod = null,
        string? searchText = null,
        bool? ourOreasOnly = null,
        int page = 1,
        int pageSize = 0)
    {
        try
        {
            var query = _db.CrmData.AsNoTracking();

            // Filter by analysis method
            if (!string.IsNullOrWhiteSpace(analysisMethod) && analysisMethod != "All")
            {
                query = query.Where(c => c.AnalysisMethod == analysisMethod);
            }

            // Filter by Our OREAS
            if (ourOreasOnly == true)
            {
                query = query.Where(c => c.IsOurOreas);
            }

            // Search in CRM ID
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var search = searchText.ToLower();
                query = query.Where(c => c.CrmId.ToLower().Contains(search));
            }

            var totalCount = await query.CountAsync();
            var pageIndex = page < 1 ? 1 : page;

            List<CrmData> items;
            if (pageSize > 0)
            {
                items = await query
                    .OrderBy(c => c.CrmId)
                    .Skip((pageIndex - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
            }
            else
            {
                items = await query
                    .OrderBy(c => c.CrmId)
                    .ToListAsync();
                pageSize = totalCount == 0 ? 1 : totalCount;
                pageIndex = 1;
            }

            var dtos = items.Select(MapToDto).ToList();

            return Result<PaginatedResult<CrmListItemDto>>.Success(
                new PaginatedResult<CrmListItemDto>(dtos, totalCount, pageIndex, pageSize));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get CRM list");
            return Result<PaginatedResult<CrmListItemDto>>.Fail($"Failed to get CRM list: {ex.Message}");
        }
    }

    public async Task<Result<CrmListItemDto>> GetCrmByIdAsync(int id)
    {
        try
        {
            var crm = await _db.CrmData.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (crm == null)
                return Result<CrmListItemDto>.Fail("CRM not found");

            return Result<CrmListItemDto>.Success(MapToDto(crm));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get CRM by ID {Id}", id);
            return Result<CrmListItemDto>.Fail($"Failed to get CRM: {ex.Message}");
        }
    }

    public async Task<Result<List<CrmListItemDto>>> GetCrmByCrmIdAsync(string crmId, string? analysisMethod = null)
    {
        try
        {
            // 1. Retrieve all records related to this ID
            var query = _db.CrmData.AsNoTracking().Where(c => c.CrmId == crmId);

            if (!string.IsNullOrEmpty(analysisMethod))
            {
                query = query.Where(c => c.AnalysisMethod == analysisMethod);
            }

            var crmRecords = await query.ToListAsync();

            if (!crmRecords.Any())
                return Result<List<CrmListItemDto>>.Fail($"CRM {crmId} not found");

            // 2. Merge Logic
            var mergedElements = new Dictionary<string, decimal>();

            // Select base record
            var preferredMethods = new[] { "4-Acid Digestion", "Aqua Regia Digestion" };
            var primaryRecord = crmRecords
                .OrderByDescending(c => preferredMethods.Any(pm => c.AnalysisMethod?.Contains(pm) == true))
                .FirstOrDefault() ?? crmRecords.First();

            foreach (var record in crmRecords)
            {
                var elements = ParseElementValues(record.ElementValues);
                foreach (var kvp in elements)
                {
                    if (!mergedElements.ContainsKey(kvp.Key))
                    {
                        mergedElements[kvp.Key] = kvp.Value;
                    }
                    else
                    {
                        if (kvp.Value > mergedElements[kvp.Key])
                        {
                            mergedElements[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }

            // 3. Create CrmData object (Modified: removed extra fields)
            var mergedEntity = new CrmData
            {
                Id = primaryRecord.Id,
                CrmId = primaryRecord.CrmId,
                AnalysisMethod = primaryRecord.AnalysisMethod + " (Combined)",
                ElementValues = System.Text.Json.JsonSerializer.Serialize(mergedElements)
                // CertDate, Supplier, Unit fields were removed
            };

            // 4. Convert to DTO
            var dto = MapToDto(mergedEntity);

            return Result<List<CrmListItemDto>>.Success(new List<CrmListItemDto> { dto });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting CRM {CrmId}", crmId);
            return Result<List<CrmListItemDto>>.Fail(ex.Message);
        }
    }

    public async Task<Result<List<string>>> GetAnalysisMethodsAsync()
    {
        try
        {
            var methods = await _db.CrmData
                .AsNoTracking()
                .Where(c => c.AnalysisMethod != null)
                .Select(c => c.AnalysisMethod!)
                .Distinct()
                .OrderBy(m => m)
                .ToListAsync();

            return Result<List<string>>.Success(methods);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get analysis methods");
            return Result<List<string>>.Fail($"Failed to get analysis methods: {ex.Message}");
        }
    }

    /// <summary>
    /// Calculate differences between project data and CRM values.
    /// Matches the logic in crm_manager.py: check_rm() and _build_crm_row_lists_for_columns()
    /// </summary>
    public async Task<Result<List<CrmDiffResultDto>>> CalculateDiffAsync(CrmDiffRequest request)
    {
        try
        {
            var projectExists = await _db.Projects.AsNoTracking()
                .AnyAsync(p => p.ProjectId == request.ProjectId);
            if (!projectExists)
                return Result<List<CrmDiffResultDto>>.Fail("Project not found");

            var crmIds = request.CrmPatterns != null && request.CrmPatterns.Count > 0
                ? request.CrmPatterns.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList()
                : DefaultCrmPatterns.ToList();

            if (crmIds.Count == 0)
                crmIds = DefaultCrmPatterns.ToList();

            var crmRegex = BuildCrmRegex(crmIds);
            var pivotData = await LoadPivotRowsAsync(request.ProjectId, request.UseInt, request.UseOxide);
            if (pivotData.Rows.Count == 0)
                return Result<List<CrmDiffResultDto>>.Success(new List<CrmDiffResultDto>());

            var pivotColumns = pivotData.Columns;
            var selections = await GetCrmSelectionMapAsync(request.ProjectId);
            var results = new List<CrmDiffResultDto>();
            var crmCache = new Dictionary<string, CrmOptions>(StringComparer.Ordinal);

            foreach (var row in pivotData.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.SolutionLabel))
                    continue;

                var crmIdNumber = TryExtractCrmId(row.SolutionLabel, crmRegex);
                if (crmIdNumber == null)
                    continue;

                if (!crmCache.TryGetValue(crmIdNumber, out var crmOptions))
                {
                    crmOptions = await LoadCrmOptionsAsync(crmIdNumber);
                    crmCache[crmIdNumber] = crmOptions;
                }

                if (crmOptions.All.Count == 0)
                    continue;

                var selectedKey = ResolveSelectedCrmKey(selections, row, crmOptions, out var selectedFromSelections);
                if (string.IsNullOrWhiteSpace(selectedKey))
                    continue;

                var selected = crmOptions.All.FirstOrDefault(o =>
                    string.Equals(o.Key, selectedKey, StringComparison.Ordinal));
                if (selected == null)
                {
                    if (selectedFromSelections)
                        continue;

                    selected = crmOptions.All[0];
                }

                var differences = new List<ElementDiffDto>(pivotColumns.Count);
                foreach (var column in pivotColumns)
                {
                    var baseElement = GetColumnBaseElement(column);
                    decimal? crmValue = null;
                    if (!string.IsNullOrWhiteSpace(baseElement))
                    {
                        crmValue = selected.Elements.TryGetValue(baseElement, out var crmVal)
                            ? crmVal
                            : 0m;
                    }
                    row.Values.TryGetValue(column, out var projectValue);

                    decimal? diffPercent = null;
                    bool isInRange = false;

                    if (projectValue.HasValue && crmValue.HasValue && crmValue.Value != 0m)
                    {
                        var rawDiff = ((crmValue.Value - projectValue.Value) / crmValue.Value) * 100m;
                        isInRange = rawDiff >= request.MinDiffPercent && rawDiff <= request.MaxDiffPercent;
                        diffPercent = Math.Round(rawDiff, Math.Max(0, request.DecimalPlaces));
                    }

                    differences.Add(new ElementDiffDto(
                        column,
                        projectValue,
                        crmValue,
                        diffPercent,
                        isInRange
                    ));
                }

                if (differences.Count > 0)
                {
                    var method = string.IsNullOrWhiteSpace(selected.AnalysisMethod) ? "Unknown" : selected.AnalysisMethod.Trim();
                    results.Add(new CrmDiffResultDto(
                        row.SolutionLabel,
                        selected.CrmId,
                        method,
                        differences
                    ));
                }
            }

            return Result<List<CrmDiffResultDto>>.Success(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate CRM diff for project {ProjectId}", request.ProjectId);
            return Result<List<CrmDiffResultDto>>.Fail($"Failed to calculate diff: {ex.Message}");
        }
    }

    public async Task<Result<int>> UpsertCrmAsync(CrmUpsertRequest request)
    {
        try
        {
            var existing = await _db.CrmData
                .FirstOrDefaultAsync(c => c.CrmId == request.CrmId && c.AnalysisMethod == request.AnalysisMethod);

            var elementsJson = JsonSerializer.Serialize(request.Elements);

            if (existing != null)
            {
                existing.Type = request.Type;
                existing.ElementValues = elementsJson;
                existing.IsOurOreas = request.IsOurOreas;
                existing.UpdatedAt = DateTime.UtcNow;
                _db.CrmData.Update(existing);
            }
            else
            {
                var newCrm = new CrmData
                {
                    CrmId = request.CrmId,
                    AnalysisMethod = request.AnalysisMethod,
                    Type = request.Type,
                    ElementValues = elementsJson,
                    IsOurOreas = request.IsOurOreas,
                    CreatedAt = DateTime.UtcNow
                };
                _db.CrmData.Add(newCrm);
            }

            await _db.SaveChangesAsync();
            var id = existing?.Id ?? (await _db.CrmData.FirstAsync(c => c.CrmId == request.CrmId && c.AnalysisMethod == request.AnalysisMethod)).Id;

            return Result<int>.Success(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert CRM {CrmId}", request.CrmId);
            return Result<int>.Fail($"Failed to upsert CRM: {ex.Message}");
        }
    }

    public async Task<Result<bool>> DeleteCrmAsync(int id)
    {
        try
        {
            var crm = await _db.CrmData.FindAsync(id);
            if (crm == null)
                return Result<bool>.Fail("CRM not found");

            _db.CrmData.Remove(crm);
            await _db.SaveChangesAsync();

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete CRM {Id}", id);
            return Result<bool>.Fail($"Failed to delete CRM: {ex.Message}");
        }
    }

    public async Task<Result<int>> ImportCrmsFromCsvAsync(Stream csvStream)
    {
        try
        {
            using var reader = new StreamReader(csvStream);
            var headerLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(headerLine))
                return Result<int>.Fail("CSV is empty");

            var headers = headerLine.Split(',').Select(h => h.Trim()).ToArray();
            var crmIdIndex = Array.FindIndex(headers, h => h.Equals("CRM ID", StringComparison.OrdinalIgnoreCase));
            var methodIndex = Array.FindIndex(headers, h => h.Equals("Analysis Method", StringComparison.OrdinalIgnoreCase));

            if (crmIdIndex < 0)
                return Result<int>.Fail("CSV must have 'CRM ID' column");

            var importedCount = 0;
            string? line;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var values = line.Split(',');
                if (values.Length <= crmIdIndex)
                    continue;

                var crmId = values[crmIdIndex].Trim();
                var method = methodIndex >= 0 && values.Length > methodIndex ? values[methodIndex].Trim() : null;

                // Parse element values
                var elements = new Dictionary<string, decimal>();
                for (int i = 0; i < headers.Length && i < values.Length; i++)
                {
                    if (i == crmIdIndex || i == methodIndex)
                        continue;

                    var header = headers[i];
                    if (decimal.TryParse(values[i], out var val))
                    {
                        var symbol = ExtractElementSymbol(header);
                        if (!string.IsNullOrEmpty(symbol))
                            elements[symbol] = val;
                    }
                }

                var request = new CrmUpsertRequest(crmId, method, null, elements, false);
                var result = await UpsertCrmAsync(request);
                if (result.Succeeded)
                    importedCount++;
            }

            return Result<int>.Success(importedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import CRMs from CSV");
            return Result<int>.Fail($"Failed to import: {ex.Message}");
        }
    }

    #region Private Helpers

    private static CrmListItemDto MapToDto(CrmData crm)
    {
        var elements = ParseElementValues(crm.ElementValues);
        return new CrmListItemDto(
            crm.Id,
            crm.CrmId,
            crm.AnalysisMethod,
            crm.Type,
            crm.IsOurOreas,
            elements
        );
    }

    private static Dictionary<string, decimal> ParseElementValues(string json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, decimal>();

            return JsonSerializer.Deserialize<Dictionary<string, decimal>>(json)
                   ?? new Dictionary<string, decimal>();
        }
        catch
        {
            return new Dictionary<string, decimal>();
        }
    }

    private sealed record PivotData(List<PivotRow> Rows, List<string> Columns);

    private sealed class PivotRow
    {
        public PivotRow(string solutionLabel, int rowIndex, Dictionary<string, decimal?> values)
        {
            SolutionLabel = solutionLabel;
            RowIndex = rowIndex;
            Values = values;
        }

        public string SolutionLabel { get; }
        public int RowIndex { get; }
        public Dictionary<string, decimal?> Values { get; }
    }

    private sealed class SampleRecord
    {
        public SampleRecord(
            string solutionLabel,
            string element,
            decimal? value,
            int originalIndex,
            int positionInSolution)
        {
            SolutionLabel = solutionLabel;
            Element = element;
            Value = value;
            OriginalIndex = originalIndex;
            PositionInSolution = positionInSolution;
        }

        public string SolutionLabel { get; }
        public string Element { get; }
        public decimal? Value { get; }
        public int OriginalIndex { get; }
        public int PositionInSolution { get; }
        public int GroupId { get; set; }
        public int Uid { get; set; }
        public string ColumnKey { get; set; } = string.Empty;
    }

    private sealed class PivotRowBucket
    {
        public PivotRowBucket(string solutionLabel, int groupId, int firstIndex)
        {
            SolutionLabel = solutionLabel;
            GroupId = groupId;
            FirstIndex = firstIndex;
        }

        public string SolutionLabel { get; }
        public int GroupId { get; }
        public int FirstIndex { get; set; }
        public Dictionary<string, decimal?> Values { get; private set; } = new();
        public List<string> KeyOrder { get; private set; } = new();

        public void SetValue(string key, decimal? value)
        {
            if (!Values.ContainsKey(key))
                KeyOrder.Add(key);

            Values[key] = value;
        }

        public void ReplaceValues(Dictionary<string, decimal?> values, List<string> keyOrder)
        {
            Values = values;
            KeyOrder = keyOrder;
        }
    }

    private sealed record CrmOption(
        int Id,
        string Key,
        string CrmId,
        string AnalysisMethod,
        Dictionary<string, decimal> Elements);

    private sealed record CrmOptions(
        List<CrmOption> All,
        List<CrmOption> Preferred);

    private sealed record OxideFactor(string Formula, decimal Factor);

    private static readonly Dictionary<string, OxideFactor> PythonOxideFactors = new(StringComparer.Ordinal)
    {
        { "Li", new OxideFactor("Li2O", 29.8814m / (2m * 6.941m)) },
        { "Be", new OxideFactor("BeO", 25.0116m / 9.0122m) },
        { "B", new OxideFactor("B2O3", 69.6182m / (2m * 10.811m)) },
        { "C", new OxideFactor("CO2", 44.0095m / 12.011m) },
        { "N", new OxideFactor("N2O5", 108.0104m / (2m * 14.007m)) },
        { "O", new OxideFactor("O2", 31.9988m / (2m * 15.999m)) },
        { "F", new OxideFactor("F2O", 53.9962m / (2m * 18.998m)) },
        { "Na", new OxideFactor("Na2O", 61.9789m / (2m * 22.9898m)) },
        { "Mg", new OxideFactor("MgO", 40.3044m / 24.305m) },
        { "Al", new OxideFactor("Al2O3", 101.9613m / (2m * 26.9815m)) },
        { "Si", new OxideFactor("SiO2", 60.0843m / 28.0855m) },
        { "P", new OxideFactor("P2O5", 141.9445m / (2m * 30.9738m)) },
        { "S", new OxideFactor("SO3", 80.0642m / 32.06m) },
        { "Cl", new OxideFactor("Cl2O7", 182.9027m / (2m * 35.453m)) },
        { "K", new OxideFactor("K2O", 94.196m / (2m * 39.0983m)) },
        { "Ca", new OxideFactor("CaO", 56.0774m / 40.078m) },
        { "Sc", new OxideFactor("Sc2O3", 137.9104m / (2m * 44.9559m)) },
        { "Ti", new OxideFactor("TiO2", 79.8658m / 47.867m) },
        { "V", new OxideFactor("V2O5", 181.8802m / (2m * 50.9415m)) },
        { "Cr", new OxideFactor("Cr2O3", 151.9902m / (2m * 51.9961m)) },
        { "Mn", new OxideFactor("MnO", 70.9374m / 54.9380m) },
        { "Fe", new OxideFactor("Fe2O3", 159.6872m / (2m * 55.845m)) },
        { "Co", new OxideFactor("CoO", 74.9326m / 58.9332m) },
        { "Ni", new OxideFactor("NiO", 74.6928m / 58.6934m) },
        { "Cu", new OxideFactor("CuO", 79.5454m / 63.546m) },
        { "Zn", new OxideFactor("ZnO", 81.3794m / 65.38m) },
        { "Ga", new OxideFactor("Ga2O3", 187.443m / (2m * 69.723m)) },
        { "Ge", new OxideFactor("GeO2", 104.636m / 72.63m) },
        { "As", new OxideFactor("As2O5", 229.8404m / (2m * 74.9216m)) },
        { "Se", new OxideFactor("SeO2", 110.96m / 78.971m) },
        { "Br", new OxideFactor("Br2O5", 270.808m / (2m * 79.904m)) },
        { "Rb", new OxideFactor("Rb2O", 186.94m / (2m * 85.4678m)) },
        { "Sr", new OxideFactor("SrO", 103.6194m / 87.62m) },
        { "Y", new OxideFactor("Y2O3", 225.8102m / (2m * 88.9058m)) },
        { "Zr", new OxideFactor("ZrO2", 123.2182m / 91.224m) },
        { "Nb", new OxideFactor("Nb2O5", 265.8098m / (2m * 92.9064m)) },
        { "Mo", new OxideFactor("MoO3", 143.9382m / 95.94m) },
        { "Tc", new OxideFactor("Tc2O7", 291.818m / (2m * 98m)) },
        { "Ru", new OxideFactor("RuO2", 133.07m / 101.07m) },
        { "Rh", new OxideFactor("Rh2O3", 233.808m / (2m * 102.9055m)) },
        { "Pd", new OxideFactor("PdO", 122.42m / 106.42m) },
        { "Ag", new OxideFactor("Ag2O", 231.735m / (2m * 107.8682m)) },
        { "Cd", new OxideFactor("CdO", 128.41m / 112.411m) },
        { "In", new OxideFactor("In2O3", 277.64m / (2m * 114.818m)) },
        { "Sn", new OxideFactor("SnO2", 150.7088m / 118.71m) },
        { "Sb", new OxideFactor("Sb2O3", 291.5182m / (2m * 121.76m)) },
        { "Te", new OxideFactor("TeO2", 159.5988m / 127.60m) },
        { "I", new OxideFactor("I2O5", 333.805m / (2m * 126.9045m)) },
        { "Cs", new OxideFactor("Cs2O", 281.81m / (2m * 132.905m)) },
        { "Ba", new OxideFactor("BaO", 153.3294m / 137.327m) },
        { "La", new OxideFactor("La2O3", 325.8092m / (2m * 138.9055m)) },
        { "Ce", new OxideFactor("CeO2", 172.1148m / 140.116m) },
        { "Pr", new OxideFactor("Pr6O11", 1021.44m / (6m * 140.9077m)) },
        { "Nd", new OxideFactor("Nd2O3", 336.482m / (2m * 144.242m)) },
        { "Sm", new OxideFactor("Sm2O3", 348.72m / (2m * 150.36m)) },
        { "Eu", new OxideFactor("Eu2O3", 351.926m / (2m * 151.964m)) },
        { "Gd", new OxideFactor("Gd2O3", 362.498m / (2m * 157.25m)) },
        { "Tb", new OxideFactor("Tb4O7", 747.696m / (4m * 158.925m)) },
        { "Dy", new OxideFactor("Dy2O3", 372.998m / (2m * 162.5m)) },
        { "Ho", new OxideFactor("Ho2O3", 377.86m / (2m * 164.93m)) },
        { "Er", new OxideFactor("Er2O3", 382.52m / (2m * 167.259m)) },
        { "Tm", new OxideFactor("Tm2O3", 385.866m / (2m * 168.934m)) },
        { "Yb", new OxideFactor("Yb2O3", 394.08m / (2m * 173.04m)) },
        { "Lu", new OxideFactor("Lu2O3", 397.932m / (2m * 174.967m)) },
        { "Hf", new OxideFactor("HfO2", 210.49m / 178.49m) },
        { "Ta", new OxideFactor("Ta2O5", 441.89m / (2m * 180.948m)) },
        { "W", new OxideFactor("WO3", 231.8382m / 183.84m) },
        { "Re", new OxideFactor("Re2O7", 484.42m / (2m * 186.207m)) },
        { "Os", new OxideFactor("OsO4", 254.23m / 190.23m) },
        { "Ir", new OxideFactor("Ir2O3", 409.42m / (2m * 192.217m)) },
        { "Pt", new OxideFactor("PtO2", 227.08m / 195.084m) },
        { "Au", new OxideFactor("Au2O3", 441.93m / (2m * 196.967m)) },
        { "Hg", new OxideFactor("HgO", 216.59m / 200.59m) },
        { "Tl", new OxideFactor("Tl2O3", 456.76m / (2m * 204.38m)) },
        { "Pb", new OxideFactor("PbO", 223.1992m / 207.2m) },
        { "Bi", new OxideFactor("Bi2O3", 465.9590m / (2m * 208.9804m)) },
        { "Th", new OxideFactor("ThO2", 248.0722m / 232.038m) },
        { "U", new OxideFactor("U3O8", 842.088m / (3m * 238.029m)) }
    };

    private static readonly Regex LabelNumberRegex = new(@"\d+", RegexOptions.Compiled);

    private static Regex BuildCrmRegex(IEnumerable<string> crmIds)
    {
        var ids = crmIds
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Regex.Escape(p.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
            return new Regex("$a", RegexOptions.Compiled);

        var pattern = $@"(?i)(?:(?:^|(?<=\s))(?:CRM|OREAS)?\s*({string.Join("|", ids)})(?:[a-zA-Z0-9]{{0,2}})?\b)";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    private static string? TryExtractCrmId(string label, Regex crmRegex)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var match = crmRegex.Match(label);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private async Task<PivotData> LoadPivotRowsAsync(Guid projectId, bool useInt, bool useOxide)
    {
        var rawRows = await _db.RawDataRows.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.DataId)
            .ToListAsync();

        var samples = new List<SampleRecord>();
        var solutionGroups = new Dictionary<string, List<SampleRecord>>(StringComparer.Ordinal);
        int sampleIndex = 0;

        foreach (var row in rawRows)
        {
            if (string.IsNullOrWhiteSpace(row.ColumnData))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(row.ColumnData);
                var root = doc.RootElement;

                var solutionLabel = TryGetString(root, "Solution Label") ?? row.SampleId ?? string.Empty;
                var type = TryGetString(root, "Type") ?? string.Empty;
                if (!IsSampleType(type))
                    continue;

                var elementRaw = TryGetString(root, "Element") ?? string.Empty;
                var element = NormalizeElementForPivot(elementRaw);
                if (string.IsNullOrWhiteSpace(element))
                    continue;

                decimal? value = null;
                if (useInt)
                {
                    if (TryGetDecimal(root, "Int", out var intVal))
                        value = intVal;
                }
                else
                {
                    if (TryGetDecimal(root, "Corr Con", out var corrVal))
                        value = corrVal;
                }

                var normalizedLabel = NormalizeSolutionLabel(solutionLabel);
                if (!solutionGroups.TryGetValue(normalizedLabel, out var list))
                {
                    list = new List<SampleRecord>();
                    solutionGroups[normalizedLabel] = list;
                }

                var record = new SampleRecord(normalizedLabel, element, value, sampleIndex, list.Count);
                list.Add(record);
                samples.Add(record);
                sampleIndex++;
            }
            catch
            {
                // ignore broken json row
            }
        }

        if (samples.Count == 0)
            return new PivotData(new List<PivotRow>(), new List<string>());

        var mostCommonSizes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kvp in solutionGroups)
        {
            var group = kvp.Value;
            var counts = group.GroupBy(r => r.Element)
                .Select(g => g.Count())
                .ToList();
            var total = group.Count;
            if (counts.Count > 0)
            {
                var gcd = GCDList(counts);
                mostCommonSizes[kvp.Key] = gcd > 1 && total % gcd == 0 ? total / gcd : total;
            }
            else
            {
                mostCommonSizes[kvp.Key] = total > 0 ? total : 1;
            }
        }

        var repeatCounter = new Dictionary<string, int>(StringComparer.Ordinal);
        bool hasRepeats = false;
        foreach (var rec in samples)
        {
            var size = mostCommonSizes.GetValueOrDefault(rec.SolutionLabel, 1);
            rec.GroupId = size > 0 ? rec.PositionInSolution / size : 0;
            var key = $"{rec.SolutionLabel}::{rec.GroupId}::{rec.Element}";
            var count = repeatCounter.GetValueOrDefault(key) + 1;
            repeatCounter[key] = count;
            if (count > 1)
            {
                hasRepeats = true;
                break;
            }
        }

        var rowBuckets = new Dictionary<string, PivotRowBucket>(StringComparer.Ordinal);

        if (hasRepeats)
        {
            var occCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var rec in samples)
            {
                var key = $"{rec.SolutionLabel}::{rec.GroupId}::{rec.Element}";
                occCounts[key] = occCounts.GetValueOrDefault(key) + 1;
            }

            var occCounter = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var rec in samples)
            {
                var key = $"{rec.SolutionLabel}::{rec.GroupId}::{rec.Element}";
                var count = occCounts[key];
                var idx = occCounter.GetValueOrDefault(key) + 1;
                occCounter[key] = idx;
                rec.ColumnKey = count > 1 ? $"{rec.Element}_{idx}" : rec.Element;

                var rowKey = $"{rec.SolutionLabel}::{rec.GroupId}";
                if (!rowBuckets.TryGetValue(rowKey, out var bucket))
                {
                    bucket = new PivotRowBucket(rec.SolutionLabel, rec.GroupId, rec.OriginalIndex);
                    rowBuckets[rowKey] = bucket;
                }

                bucket.FirstIndex = Math.Min(bucket.FirstIndex, rec.OriginalIndex);
                bucket.SetValue(rec.ColumnKey, rec.Value);
            }
        }
        else
        {
            var uidMap = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var rec in samples)
            {
                var uidKey = $"{rec.SolutionLabel}::{rec.Element}";
                var uid = uidMap.GetValueOrDefault(uidKey) + 1;
                uidMap[uidKey] = uid;
                rec.Uid = uid - 1;

                var rowKey = $"{rec.SolutionLabel}::{rec.Uid}";
                if (!rowBuckets.TryGetValue(rowKey, out var bucket))
                {
                    bucket = new PivotRowBucket(rec.SolutionLabel, rec.Uid, rec.OriginalIndex);
                    rowBuckets[rowKey] = bucket;
                }

                bucket.FirstIndex = Math.Min(bucket.FirstIndex, rec.OriginalIndex);
                bucket.SetValue(rec.Element, rec.Value);
            }
        }

        var ordered = rowBuckets.Values
            .OrderBy(r => r.FirstIndex)
            .ToList();

        if (ordered.Count == 0)
            return new PivotData(new List<PivotRow>(), new List<string>());

        List<string> elementOrder;
        if (hasRepeats)
        {
            PivotRowBucket? firstFull = null;
            foreach (var bucket in ordered)
            {
                var size = mostCommonSizes.GetValueOrDefault(bucket.SolutionLabel, 1);
                if (bucket.Values.Count >= size)
                {
                    firstFull = bucket;
                    break;
                }
            }

            var sourceBucket = firstFull ?? ordered[0];
            elementOrder = OrderByLabelKey(sourceBucket.KeyOrder);
        }
        else
        {
            var elements = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bucket in ordered)
            {
                foreach (var key in bucket.KeyOrder)
                    elements.Add(key);
            }

            elementOrder = OrderByLabelKey(elements);
        }

        if (useOxide)
        {
            foreach (var bucket in ordered)
            {
                var newValues = new Dictionary<string, decimal?>(StringComparer.Ordinal);
                var newOrder = new List<string>();

                foreach (var key in bucket.KeyOrder)
                {
                    bucket.Values.TryGetValue(key, out var value);
                    var underscoreIndex = key.IndexOf('_');
                    var elem = underscoreIndex >= 0 ? key[..underscoreIndex] : key;
                    var suffix = hasRepeats && underscoreIndex >= 0
                        ? "_" + key[(underscoreIndex + 1)..]
                        : string.Empty;

                    if (PythonOxideFactors.TryGetValue(elem, out var oxide))
                    {
                        var newKey = string.IsNullOrEmpty(suffix) ? oxide.Formula : $"{oxide.Formula}{suffix}";
                        var newValue = value.HasValue ? value.Value * oxide.Factor : (decimal?)null;
                        if (!newValues.ContainsKey(newKey))
                            newOrder.Add(newKey);
                        newValues[newKey] = newValue;
                    }
                    else
                    {
                        if (!newValues.ContainsKey(key))
                            newOrder.Add(key);
                        newValues[key] = value;
                    }
                }

                bucket.ReplaceValues(newValues, newOrder);
            }
        }

        var pivotColumnsBase = new List<string> { "Solution Label" };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bucket in ordered)
        {
            foreach (var key in bucket.KeyOrder)
            {
                if (seen.Add(key))
                    pivotColumnsBase.Add(key);
            }
        }

        List<string> finalColumns;
        if (elementOrder.Count > 0)
        {
            var cols = new List<string> { "Solution Label" };
            var baseSet = new HashSet<string>(pivotColumnsBase, StringComparer.Ordinal);
            foreach (var col in elementOrder)
            {
                if (baseSet.Contains(col))
                    cols.Add(col);
            }

            var colsSet = new HashSet<string>(cols, StringComparer.Ordinal);
            var missing = pivotColumnsBase.Where(c => !colsSet.Contains(c)).ToList();
            finalColumns = cols.Concat(missing).ToList();
        }
        else
        {
            finalColumns = pivotColumnsBase;
        }

        var pivotRows = new List<PivotRow>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            var bucket = ordered[i];
            pivotRows.Add(new PivotRow(bucket.SolutionLabel, i, bucket.Values));
        }

        var columns = finalColumns
            .Where(c => !string.Equals(c, "Solution Label", StringComparison.Ordinal))
            .ToList();
        return new PivotData(pivotRows, columns);
    }

    private async Task<Dictionary<string, string>> GetCrmSelectionMapAsync(Guid projectId)
    {
        var rows = await _db.CrmSelections.AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .ToListAsync();

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = BuildRowKey(row.SolutionLabel, row.RowIndex);
            map[key] = row.SelectedCrmKey;
        }

        return map;
    }

    private async Task<CrmOptions> LoadCrmOptionsAsync(string crmIdNumber)
    {
        var prefix = $"oreas {crmIdNumber}".ToLowerInvariant();
        var rows = await _db.CrmData.AsNoTracking()
            .Where(c => c.CrmId.ToLower().StartsWith(prefix))
            .OrderBy(c => c.Id)
            .ToListAsync();

        if (rows.Count == 0)
            return new CrmOptions(new List<CrmOption>(), new List<CrmOption>());

        var allowedMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "4-Acid Digestion",
            "Aqua Regia Digestion"
        };

        var all = new List<CrmOption>();
        var preferred = new List<CrmOption>();

        foreach (var row in rows)
        {
            var method = string.IsNullOrWhiteSpace(row.AnalysisMethod) ? "Unknown" : row.AnalysisMethod.Trim();
            var key = $"{row.CrmId} ({method})";

            var elements = ParseCrmElementValues(row.ElementValues);
            var normalized = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var kvp in elements)
            {
                var symbol = kvp.Key.Split('_')[0].Trim();
                if (string.IsNullOrWhiteSpace(symbol))
                    continue;

                normalized[symbol] = kvp.Value;
            }

            var option = new CrmOption(row.Id, key, row.CrmId, method, normalized);
            all.Add(option);
            if (allowedMethods.Contains(method))
                preferred.Add(option);
        }

        return new CrmOptions(all, preferred);
    }

    private static string? ResolveSelectedCrmKey(
        Dictionary<string, string> selections,
        PivotRow row,
        CrmOptions options,
        out bool fromSelections)
    {
        fromSelections = false;
        var rowKey = BuildRowKey(row.SolutionLabel, row.RowIndex);
        if (selections.TryGetValue(rowKey, out var selected) && !string.IsNullOrWhiteSpace(selected))
        {
            fromSelections = true;
            return selected;
        }

        var candidates = options.Preferred.Count > 0 ? options.Preferred : options.All;
        if (candidates.Count == 0)
            return null;

        var method = candidates
            .Select(o => o.AnalysisMethod ?? string.Empty)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(method))
        {
            var byMethod = candidates
                .Where(o => string.Equals(o.AnalysisMethod, method, StringComparison.OrdinalIgnoreCase))
                .OrderBy(o => o.Id)
                .FirstOrDefault();
            if (byMethod != null)
                return byMethod.Key;
        }

        return candidates.OrderBy(o => o.Id).First().Key;
    }

    private static string BuildRowKey(string solutionLabel, int rowIndex)
        => $"{solutionLabel}::{rowIndex}";

    private static string GetColumnBaseElement(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return string.Empty;

        var parts = columnName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0].Trim() : columnName.Trim();
    }

    private static (string Prefix, int Number) BuildLabelKey(string value)
    {
        var cleaned = (value ?? string.Empty).Replace(" ", string.Empty);
        var match = LabelNumberRegex.Match(cleaned);
        if (!match.Success)
            return (cleaned.ToLowerInvariant(), 0);

        var prefix = cleaned[..match.Index].ToLowerInvariant();
        var number = int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num)
            ? num
            : 0;
        return (prefix, number);
    }

    private static List<string> OrderByLabelKey(IEnumerable<string> values)
    {
        return values
            .Select(v => new { Value = v, Key = BuildLabelKey(v) })
            .OrderBy(v => v.Key.Prefix, StringComparer.Ordinal)
            .ThenBy(v => v.Key.Number)
            .Select(v => v.Value)
            .ToList();
    }

    private static Dictionary<string, decimal> ParseCrmElementValues(string json)
    {
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number &&
                    prop.Value.TryGetDecimal(out var numVal))
                {
                    result[prop.Name] = numVal;
                }
                else if (prop.Value.ValueKind == JsonValueKind.String &&
                         decimal.TryParse(prop.Value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var strVal))
                {
                    result[prop.Name] = strVal;
                }
            }
        }
        catch
        {
            return new Dictionary<string, decimal>(StringComparer.Ordinal);
        }

        return result;
    }

    private static string? TryGetString(JsonElement root, string propName)
    {
        if (!root.TryGetProperty(propName, out var p))
            return null;

        if (p.ValueKind == JsonValueKind.String)
            return p.GetString();

        return p.ToString();
    }

    private static bool TryGetDecimal(JsonElement root, string propName, out decimal value)
    {
        value = 0m;
        if (!root.TryGetProperty(propName, out var p))
            return false;

        if (p.ValueKind == JsonValueKind.Number)
            return p.TryGetDecimal(out value);

        if (p.ValueKind == JsonValueKind.String &&
            decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static string NormalizeSolutionLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "Unknown";

        var trimmed = label.Trim();
        return string.Equals(trimmed, "nan", StringComparison.OrdinalIgnoreCase) ? "Unknown" : trimmed;
    }

    private static string NormalizeElementForPivot(string rawElement)
    {
        if (string.IsNullOrWhiteSpace(rawElement))
            return string.Empty;

        var cleaned = rawElement.Trim();
        var parts = cleaned.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0].Trim() : cleaned;
    }

    private static bool IsSampleType(string type)
    {
        return string.Equals(type, "Samp", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "Sample", StringComparison.OrdinalIgnoreCase);
    }

    private static int GCD(int a, int b)
    {
        while (b != 0)
        {
            int temp = b;
            b = a % b;
            a = temp;
        }
        return a;
    }

    private static int GCDList(IEnumerable<int> values)
    {
        int result = 0;
        foreach (var v in values)
            result = result == 0 ? v : GCD(result, v);

        return result == 0 ? 1 : result;
    }

    /// <summary>
    /// Find CRM match in solution label using patterns. 
    /// Matches logic from Python: is_crm_label()
    /// Python pattern: rf'(?i)(?:(?:^|(?<=\s))(?:CRM|OREAS)?\s*{crm_id}(?:[a-zA-Z0-9]{0,2})?\b)'
    /// </summary>
    private static string? FindCrmMatch(string label, List<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var labelLower = label.Trim().ToLower();

        foreach (var pattern in patterns)
        {
            // Python-compatible pattern: rf'(?i)(?:(?:^|(?<=\s))(?:CRM|OREAS)?\s*{crm_id}(?:[a-zA-Z0-9]{0,2})?\b)'
            // Match: "OREAS 258", "CRM258", "258a", "oreas-258", "258", " 258b"
            var regexPattern = $@"(?:^|(?<=\s))(?:CRM|OREAS)?[\s\-_]*({Regex.Escape(pattern)}(?:[a-zA-Z0-9]{{0,2}})?)\b";

            try
            {
                var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
                var match = regex.Match(label);
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
            }
            catch
            {
                // If regex fails, try simple contains
                if (labelLower.Contains(pattern.ToLower()))
                {
                    return pattern;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extract element symbol from column name. 
    /// Python equivalent: col.split('_')[0].strip()
    /// E.g., "Fe_ppm" -> "Fe", "Fe 238.204" -> "Fe", "Cu_1" -> "Cu"
    /// </summary>
    private static string? ExtractElementSymbol(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return null;

        columnName = columnName.Trim();

        // Python approach: split by underscore first
        // col.split('_')[0].strip()
        var underscoreParts = columnName.Split('_');
        var baseName = underscoreParts[0].Trim();

        // If the base name contains space (like "Fe 238.204"), take first part
        var spaceParts = baseName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (spaceParts.Length > 0)
        {
            baseName = spaceParts[0].Trim();
        }

        // Validate it looks like an element symbol (1-3 letters, starts with uppercase)
        if (!string.IsNullOrEmpty(baseName) && 
            baseName.Length <= 3 && 
            char.IsUpper(baseName[0]) &&
            baseName.All(c => char.IsLetter(c)))
        {
            return baseName;
        }

        // Fallback: try regex for element symbol pattern
        var match = Regex.Match(columnName, @"^([A-Z][a-z]{0,2})");
        return match.Success ? match.Groups[1].Value : null;
    }

    #endregion
}
