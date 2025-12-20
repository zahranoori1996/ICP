using System.Text.Json;
using System.Text.RegularExpressions;
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
        int pageSize = 50)
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

            var items = await query
                .OrderBy(c => c.CrmId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var dtos = items.Select(MapToDto).ToList();

            return Result<PaginatedResult<CrmListItemDto>>.Success(
                new PaginatedResult<CrmListItemDto>(dtos, totalCount, page, pageSize));
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
            // 1. Load project raw data
            var project = await _db.Projects
                .AsNoTracking()
                .Include(p => p.RawDataRows)
                .FirstOrDefaultAsync(p => p.ProjectId == request.ProjectId);

            if (project == null)
                return Result<List<CrmDiffResultDto>>.Fail("Project not found");

            var patterns = request.CrmPatterns ?? DefaultCrmPatterns.ToList();
            var results = new List<CrmDiffResultDto>();

            // 2. Parse raw rows and find CRM matches
            foreach (var rawRow in project.RawDataRows)
            {
                if (string.IsNullOrWhiteSpace(rawRow.ColumnData))
                    continue;

                Dictionary<string, object?>? rowData;
                try
                {
                    rowData = JsonSerializer.Deserialize<Dictionary<string, object?>>(rawRow.ColumnData);
                }
                catch
                {
                    continue;
                }

                if (rowData == null)
                    continue;

                // Get Solution Label
                var solutionLabel = rawRow.SampleId ??
                    (rowData.TryGetValue("Solution Label", out var sl) ? sl?.ToString() : null) ??
                    (rowData.TryGetValue("SolutionLabel", out var sl2) ? sl2?.ToString() : null);

                if (string.IsNullOrWhiteSpace(solutionLabel))
                    continue;

                // Check if this row matches a CRM pattern
                var matchedCrmNumber = FindCrmMatch(solutionLabel, patterns);
                if (matchedCrmNumber == null)
                    continue;

                // 3. Find ALL matching CRMs in database and MERGE them
                // Instead of picking just one, we fetch all rows for this CRM ID (e.g. OREAS 252)
                var allCrmRecords = await _db.CrmData
                    .AsNoTracking()
                    .Where(c => c.CrmId.Contains(matchedCrmNumber))
                    .ToListAsync();

                if (!allCrmRecords.Any())
                    continue;

                // --- MERGE LOGIC START ---
                // We create a unified dictionary of elements taking the Max value found across all methods.
                // This handles cases where Au is in "Fire Assay" and Cu is in "4-Acid".
                var mergedCrmElements = new Dictionary<string, decimal>();
                string finalCrmId = allCrmRecords.First().CrmId; // e.g. "OREAS 252"

                // Identify the "Best" method name just for display purposes
                var preferredMethods = new[] { "4-Acid Digestion", "Aqua Regia Digestion", "Fire Assay" };
                var bestRecord = allCrmRecords
                    .OrderByDescending(c => preferredMethods.Any(pm => c.AnalysisMethod?.Contains(pm) == true))
                    .ThenByDescending(c => c.ElementValues.Length) // Prefer record with more data
                    .First();
                string displayMethod = bestRecord.AnalysisMethod ?? "Combined";

                foreach (var record in allCrmRecords)
                {
                    var elements = ParseElementValues(record.ElementValues);
                    foreach (var kvp in elements)
                    {
                        // If element exists, take the larger value (assuming max is total digestion)
                        // Or if it's 0 in one and valid in another, take the valid one.
                        if (!mergedCrmElements.ContainsKey(kvp.Key))
                        {
                            mergedCrmElements[kvp.Key] = kvp.Value;
                        }
                        else
                        {
                            if (kvp.Value > mergedCrmElements[kvp.Key])
                            {
                                mergedCrmElements[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }
                // --- MERGE LOGIC END ---

                // 4. Calculate differences
                var differences = new List<ElementDiffDto>();

                foreach (var kvp in rowData)
                {
                    if (kvp.Key == "Solution Label" || kvp.Key == "SolutionLabel" || kvp.Key == "SampleId")
                        continue;

                    var elementSymbol = ExtractElementSymbol(kvp.Key);
                    if (string.IsNullOrEmpty(elementSymbol))
                        continue;

                    // Get project value
                    decimal? projectValue = null;
                    if (kvp.Value is JsonElement je)
                    {
                        if (je.ValueKind == JsonValueKind.Number && je.TryGetDecimal(out var d))
                            projectValue = d;
                        else if (je.ValueKind == JsonValueKind.String && decimal.TryParse(je.GetString(), out var d2))
                            projectValue = d2;
                    }
                    else if (kvp.Value != null && decimal.TryParse(kvp.Value.ToString(), out var d3))
                    {
                        projectValue = d3;
                    }

                    // Get CRM value from MERGED dictionary
                    decimal? crmValue = null;
                    if (mergedCrmElements.TryGetValue(elementSymbol, out var cv))
                        crmValue = cv;

                    decimal? diffPercent = null;
                    bool isInRange = false;

                    if (projectValue.HasValue && crmValue.HasValue && crmValue.Value != 0)
                    {
                        diffPercent = ((projectValue.Value - crmValue.Value) / crmValue.Value) * 100; // Corrected: (Measured - Ref) / Ref

                        // Check range (using absolute value logic if needed, or simple min/max)
                        // Assuming request.Min/Max are like -10 and 10
                        isInRange = diffPercent >= request.MinDiffPercent && diffPercent <= request.MaxDiffPercent;
                    }

                    // Only add if we have data to compare (or if user wants to see all)
                    // Here we add everything found in the sample
                    differences.Add(new ElementDiffDto(
                        elementSymbol, // Use clean symbol as key
                        projectValue,
                        crmValue,
                        diffPercent.HasValue ? Math.Round(diffPercent.Value, 2) : null,
                        isInRange
                    ));
                }

                if (differences.Any())
                {
                    results.Add(new CrmDiffResultDto(
                        solutionLabel,
                        finalCrmId,
                        displayMethod,
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