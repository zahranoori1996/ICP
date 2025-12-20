using System.Text;
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

public class PivotService : IPivotService
{
    private readonly IsatisDbContext _db;
    private readonly ILogger<PivotService> _logger;

    // Default duplicate patterns
    private static readonly string[] DefaultDuplicatePatterns = { "TEK", "RET", "ret", "Ret" };

    public PivotService(IsatisDbContext db, ILogger<PivotService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Basic Pivot

    public async Task<Result<PivotResultDto>> GetPivotTableAsync(PivotRequest request)
    {
        try
        {
            var project = await _db.Projects
                .AsNoTracking()
                .Include(p => p.RawDataRows)
                .FirstOrDefaultAsync(p => p.ProjectId == request.ProjectId);

            if (project == null)
                return Result<PivotResultDto>.Fail("Project not found");

            if (!project.RawDataRows.Any())
                return Result<PivotResultDto>.Fail("Project has no data");

            // Detect format: ICP format has "Element" column, Tabular format doesn't
            var firstRow = project.RawDataRows.First();
            var firstRowData = ParseRowData(firstRow);
            bool isIcpFormat = firstRowData != null && firstRowData.ContainsKey("Element");

            if (isIcpFormat)
            {
                return await GetPivotTableIcpFormatAsync(project, request);
            }
            else
            {
                return await GetPivotTableTabularFormatAsync(project, request);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pivot table for project {ProjectId}", request.ProjectId);
            return Result<PivotResultDto>.Fail($"Failed to get pivot table: {ex.Message}");
        }
    }

    private async Task<Result<PivotResultDto>> GetPivotTableIcpFormatAsync(Project project, PivotRequest request)
    {
        var pivotDict = new Dictionary<string, Dictionary<string, decimal?>>();
        var allElements = new HashSet<string>();
        var allLabels = new HashSet<string>();

        // Run CPU-bound work on a thread pool thread to avoid blocking if processing is heavy
        // or process synchronously if it's light. Here we keep it simple.

        foreach (var rawRow in project.RawDataRows.OrderBy(r => r.DataId))
        {
            var rowData = ParseRowData(rawRow);
            if (rowData == null) continue;

            var solutionLabel = GetSolutionLabel(rawRow, rowData);
            if (string.IsNullOrWhiteSpace(solutionLabel)) continue;

            allLabels.Add(solutionLabel);

            var elementName = GetStringValue(rowData, "Element");
            if (string.IsNullOrWhiteSpace(elementName)) continue;

            allElements.Add(elementName);

            var corrCon = GetDecimalValue(rowData, "Corr Con");

            if (request.UseOxide && corrCon.HasValue)
            {
                var elementSymbol = ExtractElementSymbol(elementName);
                if (OxideFactors.Factors.TryGetValue(elementSymbol, out var oxide))
                {
                    corrCon = corrCon.Value * oxide.Factor;
                }
            }

            if (!pivotDict.ContainsKey(solutionLabel))
            {
                pivotDict[solutionLabel] = new Dictionary<string, decimal?>();
            }
            pivotDict[solutionLabel][elementName] = corrCon;
        }

        // Apply filters
        var filteredLabels = allLabels.AsEnumerable();

        if (request.SelectedSolutionLabels?.Any() == true)
        {
            filteredLabels = filteredLabels.Where(l => request.SelectedSolutionLabels.Contains(l));
        }

        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            var search = request.SearchText.ToLower();
            filteredLabels = filteredLabels.Where(l => l.ToLower().Contains(search));
        }

        var filteredList = filteredLabels.ToList();
        var totalCount = filteredList.Count;

        var pagedLabels = filteredList
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var columns = allElements.OrderBy(e => e).ToList();
        if (request.SelectedElements?.Any() == true)
        {
            columns = columns.Where(c =>
                request.SelectedElements.Any(e => c.Contains(e, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        int index = 0;
        var rows = new List<PivotRowDto>();
        foreach (var label in pagedLabels)
        {
            var values = pivotDict.TryGetValue(label, out var v) ? v : new Dictionary<string, decimal?>();
            var roundedValues = RoundValues(values, request.DecimalPlaces);
            rows.Add(new PivotRowDto(label, roundedValues, index++));
        }

        var allData = filteredList.Select(l => (l, pivotDict.TryGetValue(l, out var v) ? v : new Dictionary<string, decimal?>(), 0)).ToList();
        var columnStats = CalculateColumnStats(allData, columns);

        return Result<PivotResultDto>.Success(new PivotResultDto(
            columns,
            rows,
            totalCount,
            request.Page,
            request.PageSize,
            new PivotMetadataDto(
                allLabels.OrderBy(l => l).ToList(),
                allElements.OrderBy(e => e).ToList(),
                columnStats
            )
        ));
    }

    private async Task<Result<PivotResultDto>> GetPivotTableTabularFormatAsync(Project project, PivotRequest request)
    {
        var pivotData = new List<(string SolutionLabel, Dictionary<string, decimal?> Values, int Index)>();
        var allElements = new HashSet<string>();

        int index = 0;
        foreach (var rawRow in project.RawDataRows.OrderBy(r => r.DataId))
        {
            var rowData = ParseRowData(rawRow);
            if (rowData == null) continue;

            var solutionLabel = GetSolutionLabel(rawRow, rowData);
            if (string.IsNullOrWhiteSpace(solutionLabel)) continue;

            var values = new Dictionary<string, decimal?>();

            foreach (var kvp in rowData)
            {
                if (IsMetadataColumn(kvp.Key)) continue;

                var element = ExtractElementName(kvp.Key);
                if (string.IsNullOrEmpty(element)) continue;

                allElements.Add(kvp.Key);

                decimal? value = ParseDecimalValue(kvp.Value);

                if (request.UseOxide && value.HasValue)
                {
                    var elementSymbol = ExtractElementSymbol(element);
                    if (OxideFactors.Factors.TryGetValue(elementSymbol, out var oxide))
                    {
                        value = value.Value * oxide.Factor;
                    }
                }

                values[kvp.Key] = value;
            }

            pivotData.Add((solutionLabel, values, index++));
        }

        var filteredData = pivotData.AsEnumerable();

        if (request.SelectedSolutionLabels?.Any() == true)
        {
            filteredData = filteredData.Where(d => request.SelectedSolutionLabels.Contains(d.SolutionLabel));
        }

        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            var search = request.SearchText.ToLower();
            filteredData = filteredData.Where(d =>
                d.SolutionLabel.ToLower().Contains(search) ||
                d.Values.Any(v => v.Value?.ToString().Contains(search) == true));
        }

        if (request.NumberFilters?.Any() == true)
        {
            foreach (var filter in request.NumberFilters)
            {
                var column = filter.Key;
                var numberFilter = filter.Value;

                filteredData = filteredData.Where(d =>
                {
                    if (!d.Values.TryGetValue(column, out var val) || !val.HasValue)
                        return true;

                    if (numberFilter.Min.HasValue && val.Value < numberFilter.Min.Value)
                        return false;
                    if (numberFilter.Max.HasValue && val.Value > numberFilter.Max.Value)
                        return false;

                    return true;
                });
            }
        }

        var filteredList = filteredData.ToList();
        var totalCount = filteredList.Count;

        var pagedData = filteredList
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var columns = allElements.OrderBy(e => e).ToList();
        if (request.SelectedElements?.Any() == true)
        {
            columns = columns.Where(c => request.SelectedElements.Any(e => c.StartsWith(e))).ToList();
        }

        var rows = pagedData.Select(d => new PivotRowDto(
            d.SolutionLabel,
            RoundValues(d.Values, request.DecimalPlaces),
            d.Index
        )).ToList();

        var columnStats = CalculateColumnStats(filteredList, columns);

        var metadata = new PivotMetadataDto(
            pivotData.Select(d => d.SolutionLabel).Distinct().OrderBy(s => s).ToList(),
            allElements.OrderBy(e => e).ToList(),
            columnStats
        );

        return Result<PivotResultDto>.Success(new PivotResultDto(
            columns,
            rows,
            totalCount,
            request.Page,
            request.PageSize,
            metadata
        ));
    }

    #endregion

    #region Advanced Pivot with GCD/Repeats

    public async Task<Result<AdvancedPivotResultDto>> GetAdvancedPivotTableAsync(AdvancedPivotRequest request)
    {
        try
        {
            var project = await _db.Projects
                .AsNoTracking()
                .Include(p => p.RawDataRows)
                .FirstOrDefaultAsync(p => p.ProjectId == request.ProjectId);

            if (project == null)
                return Result<AdvancedPivotResultDto>.Fail("Project not found");

            if (!project.RawDataRows.Any())
                return Result<AdvancedPivotResultDto>.Fail("Project has no data");

            // 1. Parse all raw data into structured format
            var rawData = new List<ParsedSampleRow>();
            int index = 0;

            foreach (var rawRow in project.RawDataRows.OrderBy(r => r.DataId))
            {
                var rowData = ParseRowData(rawRow);
                if (rowData == null) continue;

                var solutionLabel = GetSolutionLabel(rawRow, rowData);
                if (string.IsNullOrWhiteSpace(solutionLabel)) continue;

                var type = GetStringValue(rowData, "Type");
                if (type != "Samp" && type != "Sample")
                    continue;

                var element = GetStringValue(rowData, "Element");
                if (string.IsNullOrEmpty(element)) continue;

                var valueColumn = request.UseInt ? "Int" : "Corr Con";
                var value = GetDecimalValue(rowData, valueColumn);

                rawData.Add(new ParsedSampleRow(
                    solutionLabel,
                    element,
                    value,
                    index++,
                    rowData
                ));
            }

            if (!rawData.Any())
                return Result<AdvancedPivotResultDto>.Fail("No sample data found");

            // 2. Calculate set sizes
            var setSizes = CalculateSetSizes(rawData);

            // 3. Detect repeats
            var (hasRepeats, repeatedElements) = DetectRepeatedElements(rawData, setSizes);

            // 4. Build pivot table
            var allElements = new HashSet<string>();
            var pivotRows = new List<AdvancedPivotRowDto>();

            var groupedBySolution = rawData.GroupBy(r => r.SolutionLabel);

            foreach (var solutionGroup in groupedBySolution)
            {
                var solutionLabel = solutionGroup.Key;
                var setSize = setSizes.GetValueOrDefault(solutionLabel, 1);
                var rows = solutionGroup.ToList();

                // Divide into sets (CORRECTED LOGIC)
                var sets = DivideIntoSets(rows, setSize);

                for (int setIndex = 0; setIndex < sets.Count; setIndex++)
                {
                    var setRows = sets[setIndex];
                    var values = new Dictionary<string, decimal?>();

                    var elementGroups = setRows.GroupBy(r => r.Element);

                    foreach (var elementGroup in elementGroups)
                    {
                        var elementName = elementGroup.Key;
                        var elementValues = elementGroup.Where(e => e.Value.HasValue).Select(e => e.Value!.Value).ToList();

                        if (hasRepeats && elementGroup.Count() > 1 && !request.MergeRepeats)
                        {
                            int repeatIndex = 1;
                            foreach (var row in elementGroup)
                            {
                                var columnName = $"{elementName}_{repeatIndex}";
                                allElements.Add(columnName);

                                decimal? finalValue = row.Value;
                                if (request.UseOxide && finalValue.HasValue)
                                {
                                    finalValue = ApplyOxideConversion(elementName, finalValue.Value);
                                }
                                values[columnName] = finalValue;
                                repeatIndex++;
                            }
                        }
                        else
                        {
                            allElements.Add(elementName);
                            decimal? aggregatedValue = AggregateValues(elementValues, request.Aggregation);

                            if (request.UseOxide && aggregatedValue.HasValue)
                            {
                                aggregatedValue = ApplyOxideConversion(elementName, aggregatedValue.Value);
                            }
                            values[elementName] = aggregatedValue;
                        }
                    }

                    var firstRowIndex = setRows.FirstOrDefault()?.OriginalIndex ?? 0;

                    pivotRows.Add(new AdvancedPivotRowDto(
                        solutionLabel,
                        RoundValues(values, request.DecimalPlaces),
                        firstRowIndex,
                        setIndex,
                        sets.Count
                    ));
                }
            }

            // 5. Apply filters
            var filteredRows = pivotRows.AsEnumerable();

            if (request.SelectedSolutionLabels?.Any() == true)
            {
                filteredRows = filteredRows.Where(r => request.SelectedSolutionLabels.Contains(r.SolutionLabel));
            }

            if (!string.IsNullOrWhiteSpace(request.SearchText))
            {
                var search = request.SearchText.ToLower();
                filteredRows = filteredRows.Where(r => r.SolutionLabel.ToLower().Contains(search));
            }

            var filteredList = filteredRows.OrderBy(r => r.OriginalIndex).ToList();
            var totalCount = filteredList.Count;

            // 6. Pagination
            var pagedRows = filteredList
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList();

            // 7. Filter columns
            var columns = allElements.OrderBy(e => e).ToList();
            if (request.SelectedElements?.Any() == true)
            {
                columns = columns.Where(c => request.SelectedElements.Any(e => c.StartsWith(e))).ToList();
            }

            // 8. Stats
            var statsData = filteredList.Select(r => (r.SolutionLabel, r.Values, r.OriginalIndex)).ToList();
            var columnStats = CalculateColumnStats(statsData, columns);

            var metadata = new AdvancedPivotMetadataDto(
                pivotRows.Select(r => r.SolutionLabel).Distinct().OrderBy(s => s).ToList(),
                allElements.OrderBy(e => e).ToList(),
                columnStats,
                hasRepeats,
                setSizes,
                repeatedElements
            );

            return Result<AdvancedPivotResultDto>.Success(new AdvancedPivotResultDto(
                columns,
                pagedRows,
                totalCount,
                request.Page,
                request.PageSize,
                metadata
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get advanced pivot table for project {ProjectId}", request.ProjectId);
            return Result<AdvancedPivotResultDto>.Fail($"Failed to get advanced pivot table: {ex.Message}");
        }
    }

    public async Task<Result<RepeatAnalysisDto>> AnalyzeRepeatsAsync(Guid projectId)
    {
        try
        {
            var project = await _db.Projects
                .AsNoTracking()
                .Include(p => p.RawDataRows)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);

            if (project == null)
                return Result<RepeatAnalysisDto>.Fail("Project not found");

            var rawData = new List<ParsedSampleRow>();
            int index = 0;

            foreach (var rawRow in project.RawDataRows.OrderBy(r => r.DataId))
            {
                var rowData = ParseRowData(rawRow);
                if (rowData == null) continue;

                var solutionLabel = GetSolutionLabel(rawRow, rowData);
                if (string.IsNullOrWhiteSpace(solutionLabel)) continue;

                var type = GetStringValue(rowData, "Type");
                if (type != "Samp" && type != "Sample") continue;

                var element = GetStringValue(rowData, "Element");
                if (string.IsNullOrEmpty(element)) continue;

                rawData.Add(new ParsedSampleRow(solutionLabel, element, null, index++, null));
            }

            var setSizes = CalculateSetSizes(rawData);
            var (hasRepeats, repeatedElements) = DetectRepeatedElements(rawData, setSizes);

            var elementCounts = rawData
                .GroupBy(r => r.Element)
                .ToDictionary(g => g.Key, g => g.Count());

            return Result<RepeatAnalysisDto>.Success(new RepeatAnalysisDto(
                hasRepeats,
                rawData.Count,
                setSizes,
                repeatedElements,
                elementCounts
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze repeats for project {ProjectId}", projectId);
            return Result<RepeatAnalysisDto>.Fail($"Failed to analyze repeats: {ex.Message}");
        }
    }

    #endregion

    #region Other Methods

    public async Task<Result<List<string>>> GetSolutionLabelsAsync(Guid projectId)
    {
        try
        {
            var project = await _db.Projects
                .AsNoTracking()
                .Include(p => p.RawDataRows)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);

            if (project == null) return Result<List<string>>.Fail("Project not found");

            var labels = new HashSet<string>();
            foreach (var rawRow in project.RawDataRows)
            {
                var rowData = ParseRowData(rawRow);
                if (rowData == null) continue;
                var label = GetSolutionLabel(rawRow, rowData);
                if (!string.IsNullOrWhiteSpace(label)) labels.Add(label);
            }

            return Result<List<string>>.Success(labels.OrderBy(l => l).ToList());
        }
        catch (Exception ex)
        {
            return Result<List<string>>.Fail(ex.Message);
        }
    }

    public async Task<Result<List<string>>> GetElementsAsync(Guid projectId)
    {
        try
        {
            var project = await _db.Projects
                .AsNoTracking()
                .Include(p => p.RawDataRows)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);

            if (project == null) return Result<List<string>>.Fail("Project not found");

            var elements = new HashSet<string>();
            foreach (var rawRow in project.RawDataRows)
            {
                var rowData = ParseRowData(rawRow);
                if (rowData == null) continue;

                if (rowData.TryGetValue("Element", out var elementVal) && elementVal != null)
                {
                    var name = elementVal.ToString();
                    if (!string.IsNullOrWhiteSpace(name)) elements.Add(name);
                }
                else
                {
                    foreach (var key in rowData.Keys)
                    {
                        if (!IsMetadataColumn(key))
                        {
                            var element = ExtractElementName(key);
                            if (!string.IsNullOrEmpty(element)) elements.Add(key);
                        }
                    }
                }
            }
            return Result<List<string>>.Success(elements.OrderBy(e => e).ToList());
        }
        catch (Exception ex)
        {
            return Result<List<string>>.Fail(ex.Message);
        }
    }

    public async Task<Result<List<DuplicateResultDto>>> DetectDuplicatesAsync(DuplicateDetectionRequest request)
    {
        try
        {
            var pivotResult = await GetPivotTableAsync(new PivotRequest(
                request.ProjectId,
                null, null, null, null, false, 2, 1, 10000
            ));

            if (!pivotResult.Succeeded)
                return Result<List<DuplicateResultDto>>.Fail(pivotResult.Messages.FirstOrDefault() ?? "Failed");

            var pivotData = pivotResult.Data!;
            var patterns = request.DuplicatePatterns ?? DefaultDuplicatePatterns.ToList();
            var duplicates = new List<DuplicateResultDto>();
            var patternRegex = new Regex($@"\b({string.Join("|", patterns.Select(Regex.Escape))})\b", RegexOptions.IgnoreCase);

            var duplicateRows = pivotData.Rows.Where(r => patternRegex.IsMatch(r.SolutionLabel)).ToList();

            foreach (var dupRow in duplicateRows)
            {
                var baseNumber = ExtractBaseNumber(dupRow.SolutionLabel);
                if (string.IsNullOrEmpty(baseNumber)) continue;

                var mainRow = pivotData.Rows.FirstOrDefault(r =>
                    !patternRegex.IsMatch(r.SolutionLabel) && r.SolutionLabel.Contains(baseNumber));

                if (mainRow == null) continue;

                var differences = new List<ElementDiffDto>();
                bool hasOutOfRange = false;

                foreach (var col in pivotData.Columns)
                {
                    var mainVal = mainRow.Values.GetValueOrDefault(col);
                    var dupVal = dupRow.Values.GetValueOrDefault(col);
                    decimal? diffPercent = null;
                    bool isInRange = true;

                    if (mainVal.HasValue && dupVal.HasValue && mainVal.Value != 0)
                    {
                        diffPercent = Math.Abs((dupVal.Value - mainVal.Value) / mainVal.Value) * 100;
                        isInRange = diffPercent <= request.ThresholdPercent;
                        if (!isInRange) hasOutOfRange = true;
                    }

                    differences.Add(new ElementDiffDto(col, mainVal, dupVal, diffPercent.HasValue ? Math.Round(diffPercent.Value, 2) : null, isInRange));
                }

                duplicates.Add(new DuplicateResultDto(mainRow.SolutionLabel, dupRow.SolutionLabel, differences, hasOutOfRange));
            }

            return Result<List<DuplicateResultDto>>.Success(duplicates);
        }
        catch (Exception ex)
        {
            return Result<List<DuplicateResultDto>>.Fail(ex.Message);
        }
    }

    public async Task<Result<Dictionary<string, ColumnStatsDto>>> GetColumnStatsAsync(Guid projectId)
    {
        try
        {
            var pivotResult = await GetPivotTableAsync(new PivotRequest(projectId, null, null, null, null, false, 2, 1, 10000));
            if (!pivotResult.Succeeded) return Result<Dictionary<string, ColumnStatsDto>>.Fail("Failed");
            return Result<Dictionary<string, ColumnStatsDto>>.Success(pivotResult.Data!.Metadata.ColumnStats);
        }
        catch (Exception ex)
        {
            return Result<Dictionary<string, ColumnStatsDto>>.Fail(ex.Message);
        }
    }

    public async Task<Result<byte[]>> ExportToCsvAsync(PivotRequest request)
    {
        try
        {
            var exportRequest = request with { Page = 1, PageSize = int.MaxValue };
            var pivotResult = await GetPivotTableAsync(exportRequest);
            if (!pivotResult.Succeeded) return Result<byte[]>.Fail("Failed to get data");

            var pivot = pivotResult.Data!;
            var sb = new StringBuilder();
            sb.AppendLine("Solution Label," + string.Join(",", pivot.Columns));

            foreach (var row in pivot.Rows)
            {
                var values = pivot.Columns.Select(c =>
                    row.Values.TryGetValue(c, out var v) && v.HasValue ? v.Value.ToString() : "");
                sb.AppendLine($"{row.SolutionLabel},{string.Join(",", values)}");
            }

            return Result<byte[]>.Success(Encoding.UTF8.GetBytes(sb.ToString()));
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail(ex.Message);
        }
    }

    #endregion

    #region Private Helpers

    // Define the record internally
    private record ParsedSampleRow(
        string SolutionLabel,
        string Element,
        decimal? Value,
        int OriginalIndex,
        Dictionary<string, object?>? RawData
    );

    private Dictionary<string, int> CalculateSetSizes(List<ParsedSampleRow> rawData)
    {
        var setSizes = new Dictionary<string, int>();
        var groupedBySolution = rawData.GroupBy(r => r.SolutionLabel);

        foreach (var group in groupedBySolution)
        {
            var rows = group.ToList();
            var elementCounts = rows.GroupBy(r => r.Element).Select(g => g.Count()).ToArray();

            if (elementCounts.Length > 0)
            {
                int gcd = elementCounts.Aggregate(GCD);
                int totalRows = rows.Count;
                if (gcd > 0 && totalRows % gcd == 0)
                {
                    setSizes[group.Key] = totalRows / gcd;
                }
                else
                {
                    setSizes[group.Key] = totalRows;
                }
            }
            else
            {
                setSizes[group.Key] = 1;
            }
        }
        return setSizes;
    }

    private (bool HasRepeats, Dictionary<string, List<string>> RepeatedElements) DetectRepeatedElements(
       List<ParsedSampleRow> rawData,
       Dictionary<string, int> setSizes)
    {
        var repeatedElements = new Dictionary<string, List<string>>();
        bool hasRepeats = false;

        var groupedBySolution = rawData.GroupBy(r => r.SolutionLabel);

        foreach (var solutionGroup in groupedBySolution)
        {
            var solutionLabel = solutionGroup.Key;
            var setSize = setSizes.GetValueOrDefault(solutionLabel, 1);
            var rows = solutionGroup.ToList();

            var sets = DivideIntoSets(rows, setSize);
            var repeatedInSolution = new List<string>();

            foreach (var set in sets)
            {
                var elementCounts = set
                    .GroupBy(r => r.Element)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key);

                foreach (var element in elementCounts)
                {
                    if (!repeatedInSolution.Contains(element))
                    {
                        repeatedInSolution.Add(element);
                        hasRepeats = true;
                    }
                }
            }

            if (repeatedInSolution.Any())
            {
                repeatedElements[solutionLabel] = repeatedInSolution;
            }
        }
        return (hasRepeats, repeatedElements);
    }

    private List<List<ParsedSampleRow>> DivideIntoSets(List<ParsedSampleRow> rows, int setSize)
    {
        var sets = new List<List<ParsedSampleRow>>();
        if (setSize <= 0 || rows.Count == 0 || rows.Count % setSize != 0)
        {
            sets.Add(rows);
            return sets;
        }

        // CORRECT LOGIC: rowsPerSet = Total rows / Number of Sets (setSize)
        int rowsPerSet = rows.Count / setSize;

        for (int i = 0; i < rows.Count; i += rowsPerSet)
        {
            var set = rows.Skip(i).Take(rowsPerSet).ToList();
            if (set.Any()) sets.Add(set);
        }
        return sets;
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

    private decimal? AggregateValues(List<decimal> values, PivotAggregation aggregation)
    {
        if (!values.Any()) return null;
        return aggregation switch
        {
            PivotAggregation.First => values.First(),
            PivotAggregation.Last => values.Last(),
            PivotAggregation.Mean => (decimal)values.Average(v => (double)v),
            PivotAggregation.Sum => values.Sum(),
            PivotAggregation.Min => values.Min(),
            PivotAggregation.Max => values.Max(),
            PivotAggregation.Count => values.Count,
            _ => values.First()
        };
    }

    private decimal ApplyOxideConversion(string elementName, decimal value)
    {
        var elementSymbol = ExtractElementSymbol(elementName);
        if (OxideFactors.Factors.TryGetValue(elementSymbol, out var oxide))
        {
            return value * oxide.Factor;
        }
        return value;
    }

    private Dictionary<string, object?>? ParseRowData(RawDataRow rawRow)
    {
        if (string.IsNullOrWhiteSpace(rawRow.ColumnData)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, object?>>(rawRow.ColumnData); }
        catch { return null; }
    }

    private string? GetSolutionLabel(RawDataRow rawRow, Dictionary<string, object?> rowData)
    {
        if (!string.IsNullOrWhiteSpace(rawRow.SampleId)) return rawRow.SampleId;
        if (rowData.TryGetValue("Solution Label", out var sl) && sl != null) return sl.ToString();
        if (rowData.TryGetValue("SolutionLabel", out var sl2) && sl2 != null) return sl2.ToString();
        return null;
    }

    private string? GetStringValue(Dictionary<string, object?> rowData, string key)
    {
        if (rowData.TryGetValue(key, out var value) && value != null)
        {
            if (value is JsonElement je) return je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
            return value.ToString();
        }
        return null;
    }

    private decimal? GetDecimalValue(Dictionary<string, object?> rowData, string key)
    {
        if (rowData.TryGetValue(key, out var value)) return ParseDecimalValue(value);
        return null;
    }

    private bool IsMetadataColumn(string columnName)
    {
        var metadataColumns = new[] { "Solution Label", "SolutionLabel", "SampleId", "Sample ID", "Type", "Act Wgt", "Act Vol", "DF", "Element", "Int", "Corr Con" };
        return metadataColumns.Contains(columnName, StringComparer.OrdinalIgnoreCase);
    }

    private string? ExtractElementName(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return null;
        return Regex.Replace(columnName, @"_\d+$", "");
    }

    private string ExtractElementSymbol(string elementName)
    {
        var match = Regex.Match(elementName, @"^([A-Z][a-z]?)");
        return match.Success ? match.Groups[1].Value : elementName;
    }

    private string? ExtractBaseNumber(string label)
    {
        var match = Regex.Match(label, @"(\d+[-]?\d*)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private decimal? ParseDecimalValue(object? value)
    {
        if (value == null) return null;
        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetDecimal(out var d)) return d;
            if (je.ValueKind == JsonValueKind.String && decimal.TryParse(je.GetString(), out var d2)) return d2;
        }
        else if (decimal.TryParse(value.ToString(), out var d3)) return d3;
        return null;
    }

    private Dictionary<string, decimal?> RoundValues(Dictionary<string, decimal?> values, int decimalPlaces)
    {
        return values.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.HasValue ? Math.Round(kvp.Value.Value, decimalPlaces) : (decimal?)null);
    }

    private Dictionary<string, ColumnStatsDto> CalculateColumnStats(
        List<(string SolutionLabel, Dictionary<string, decimal?> Values, int Index)> data,
        List<string> columns)
    {
        var stats = new Dictionary<string, ColumnStatsDto>();
        foreach (var col in columns)
        {
            var values = data.Select(d => d.Values.GetValueOrDefault(col)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            if (!values.Any())
            {
                stats[col] = new ColumnStatsDto(null, null, null, null, 0);
                continue;
            }
            var min = values.Min();
            var max = values.Max();
            var mean = values.Average();
            var count = values.Count;
            decimal? stdDev = null;
            if (count > 1)
            {
                var sumSquares = values.Sum(v => (v - (decimal)mean) * (v - (decimal)mean));
                stdDev = (decimal)Math.Sqrt((double)(sumSquares / (count - 1)));
            }
            stats[col] = new ColumnStatsDto(min, max, (decimal)Math.Round(mean, 4), stdDev.HasValue ? Math.Round(stdDev.Value, 4) : null, count);
        }
        return stats;
    }

    #endregion
}