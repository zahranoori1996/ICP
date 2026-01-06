using Application.DTOs;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Wrapper;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Infrastructure.Services;

/// <summary>
/// Drift Correction Service - Python-aligned behavior
/// Key fixes:
/// - RM detection follows Python keyword prefix (default "RM"); regex mode only when BasePattern is provided
/// - RM exclusion in correction uses keyword full-match when in Python mode
/// - Segment boundaries and correction ranges use PivotIndex consistently
/// - Element drift in ANALYZE is computed per-segment refRM (Python-like), fallback to last/first
/// - Apply updates Corr Con per (SolutionLabel, GroupId, Element) in DB + snapshot undo
/// - ✅ DebugDumpRm (Request) -> returns DebugRmDump in response + prints to stdout for journalctl
/// </summary>
public sealed class DriftCorrectionService : IDriftCorrectionService
{
    private readonly IsatisDbContext _db;
    private readonly IChangeLogService _changeLogService;
    private readonly ILogger<DriftCorrectionService> _logger;

    private const string DefaultBasePattern = @"^\s*RM";
    private const int MaxCorrectedSamplesInResponse = 1500;

    public DriftCorrectionService(
        IsatisDbContext db,
        IChangeLogService changeLogService,
        ILogger<DriftCorrectionService> logger)
    {
        _db = db;
        _changeLogService = changeLogService;
        _logger = logger;
    }

    #region Public API

    public Task<Result<DriftCorrectionResult>> AnalyzeDriftAsync(DriftCorrectionRequest request)
        => ExecuteCoreAsync(request, applyToDatabase: false);

    public Task<Result<DriftCorrectionResult>> ApplyDriftCorrectionAsync(DriftCorrectionRequest request)
        => ExecuteCoreAsync(request, applyToDatabase: true);

    public async Task<Result<List<DriftSegment>>> DetectSegmentsAsync(Guid projectId, string? basePattern = null, string? conePattern = null)
    {
        try
        {
            var processed = await LoadAndProcessDataAsync(projectId, tracking: false);
            if (processed.PivotRows.Count == 0)
                return Result<List<DriftSegment>>.Fail("No data found.");

            var keyword = ResolveKeyword(null, basePattern);
            var baseRx = keyword == null ? BuildRegex(basePattern, DefaultBasePattern) : null;
            var coneRx = BuildRegexOrNull(conePattern);

            var rmData = ExtractRmData(processed.PivotRows, baseRx, keyword);
            var segments = BuildSegments(rmData, coneRx);

            var dtos = segments.Select(s => new DriftSegment(
                s.SegmentId,
                s.StartIndex,
                s.EndIndex,
                s.StartStandard,
                s.EndStandard,
                s.SampleCount
            )).ToList();

            return Result<List<DriftSegment>>.Success(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DetectSegments failed for {ProjectId}", projectId);
            return Result<List<DriftSegment>>.Fail(ex.Message);
        }
    }

    public async Task<Result<Dictionary<string, List<decimal>>>> CalculateDriftRatiosAsync(Guid projectId, List<string>? elements = null)
    {
        try
        {
            var processed = await LoadAndProcessDataAsync(projectId, tracking: false);
            if (processed.PivotRows.Count == 0)
                return Result<Dictionary<string, List<decimal>>>.Fail("No data found.");

            var keyword = ResolveKeyword(null, null);
            var baseRx = keyword == null ? BuildRegex(null, DefaultBasePattern) : null;
            var rmData = ExtractRmData(processed.PivotRows, baseRx, keyword);

            var allElements = elements ?? GetAllElements(processed.PivotRows);
            var ratios = new Dictionary<string, List<decimal>>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in allElements)
            {
                var list = new List<decimal>();
                var rms = rmData
                    .Where(r => r.Values.TryGetValue(element, out var v) && v.HasValue && v.Value != 0m)
                    .OrderBy(r => r.PivotIndex)
                    .ToList();

                for (int i = 0; i < rms.Count - 1; i++)
                {
                    var a = rms[i].Values[element]!.Value;
                    var b = rms[i + 1].Values[element]!.Value;
                    list.Add(b / a);
                }

                ratios[element] = list;
            }

            return Result<Dictionary<string, List<decimal>>>.Success(ratios);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CalculateDriftRatios failed for {ProjectId}", projectId);
            return Result<Dictionary<string, List<decimal>>>.Fail(ex.Message);
        }
    }

    public Task<Result<SlopeOptimizationResult>> OptimizeSlopeAsync(SlopeOptimizationRequest request)
        => Task.FromResult(Result<SlopeOptimizationResult>.Fail("Not implemented"));

    public Task<Result<SlopeOptimizationResult>> ZeroSlopeAsync(Guid projectId, string element)
        => Task.FromResult(Result<SlopeOptimizationResult>.Fail("Not implemented"));

    public async Task<Result<string>> UndoLastActionAsync(Guid projectId)
    {
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var lastState = await _db.ProjectStates
                .Where(s => s.ProjectId == projectId && s.Description != null && s.Description.StartsWith("Undo:", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefaultAsync();

            if (lastState == null)
                return Result<string>.Fail("No undo state found.");

            if (string.IsNullOrWhiteSpace(lastState.Data))
                return Result<string>.Fail("Undo state data is empty.");

            var snapshotItems = JsonSerializer.Deserialize<List<SavedRowData>>(lastState.Data);
            if (snapshotItems == null || snapshotItems.Count == 0)
                return Result<string>.Fail("Invalid undo snapshot data.");

            var currentRows = await _db.RawDataRows
                .Where(r => r.ProjectId == projectId)
                .ToListAsync();

            var currentMap = currentRows.ToDictionary(r => r.DataId);

            int restored = 0;
            foreach (var item in snapshotItems)
            {
                if (currentMap.TryGetValue(item.DataId, out var row))
                {
                    row.ColumnData = item.ColumnData;
                    row.SampleId = item.SampleId;
                    restored++;
                }
            }

            _db.ProjectStates.Remove(lastState);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return Result<string>.Success($"Undo successful. Restored {restored} rows.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Undo failed for {ProjectId}", projectId);
            return Result<string>.Fail(ex.Message);
        }
    }

    #endregion

    #region Core Execution

    private async Task<Result<DriftCorrectionResult>> ExecuteCoreAsync(DriftCorrectionRequest request, bool applyToDatabase)
    {
        try
        {
            Project? project = null;
            bool persistChanges = applyToDatabase && !request.PreviewOnly;
            bool computeCorrections = applyToDatabase || request.PreviewOnly;

            if (persistChanges)
            {
                project = await _db.Projects.FirstOrDefaultAsync(p => p.ProjectId == request.ProjectId);
                if (project == null)
                    return Result<DriftCorrectionResult>.Fail("Project not found");

                await SaveUndoStateAsync(request.ProjectId, $"DriftCorrection({request.Method})");
            }

            var processed = await LoadAndProcessDataAsync(request.ProjectId, tracking: persistChanges);
            if (processed.PivotRows.Count == 0)
                return Result<DriftCorrectionResult>.Fail("No data found.");

            var keyword = ResolveKeyword(request.Keyword, request.BasePattern);
            var baseRx = keyword == null ? BuildRegex(request.BasePattern, DefaultBasePattern) : null;
            var coneRx = BuildRegexOrNull(request.ConePattern);
            var pureRmRx = keyword == null ? null : BuildPureRmRegex(keyword);

            // RM rows and segments
            var rmData = ExtractRmData(processed.PivotRows, baseRx, keyword);
            var segments = BuildSegments(rmData, coneRx);

            // elements
            var elements = (request.SelectedElements == null || request.SelectedElements.Count == 0)
                ? GetAllElements(processed.PivotRows)
                : request.SelectedElements;

            // drift info (ANALYZE): per-segment refRM like Python, fallback to last/first
            var elementDrifts = new Dictionary<string, ElementDriftInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in elements)
            {
                var info = CalculateElementDrift(rmData, segments, element);
                if (info != null)
                    elementDrifts[element] = info;
            }

            var driftSegments = segments.Select(s => new DriftSegment(
                s.SegmentId,
                s.StartIndex,
                s.EndIndex,
                s.StartStandard,
                s.EndStandard,
                s.SampleCount
            )).ToList();

            // ✅ DEBUG DUMP (works only if DriftCorrectionRequest has DebugDumpRm property)
            DebugRmDumpDto? debugDump = null;
            if (request.DebugDumpRm)
            {
                var first10 = rmData.Take(10)
                    .Select(r => new DebugRmItemDto(r.PivotIndex, Safe(r.SolutionLabel), r.RmNum, r.RmType))
                    .ToList();

                var last10 = rmData.Skip(Math.Max(0, rmData.Count - 10))
                    .Select(r => new DebugRmItemDto(r.PivotIndex, Safe(r.SolutionLabel), r.RmNum, r.RmType))
                    .ToList();

                debugDump = new DebugRmDumpDto(rmData.Count, first10, last10);

                var sb = new StringBuilder();
                sb.AppendLine("=== DRIFT DEBUG RM DUMP ===");
                sb.AppendLine($"ProjectId: {request.ProjectId}");
                sb.AppendLine($"TotalSamples(PivotRows): {processed.PivotRows.Count}");
                sb.AppendLine($"RM Count: {rmData.Count}");
                sb.AppendLine($"SegmentsFound: {segments.Count}");
                if (segments.Count > 0)
                    sb.AppendLine($"Segment0 SampleCount: {segments[0].SampleCount}, Start: {segments[0].StartStandard}, End: {segments[0].EndStandard}, RefRmNum: {segments[0].RefRmNum}");
                sb.AppendLine("-- FIRST 10 RM --");
                foreach (var x in first10) sb.AppendLine($"{x.PivotIndex}\t{x.SolutionLabel}\tRmNum={x.RmNum}\tRmType={x.RmType}");
                sb.AppendLine("-- LAST 10 RM --");
                foreach (var x in last10) sb.AppendLine($"{x.PivotIndex}\t{x.SolutionLabel}\tRmNum={x.RmNum}\tRmType={x.RmType}");
                sb.AppendLine("=== END DRIFT DEBUG RM DUMP ===");

                var text = sb.ToString();
                _logger.LogInformation("{Dump}", text);
                Console.WriteLine(text); // force to stdout so journalctl sees it

                // extra debug for diagnosing 576 vs 577 and 36 vs 38
                DumpRmFrequency(rmData);
                DumpPivotHeadTail(processed.PivotRows, take: 10);
            }

            // ✅ ANALYZE MODE: do NOT generate correctedData / correctedSamples (Python parity)
            if (!computeCorrections)
            {
                var analyzeResult = new DriftCorrectionResult(
                    TotalSamples: processed.PivotRows.Count,
                    CorrectedSamples: 0,
                    SegmentsFound: segments.Count,
                    Segments: driftSegments,
                    ElementDrifts: elementDrifts,
                    CorrectedData: new List<CorrectedSampleDto>(),
                    DebugRmDump: debugDump
                );

                return Result<DriftCorrectionResult>.Success(analyzeResult);
            }

            // ------------------------------------------------------------
            // Apply mode
            // ------------------------------------------------------------

            var correctedIntensities = new Dictionary<PivotRow, Dictionary<string, decimal>>(ReferenceEqualityComparer<PivotRow>.Instance);
            var correctionFactors = new Dictionary<PivotRow, Dictionary<string, decimal>>(ReferenceEqualityComparer<PivotRow>.Instance);
            var allCorrectionsForDb = new Dictionary<(string Label, int GroupId, string Element), decimal>(TupleKeyComparer.Instance);

            foreach (var element in elements)
            {
                var corrections = request.Method == DriftMethod.None
                    ? new Dictionary<PivotRow, CorrectionResult>(ReferenceEqualityComparer<PivotRow>.Instance)
                    : keyword == null
                        ? (request.Method == DriftMethod.Stepwise
                            ? CalculateStepwiseCorrections(processed.PivotRows, rmData, segments, element, baseRx!)
                            : CalculateUniformCorrections(processed.PivotRows, rmData, segments, element, baseRx!))
                        : CalculatePythonCorrections(
                            processed.PivotRows,
                            rmData,
                            segments,
                            element,
                            pureRmRx!,
                            request.TargetRmNum,
                            request.Method == DriftMethod.Stepwise);

                foreach (var kv in corrections)
                {
                    var sample = kv.Key;
                    var res = kv.Value;

                    if (!correctedIntensities.TryGetValue(sample, out var dict))
                    {
                        dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                        correctedIntensities[sample] = dict;
                        correctionFactors[sample] = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                    }

                    dict[element] = res.CorrectedValue;
                    correctionFactors[sample][element] = res.CorrectionFactor;

                    allCorrectionsForDb[(sample.SolutionLabel, sample.GroupId, element)] = res.CorrectedValue;
                }
            }

            int savedCount = 0;
            if (persistChanges && allCorrectionsForDb.Count > 0)
            {
                savedCount = await SaveCorrectionsToDatabaseAsync(processed.RawRows, allCorrectionsForDb, elements);
                project!.LastModifiedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                await _changeLogService.LogChangeAsync(
                    request.ProjectId,
                    "DriftCorrection",
                    request.ChangedBy,
                    $"Applied {request.Method}. Updated values: {savedCount}"
                );
            }

            var correctedSamplesDto = new List<CorrectedSampleDto>(capacity: Math.Min(correctedIntensities.Count, MaxCorrectedSamplesInResponse));
            foreach (var sample in correctedIntensities.Keys)
            {
                if (correctedSamplesDto.Count >= MaxCorrectedSamplesInResponse)
                    break;

                var origValues = sample.Values
                    .Where(kv => correctedIntensities[sample].ContainsKey(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

                var corrValues = correctedIntensities[sample]
                    .ToDictionary(k => k.Key, v => (decimal?)v.Value, StringComparer.OrdinalIgnoreCase);

                var factors = correctionFactors[sample];

                correctedSamplesDto.Add(new CorrectedSampleDto(
                    sample.SolutionLabel,
                    sample.GroupId,
                    sample.OriginalIndex,
                    0,
                    origValues,
                    corrValues,
                    factors
                ));
            }

            var result = new DriftCorrectionResult(
                TotalSamples: processed.PivotRows.Count,
                CorrectedSamples: persistChanges ? savedCount : correctedIntensities.Count,
                SegmentsFound: segments.Count,
                Segments: driftSegments,
                ElementDrifts: elementDrifts,
                CorrectedData: correctedSamplesDto,
                DebugRmDump: debugDump
            );

            return Result<DriftCorrectionResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execute drift failed for {ProjectId}", request.ProjectId);
            return Result<DriftCorrectionResult>.Fail(ex.Message);
        }
    }

    private static string Safe(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();

    // ------------------------------------------------------------
    // Debug helpers (for parity diagnostics)
    // ------------------------------------------------------------
    private void DumpRmFrequency(List<PivotRow> rmData)
    {
        if (rmData == null || rmData.Count == 0)
        {
            _logger.LogInformation("-- RM FREQUENCY -- (rmData is empty)");
            Console.WriteLine("-- RM FREQUENCY -- (rmData is empty)");
            return;
        }

        var freq = rmData
            .GroupBy(r => ((r.SolutionLabel ?? string.Empty).Trim()), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key} -> {g.Count()}")
            .ToList();

        var msg = "-- RM FREQUENCY (by SolutionLabel) --\n" + string.Join("\n", freq);
        _logger.LogInformation("{Msg}", msg);
        Console.WriteLine(msg);
    }

    private void DumpPivotHeadTail(List<PivotRow> pivotRows, int take = 10)
    {
        if (pivotRows == null || pivotRows.Count == 0)
        {
            _logger.LogInformation("-- PIVOT DUMP -- (pivotRows is empty)");
            Console.WriteLine("-- PIVOT DUMP -- (pivotRows is empty)");
            return;
        }

        var first = pivotRows
            .Take(take)
            .Select(p => $"{p.PivotIndex}\t{(p.SolutionLabel ?? string.Empty).Replace("\t", " ")}\tGroupId={p.GroupId}")
            .ToList();

        var last = pivotRows
            .Skip(Math.Max(0, pivotRows.Count - take))
            .Select(p => $"{p.PivotIndex}\t{(p.SolutionLabel ?? string.Empty).Replace("\t", " ")}\tGroupId={p.GroupId}")
            .ToList();

        var msg1 = $"-- PIVOT FIRST {take} --\n" + string.Join("\n", first);
        var msg2 = $"-- PIVOT LAST {take} --\n" + string.Join("\n", last);

        _logger.LogInformation("{Msg}", msg1);
        _logger.LogInformation("{Msg}", msg2);
        Console.WriteLine(msg1);
        Console.WriteLine(msg2);
    }


    #endregion

    #region Data Loading + Pivot

    private async Task<ProcessedData> LoadAndProcessDataAsync(Guid projectId, bool tracking)
    {
        IQueryable<RawDataRow> q = _db.RawDataRows.Where(r => r.ProjectId == projectId).OrderBy(r => r.DataId);
        if (!tracking) q = q.AsNoTracking();

        var rawRows = await q.ToListAsync();
        return ProcessRawData(rawRows);
    }

    private ProcessedData ProcessRawData(List<RawDataRow> rawRows)
    {
        // Raw rows are already loaded ORDER BY DataId in LoadAndProcessDataAsync
        // We keep that ordering for OriginalIndex to match Python's "original_index" behavior.
        var parsed = new List<RawDataItem>(rawRows.Count);

        foreach (var row in rawRows)
        {
            if (string.IsNullOrWhiteSpace(row.ColumnData))
                continue;

            Dictionary<string, JsonElement>? data;
            try
            {
                data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.ColumnData);
            }
            catch
            {
                continue;
            }

            if (data == null)
                continue;

            var type = GetJsonString(data, "Type");
            if (string.IsNullOrWhiteSpace(type))
                continue;
            if (!string.Equals(type, "Samp", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(type, "Sample", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Match Python pivot behavior for label/element normalization.
            var solutionLabelRaw = GetJsonString(data, "Solution Label");
            var solutionLabel = string.IsNullOrWhiteSpace(solutionLabelRaw)
                ? "Unknown"
                : solutionLabelRaw.Trim();
            if (string.Equals(solutionLabel, "nan", StringComparison.OrdinalIgnoreCase))
                solutionLabel = "Unknown";

            var elementRaw = GetJsonString(data, "Element") ?? string.Empty;
            var element = elementRaw.Split('_')[0];

            var corrCon = GetJsonDecimal(data, "Corr Con");

            parsed.Add(new RawDataItem
            {
                DataId = row.DataId,
                SolutionLabel = solutionLabel,
                Element = element,
                CorrCon = corrCon,
                OriginalIndex = parsed.Count
            });
        }

        // ✅ Sequence-based pivot (this assigns GroupId internally)
        var pivot = CreatePivotTable(parsed);

        return new ProcessedData(rawRows, parsed, pivot);
    }

    private sealed class LabelGroupState
    {
        public int GroupId { get; set; }
        public HashSet<string> SeenElements { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private List<PivotRow> CreatePivotTable(List<RawDataItem> rows)
    {
        var ordered = rows.OrderBy(r => r.OriginalIndex).ToList();
        if (ordered.Count == 0)
            return new List<PivotRow>();

        // Group by Solution Label preserving original order.
        var groups = new Dictionary<string, List<RawDataItem>>(StringComparer.Ordinal);
        foreach (var item in ordered)
        {
            if (!groups.TryGetValue(item.SolutionLabel, out var list))
            {
                list = new List<RawDataItem>();
                groups[item.SolutionLabel] = list;
            }
            list.Add(item);
        }

        // Compute per-label group sizes using Python's gcd logic.
        var groupSizes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in groups)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in kv.Value)
            {
                counts[item.Element] = counts.TryGetValue(item.Element, out var c) ? c + 1 : 1;
            }

            int total = kv.Value.Count;
            int g = 1;
            if (counts.Count > 0)
            {
                g = counts.Values.First();
                foreach (var c in counts.Values.Skip(1))
                    g = Gcd(g, c);
            }

            int size = (g > 1 && total % g == 0) ? total / g : total;
            if (size <= 0) size = 1;
            groupSizes[kv.Key] = size;
        }

        // Detect repeated elements within a group (Python has_repeats).
        bool hasRepeats = false;
        var repeatCheck = new Dictionary<(string Label, int GroupId, string Element), int>(TupleKeyComparer.Instance);
        foreach (var kv in groups)
        {
            var list = kv.Value;
            int size = groupSizes[kv.Key];
            for (int i = 0; i < list.Count; i++)
            {
                int gid = size > 0 ? i / size : 0;
                var key = (kv.Key, gid, list[i].Element);

                var count = repeatCheck.TryGetValue(key, out var c) ? c + 1 : 1;
                repeatCheck[key] = count;

                if (count > 1)
                {
                    hasRepeats = true;
                    break;
                }
            }

            if (hasRepeats)
                break;
        }

        Dictionary<(string Label, int GroupId), PivotRow> rowMap;

        if (hasRepeats)
        {
            // Assign group id based on position and group size.
            foreach (var kv in groups)
            {
                var list = kv.Value;
                int size = groupSizes[kv.Key];
                for (int i = 0; i < list.Count; i++)
                    list[i].GroupId = size > 0 ? i / size : 0;
            }

            var occCount = new Dictionary<(string Label, int GroupId, string Element), int>(TupleKeyComparer.Instance);
            foreach (var item in ordered)
            {
                var key = (item.SolutionLabel, item.GroupId, item.Element);
                occCount[key] = occCount.TryGetValue(key, out var c) ? c + 1 : 1;
            }

            var occCounter = new Dictionary<(string Label, int GroupId, string Element), int>(TupleKeyComparer.Instance);
            rowMap = new Dictionary<(string Label, int GroupId), PivotRow>(LabelGroupComparer.Instance);

            foreach (var item in ordered)
            {
                var rowKey = (item.SolutionLabel, item.GroupId);
                if (!rowMap.TryGetValue(rowKey, out var row))
                {
                    row = new PivotRow
                    {
                        SolutionLabel = item.SolutionLabel,
                        GroupId = item.GroupId,
                        OriginalIndex = item.OriginalIndex,
                        Values = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase)
                    };
                    rowMap[rowKey] = row;
                }

                if (item.OriginalIndex < row.OriginalIndex)
                    row.OriginalIndex = item.OriginalIndex;

                var occKey = (item.SolutionLabel, item.GroupId, item.Element);
                var idx = occCounter.TryGetValue(occKey, out var cur) ? cur + 1 : 1;
                occCounter[occKey] = idx;

                var colName = occCount[occKey] > 1 ? $"{item.Element}_{idx}" : item.Element;
                row.Values[colName] = item.CorrCon;
            }
        }
        else
        {
            // Assign uid per (label, element) occurrence index (Python _uid).
            var uidMap = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            foreach (var item in ordered)
            {
                if (!uidMap.TryGetValue(item.SolutionLabel, out var elemMap))
                {
                    elemMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    uidMap[item.SolutionLabel] = elemMap;
                }

                var uid = elemMap.TryGetValue(item.Element, out var last) ? last + 1 : 0;
                elemMap[item.Element] = uid;
                item.GroupId = uid;
            }

            rowMap = new Dictionary<(string Label, int GroupId), PivotRow>(LabelGroupComparer.Instance);
            foreach (var item in ordered)
            {
                var rowKey = (item.SolutionLabel, item.GroupId);
                if (!rowMap.TryGetValue(rowKey, out var row))
                {
                    row = new PivotRow
                    {
                        SolutionLabel = item.SolutionLabel,
                        GroupId = item.GroupId,
                        OriginalIndex = item.OriginalIndex,
                        Values = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase)
                    };
                    rowMap[rowKey] = row;
                }

                if (item.OriginalIndex < row.OriginalIndex)
                    row.OriginalIndex = item.OriginalIndex;

                row.Values[item.Element] = item.CorrCon;
            }
        }

        var result = rowMap.Values.OrderBy(r => r.OriginalIndex).ToList();
        for (int i = 0; i < result.Count; i++)
            result[i].PivotIndex = i;

        return result;
    }

    private sealed class BlockLabelComparer : IEqualityComparer<(int BlockId, string Label)>
    {
        public bool Equals((int BlockId, string Label) x, (int BlockId, string Label) y)
            => x.BlockId == y.BlockId && string.Equals(x.Label, y.Label, StringComparison.Ordinal);

        public int GetHashCode((int BlockId, string Label) obj)
            => HashCode.Combine(obj.BlockId, StringComparer.Ordinal.GetHashCode(obj.Label ?? string.Empty));
    }

    private sealed class LabelGroupComparer : IEqualityComparer<(string Label, int GroupId)>
    {
        public static readonly LabelGroupComparer Instance = new();

        public bool Equals((string Label, int GroupId) x, (string Label, int GroupId) y)
            => x.GroupId == y.GroupId && string.Equals(x.Label, y.Label, StringComparison.Ordinal);

        public int GetHashCode((string Label, int GroupId) obj)
            => HashCode.Combine(obj.GroupId, StringComparer.Ordinal.GetHashCode(obj.Label ?? string.Empty));
    }

    #endregion

    #region RM + Segments

    private static Regex BuildRegex(string? pattern, string fallbackPattern)
    {
        var p = string.IsNullOrWhiteSpace(pattern) ? fallbackPattern : pattern!;
        return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private static Regex? BuildRegexOrNull(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        return new Regex(pattern!, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private static string? ResolveKeyword(string? keyword, string? basePattern)
    {
        if (!string.IsNullOrWhiteSpace(keyword))
            return keyword!.Trim();

        if (string.IsNullOrWhiteSpace(basePattern))
            return "RM";

        return null;
    }

    private static Regex BuildPureRmRegex(string keyword)
        => new Regex($"^\\s*{Regex.Escape(keyword)}\\s*\\d*\\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool IsPureRmLabel(string? label, Regex pureRmRx)
        => !string.IsNullOrWhiteSpace(label) && pureRmRx.IsMatch(label);

    private static bool IsKeywordPrefix(string? label, string keyword)
        => !string.IsNullOrWhiteSpace(label) &&
           label.Trim().StartsWith(keyword, StringComparison.OrdinalIgnoreCase);

    private static (int RmNum, string RmType) ExtractRmInfoPython(string label, string keyword)
    {
        if (string.IsNullOrWhiteSpace(label))
            return (0, "Base");

        var raw = label.Trim();
        var cleaned = Regex.Replace(
                raw,
                $"^\\s*{Regex.Escape(keyword)}\\s*[-_]?\\s*",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Trim()
            .ToLowerInvariant();

        var type = "Base";
        var typeMatch = Regex.Match(cleaned, @"(chek|check|cone)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var beforeText = cleaned;
        if (typeMatch.Success)
        {
            var typ = typeMatch.Groups[1].Value;
            type = typ.Equals("cone", StringComparison.OrdinalIgnoreCase) ? "Cone" : "Check";
            beforeText = cleaned.Substring(0, typeMatch.Index);
        }

        var nums = Regex.Matches(beforeText, @"\d+");
        if (nums.Count == 0)
            return (0, type);

        return int.TryParse(nums[^1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? (n, type)
            : (0, type);
    }

    private static List<PivotRow> ExtractRmData(List<PivotRow> pivotRows, Regex? baseRx, string? keyword)
    {
        if (pivotRows == null || pivotRows.Count == 0)
            return new List<PivotRow>();

        List<PivotRow> list;
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            list = pivotRows
                .Where(p => IsKeywordPrefix(p.SolutionLabel, keyword!))
                .OrderBy(p => p.PivotIndex)
                .ToList();

            foreach (var rm in list)
            {
                var (num, type) = ExtractRmInfoPython(rm.SolutionLabel ?? string.Empty, keyword!);
                rm.RmNum = num;
                rm.RmType = type;
            }

            return list;
        }

        if (baseRx == null)
            return new List<PivotRow>();

        list = pivotRows
            .Where(p => baseRx.IsMatch((p.SolutionLabel ?? string.Empty).Trim()))
            .OrderBy(p => p.PivotIndex)
            .ToList();

        foreach (var rm in list)
        {
            var (num, type) = ExtractRmInfo(rm.SolutionLabel ?? string.Empty);
            rm.RmNum = num;
            rm.RmType = type;
        }

        return list;
    }

    /// <summary>
    /// Returns true if the label represents a reference RM/CRM/STD row (not ordinary blank samples).
    /// </summary>
    private static bool IsReferenceRmLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return false;

        var raw = label.Trim();

        // ✅ Allow only "CRM BLANK ..." (reference blank)
        // Examples: "CRM BLANK  y", "CRM BLANK  R"
        if (Regex.IsMatch(raw, @"^\s*CRM\s+BLANK\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return true;

        // ❌ Reject ordinary BLANK/BLNK samples (non-reference)
        // Examples: "BLNK 1269", "BLNK NIT", "BLNK HFTIZ", "blank rar"
        if (Regex.IsMatch(raw, @"^\s*(BLNK|BLANK)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;

        // ✅ Accept RM / CRM / STD with a numeric id (>0)
        // Supports: "RM2", "RM 2", "CRM 258  y", "STD10", "STD 10"
        // IMPORTANT: do NOT use \b right after RM/CRM/STD (it breaks "RM2")
        if (Regex.IsMatch(raw, @"^\s*(RM|CRM|STD)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var num = ExtractLeadingNumberAfterToken(raw);
            return num > 0;
        }

        return false;
    }

    private static int ExtractLeadingNumberAfterToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        // strip leading token
        var cleaned = Regex.Replace(raw, @"^\s*(RM|CRM|STD)\s*[-_ ]*\s*", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

        var m = Regex.Match(cleaned, @"(\d+)", RegexOptions.CultureInvariant);
        if (!m.Success)
            return 0;

        return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }
    private static (int RmNum, string RmType) ExtractRmInfo(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return (0, "Base");

        var raw = label.Trim();
        var lower = raw.ToLowerInvariant();

        var type = "Base";
        if (Regex.IsMatch(lower, @"\bcone\b", RegexOptions.IgnoreCase))
            type = "Cone";
        else if (Regex.IsMatch(lower, @"\b(chek|check)\b", RegexOptions.IgnoreCase))
            type = "Check";

        var cleaned = Regex.Replace(raw, @"^\s*(RM|CRM|STD|BLANK)\s*[-_ ]*\s*", "", RegexOptions.IgnoreCase).Trim();

        var mNum = Regex.Match(cleaned, @"(\d+)");
        int num = 0;
        if (mNum.Success)
            int.TryParse(mNum.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out num);

        return (num, type);
    }

    private static List<SegmentInfo> BuildSegments(List<PivotRow> rmData, Regex? coneRx)
    {
        var segments = new List<SegmentInfo>();
        if (rmData.Count == 0) return segments;

        _ = coneRx; // Python uses only rm_type == "Cone" for segmentation.

        int segmentId = 0;
        int? refRmNum = null;

        int segStartIdx = 0;
        string segStartStd = rmData[0].SolutionLabel;

        for (int i = 0; i < rmData.Count; i++)
        {
            var rm = rmData[i];
            bool isCone = rm.RmType.Equals("Cone", StringComparison.OrdinalIgnoreCase);

            if (isCone && i != segStartIdx)
            {
                var prev = BuildSegment(segmentId, rmData, segStartIdx, i - 1, refRmNum, segStartStd);
                segments.Add(prev);

                segmentId++;
                refRmNum = null;
                segStartIdx = i;
                segStartStd = rm.SolutionLabel;
            }

            if (!refRmNum.HasValue
                && rm.RmNum > 0
                && (rm.RmType.Equals("Base", StringComparison.OrdinalIgnoreCase) || rm.RmType.Equals("Check", StringComparison.OrdinalIgnoreCase)))
                refRmNum = rm.RmNum;
        }

        segments.Add(BuildSegment(segmentId, rmData, segStartIdx, rmData.Count - 1, refRmNum, segStartStd));
        return segments;
    }

    private static SegmentInfo BuildSegment(int id, List<PivotRow> rmData, int start, int end, int? refRmNum, string startStd)
    {
        var rms = rmData.GetRange(start, end - start + 1);

        var positions = new List<PositionInfo>(rms.Count);
        for (int i = 0; i < rms.Count; i++)
        {
            var cur = rms[i];
            int min = (i == 0) ? -1 : rms[i - 1].PivotIndex;
            int max = cur.PivotIndex;

            positions.Add(new PositionInfo
            {
                SolutionLabel = cur.SolutionLabel,
                PivotIndex = cur.PivotIndex,
                Min = min,
                Max = max,
                RmNum = cur.RmNum,
                RmType = cur.RmType
            });
        }

        var seg = new SegmentInfo
        {
            SegmentId = id,
            RefRmNum = (refRmNum.HasValue && refRmNum.Value > 0)
                ? refRmNum.Value
                : positions.FirstOrDefault(p =>
                    p.RmNum > 0 &&
                    (p.RmType.Equals("Base", StringComparison.OrdinalIgnoreCase) || p.RmType.Equals("Check", StringComparison.OrdinalIgnoreCase)))?.RmNum ?? 0,
            Positions = positions,
            StartIndex = positions.First().Min,
            EndIndex = positions.Last().Max,
            StartStandard = startStd,
            EndStandard = positions.Last().SolutionLabel,
            SampleCount = positions.Count
        };

        return seg;
    }

    #endregion

    #region Drift Info (Python-like)

    private static ElementDriftInfo? CalculateElementDrift(
        List<PivotRow> rmData,
        List<SegmentInfo> segments,
        string element)
    {
        if (rmData == null || rmData.Count == 0)
            return null;

        foreach (var seg in segments)
        {
            if (seg.RefRmNum <= 0)
                continue;

            var refRmNum = seg.RefRmNum;

            var refRmRows = rmData
                .Where(r =>
                    r.RmNum == refRmNum &&
                    (r.RmType.Equals("Base", StringComparison.OrdinalIgnoreCase) ||
                     r.RmType.Equals("Check", StringComparison.OrdinalIgnoreCase)) &&
                    r.Values.TryGetValue(element, out var v) &&
                    v.HasValue && v.Value != 0m)
                .OrderBy(r => r.PivotIndex)
                .ToList();

            if (refRmRows.Count < 2)
                continue;

            var firstVal = refRmRows.First().Values[element]!.Value;
            var lastVal = refRmRows.Last().Values[element]!.Value;

            if (firstVal == 0m)
                continue;

            var finalRatio = lastVal / firstVal;
            var driftPercent = (finalRatio - 1m) * 100m;

            return new ElementDriftInfo(
                element,
                1m,
                finalRatio,
                driftPercent,
                0m,
                0m
            );
        }

        var fallback = rmData
            .Where(r =>
                r.Values.TryGetValue(element, out var v) &&
                v.HasValue && v.Value != 0m)
            .OrderBy(r => r.PivotIndex)
            .ToList();

        if (fallback.Count < 2)
            return null;

        var f0 = fallback.First().Values[element]!.Value;
        var f1 = fallback.Last().Values[element]!.Value;

        if (f0 == 0m)
            return null;

        var ratio = f1 / f0;
        var percent = (ratio - 1m) * 100m;

        return new ElementDriftInfo(
            element,
            1m,
            ratio,
            percent,
            0m,
            0m
        );
    }

    #endregion

    #region Corrections (Math - Fixed)

    // (Rest unchanged from your file...)

    private Dictionary<PivotRow, CorrectionResult> CalculatePythonCorrections(
        List<PivotRow> pivotData,
        List<PivotRow> rmData,
        List<SegmentInfo> segments,
        string element,
        Regex pureRmRx,
        int? targetRmNum,
        bool stepwise)
    {
        var corrections = new Dictionary<PivotRow, CorrectionResult>(ReferenceEqualityComparer<PivotRow>.Instance);
        if (rmData.Count == 0 || segments.Count == 0)
            return corrections;

        foreach (var seg in segments)
        {
            int rmNum = targetRmNum ?? seg.RefRmNum;
            if (rmNum <= 0)
                continue;

            if (targetRmNum.HasValue && targetRmNum.Value < seg.RefRmNum)
                continue;

            var segPivots = new HashSet<int>(seg.Positions.Select(p => p.PivotIndex));
            var valid = rmData
                .Where(r => r.RmNum == rmNum && segPivots.Contains(r.PivotIndex))
                .OrderBy(r => r.PivotIndex)
                .ToList();

            if (valid.Count == 0)
                continue;

            var refRow = valid.FirstOrDefault(r => IsPureRmLabel(r.SolutionLabel, pureRmRx)) ?? valid[0];
            decimal? refVal = null;
            for (int i = 0; i < valid.Count; i++)
            {
                if (valid[i].PivotIndex < refRow.PivotIndex)
                    continue;
                if (valid[i].Values.TryGetValue(element, out var v) && v.HasValue)
                {
                    refVal = v.Value;
                    break;
                }
            }
            if (!refVal.HasValue)
                continue;

            int prevPivot = refRow.PivotIndex;

            for (int i = 0; i < valid.Count; i++)
            {
                var rm = valid[i];
                if (rm.PivotIndex <= prevPivot)
                    continue;

                decimal ratio = 1m;
                if (rm.Values.TryGetValue(element, out var curVal) && curVal.HasValue && curVal.Value != 0m)
                    ratio = refVal.Value / curVal.Value;

                var samples = pivotData
                    .Where(p =>
                        p.PivotIndex >= prevPivot &&
                        p.PivotIndex < rm.PivotIndex &&
                        !IsPureRmLabel(p.SolutionLabel, pureRmRx) &&
                        p.Values.TryGetValue(element, out var ov) && ov.HasValue)
                    .OrderBy(p => p.PivotIndex)
                    .ToList();

                int n = samples.Count;
                for (int j = 0; j < n; j++)
                {
                    var sample = samples[j];
                    var original = sample.Values[element]!.Value;
                    decimal factor = ratio;
                    if (stepwise && n > 1)
                    {
                        var step = (ratio - 1m) / n;
                        factor = 1m + step * (j + 1);
                    }

                    corrections[sample] = new CorrectionResult
                    {
                        OriginalValue = original,
                        CorrectionFactor = factor,
                        CorrectedValue = original * factor
                    };
                }

                prevPivot = rm.PivotIndex;
            }
        }

        return corrections;
    }

    private Dictionary<PivotRow, CorrectionResult> CalculateUniformCorrections(
        List<PivotRow> pivotData,
        List<PivotRow> rmData,
        List<SegmentInfo> segments,
        string element,
        Regex baseRx)
    {
        var corrections = new Dictionary<PivotRow, CorrectionResult>(ReferenceEqualityComparer<PivotRow>.Instance);
        var rmByPivot = rmData.ToDictionary(r => r.PivotIndex);

        foreach (var seg in segments)
        {
            var pos = seg.Positions;

            int startIdx = 0;
            for (int i = 0; i < pos.Count; i++)
            {
                if (pos[i].RmNum == seg.RefRmNum)
                {
                    startIdx = i;
                    break;
                }
            }

            if (startIdx >= pos.Count - 1) continue;

            for (int i = startIdx; i < pos.Count - 1; i++)
            {
                var from = pos[i];
                var to = pos[i + 1];

                if (!rmByPivot.TryGetValue(from.PivotIndex, out var rmFrom) ||
                    !rmByPivot.TryGetValue(to.PivotIndex, out var rmTo))
                    continue;

                var vFrom = rmFrom.Values.GetValueOrDefault(element);
                var vTo = rmTo.Values.GetValueOrDefault(element);

                if (!vFrom.HasValue || !vTo.HasValue || vFrom.Value == 0m)
                    continue;

                var avgStd = (vFrom.Value + vTo.Value) / 2m;
                if (avgStd == 0) continue;

                var correctionFactor = vFrom.Value / avgStd;

                int rangeStart = from.Max;
                int rangeEnd = to.Max;

                var samples = pivotData
                    .Where(p =>
                        p.PivotIndex > rangeStart &&
                        p.PivotIndex < rangeEnd &&
                        !baseRx.IsMatch((p.SolutionLabel ?? string.Empty).Trim()) &&
                        p.Values.TryGetValue(element, out var ov) && ov.HasValue)
                    .OrderBy(p => p.PivotIndex)
                    .ToList();

                foreach (var s in samples)
                {
                    var original = s.Values[element]!.Value;
                    var corrected = original * correctionFactor;

                    corrections[s] = new CorrectionResult
                    {
                        OriginalValue = original,
                        CorrectionFactor = correctionFactor,
                        CorrectedValue = corrected
                    };
                }
            }
        }

        return corrections;
    }

    private Dictionary<PivotRow, CorrectionResult> CalculateStepwiseCorrections(
        List<PivotRow> pivotData,
        List<PivotRow> rmData,
        List<SegmentInfo> segments,
        string element,
        Regex baseRx)
    {
        var corrections = new Dictionary<PivotRow, CorrectionResult>(ReferenceEqualityComparer<PivotRow>.Instance);
        var rmByPivot = rmData.ToDictionary(r => r.PivotIndex);

        foreach (var seg in segments)
        {
            var pos = seg.Positions;

            int startIdx = 0;
            for (int i = 0; i < pos.Count; i++)
            {
                if (pos[i].RmNum == seg.RefRmNum)
                {
                    startIdx = i;
                    break;
                }
            }

            if (startIdx >= pos.Count - 1) continue;

            for (int i = startIdx; i < pos.Count - 1; i++)
            {
                var from = pos[i];
                var to = pos[i + 1];

                if (!rmByPivot.TryGetValue(from.PivotIndex, out var rmFrom) ||
                    !rmByPivot.TryGetValue(to.PivotIndex, out var rmTo))
                    continue;

                var vFrom = rmFrom.Values.GetValueOrDefault(element);
                var vTo = rmTo.Values.GetValueOrDefault(element);

                if (!vFrom.HasValue || !vTo.HasValue || vFrom.Value == 0m)
                    continue;

                decimal startVal = vFrom.Value;
                decimal endVal = vTo.Value;

                int rangeStart = from.Max;
                int rangeEnd = to.Max;

                var samples = pivotData
                    .Where(p =>
                        p.PivotIndex > rangeStart &&
                        p.PivotIndex < rangeEnd &&
                        !baseRx.IsMatch((p.SolutionLabel ?? string.Empty).Trim()) &&
                        p.Values.TryGetValue(element, out var ov) && ov.HasValue)
                    .OrderBy(p => p.PivotIndex)
                    .ToList();

                int n = samples.Count;
                if (n == 0) continue;

                decimal totalSteps = n + 1;

                for (int j = 0; j < n; j++)
                {
                    var s = samples[j];
                    var original = s.Values[element]!.Value;

                    decimal currentStep = j + 1;
                    decimal interpolatedBaseline = startVal + (endVal - startVal) * (currentStep / totalSteps);

                    decimal factor = 1m;
                    if (interpolatedBaseline != 0)
                        factor = startVal / interpolatedBaseline;

                    var corrected = original * factor;

                    corrections[s] = new CorrectionResult
                    {
                        OriginalValue = original,
                        CorrectionFactor = factor,
                        CorrectedValue = corrected
                    };
                }
            }
        }

        return corrections;
    }

    #endregion

    #region DB Apply + Snapshot

    private async Task SaveUndoStateAsync(Guid projectId, string operation)
    {
        var rows = await _db.RawDataRows
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.DataId)
            .Select(r => new SavedRowData
            {
                DataId = r.DataId,
                SampleId = r.SampleId,
                ColumnData = r.ColumnData
            })
            .ToListAsync();

        var json = JsonSerializer.Serialize(rows);

        _db.ProjectStates.Add(new ProjectState
        {
            ProjectId = projectId,
            Data = json,
            Description = $"Undo:{operation}",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }

    private async Task<int> SaveCorrectionsToDatabaseAsync(
        List<RawDataRow> trackedRawRows,
        Dictionary<(string Label, int GroupId, string Element), decimal> corrections,
        List<string> elements)
    {
        if (corrections.Count == 0) return 0;

        var infos = new List<RowInfo>(trackedRawRows.Count);

        foreach (var row in trackedRawRows)
        {
            if (string.IsNullOrWhiteSpace(row.ColumnData))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(row.ColumnData);
                var root = doc.RootElement;

                var type = root.TryGetProperty("Type", out var typeEl) ? typeEl.GetString() : null;

                if (!string.IsNullOrWhiteSpace(type) &&
                    !string.Equals(type, "Samp", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(type, "Sample", StringComparison.OrdinalIgnoreCase))
                    continue;

                var label = root.TryGetProperty("Solution Label", out var labelEl) ? labelEl.GetString() : row.SampleId;
                label ??= row.SampleId;

                var element = root.TryGetProperty("Element", out var el) ? el.GetString() : null;
                if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(element))
                    continue;

                infos.Add(new RowInfo(row, label!, element!, row.DataId));
            }
            catch
            {
                // ignore parse error
            }
        }

        var setSizes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var g in infos.GroupBy(x => x.Label, StringComparer.Ordinal))
        {
            var counts = g.GroupBy(x => x.Element, StringComparer.Ordinal).Select(gg => gg.Count()).ToList();
            if (counts.Count == 0) { setSizes[g.Key] = 1; continue; }

            int gcd = counts[0];
            for (int i = 1; i < counts.Count; i++)
                gcd = Gcd(gcd, counts[i]);

            var total = g.Count();
            setSizes[g.Key] = (gcd > 0 && total % gcd == 0) ? total / gcd : total;
        }

        var labelCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        int updated = 0;

        foreach (var info in infos.OrderBy(x => x.DataId))
        {
            if (!elements.Contains(info.Element, StringComparer.OrdinalIgnoreCase))
                continue;

            if (!labelCounts.TryGetValue(info.Label, out var c))
                c = 0;

            var setSize = setSizes.TryGetValue(info.Label, out var ss) ? ss : 1;
            if (setSize <= 0) setSize = 1;

            var groupId = c / setSize;
            labelCounts[info.Label] = c + 1;

            var key = (info.Label, groupId, info.Element);
            if (!corrections.TryGetValue(key, out var correctedValue))
                continue;

            info.Row.ColumnData = UpdateCorrConJson(info.Row.ColumnData, correctedValue);
            updated++;
        }

        return updated;
    }

    private static string UpdateCorrConJson(string json, decimal correctedValue)
    {
        using var doc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();

        using var ms = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();

            bool wroteCorr = false;
            foreach (var kv in dict)
            {
                if (string.Equals(kv.Key, "Corr Con", StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteNumber("Corr Con", correctedValue);
                    wroteCorr = true;
                }
                else
                {
                    writer.WritePropertyName(kv.Key);
                    kv.Value.WriteTo(writer);
                }
            }

            if (!wroteCorr)
                writer.WriteNumber("Corr Con", correctedValue);

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    #endregion

    #region Helpers

    private static List<string> GetAllElements(List<PivotRow> pivotRows)
        => pivotRows.SelectMany(p => p.Values.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

    private static int Gcd(int a, int b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            int t = b;
            b = a % b;
            a = t;
        }
        return a;
    }

    private static string? GetJsonString(Dictionary<string, JsonElement> data, string key)
    {
        if (!data.TryGetValue(key, out var v))
            return null;

        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind == JsonValueKind.Null) return null;
        return v.ToString();
    }

    private static decimal? GetJsonDecimal(Dictionary<string, JsonElement> data, string key)
    {
        if (!data.TryGetValue(key, out var v))
            return null;

        if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return d;
        }
        return null;
    }

    #endregion

    #region Internal Types

    private sealed record ProcessedData(
        List<RawDataRow> RawRows,
        List<RawDataItem> ParsedRows,
        List<PivotRow> PivotRows);

    private sealed class RawDataItem
    {
        public long DataId { get; set; }
        public string SolutionLabel { get; set; } = "";
        public string Element { get; set; } = "";
        public decimal? CorrCon { get; set; }
        public int OriginalIndex { get; set; }
        public int RowId { get; set; }
        public int GroupId { get; set; }
    }

    private sealed class PivotRow
    {
        public string SolutionLabel { get; set; } = "";
        public int GroupId { get; set; }
        public int OriginalIndex { get; set; }
        public int PivotIndex { get; set; }
        public int RmNum { get; set; }
        public string RmType { get; set; } = "Base";
        public Dictionary<string, decimal?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SegmentInfo
    {
        public int SegmentId { get; set; }
        public int RefRmNum { get; set; }
        public List<PositionInfo> Positions { get; set; } = new();
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public string StartStandard { get; set; } = "";
        public string EndStandard { get; set; } = "";
        public int SampleCount { get; set; }
    }

    private sealed class PositionInfo
    {
        public string SolutionLabel { get; set; } = "";
        public int PivotIndex { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public int RmNum { get; set; }
        public string RmType { get; set; } = "";
    }

    private sealed class CorrectionResult
    {
        public decimal OriginalValue { get; set; }
        public decimal CorrectionFactor { get; set; }
        public decimal CorrectedValue { get; set; }
    }

    private sealed class SavedRowData
    {
        public int DataId { get; set; }
        public string? SampleId { get; set; }
        public string ColumnData { get; set; } = "";
    }

    private sealed class RowInfo
    {
        public RowInfo(RawDataRow row, string label, string element, long dataId)
        {
            Row = row;
            Label = label;
            Element = element;
            DataId = dataId;
        }
        public RawDataRow Row { get; }
        public string Label { get; }
        public string Element { get; }
        public long DataId { get; }
    }

    private sealed class TupleKeyComparer : IEqualityComparer<(string Label, int GroupId, string Element)>
    {
        public static readonly TupleKeyComparer Instance = new();

        public bool Equals((string Label, int GroupId, string Element) x, (string Label, int GroupId, string Element) y)
            => x.GroupId == y.GroupId
               && string.Equals(x.Label, y.Label, StringComparison.Ordinal)
               && string.Equals(x.Element, y.Element, StringComparison.Ordinal);

        public int GetHashCode((string Label, int GroupId, string Element) obj)
            => HashCode.Combine(obj.GroupId,
                StringComparer.Ordinal.GetHashCode(obj.Label ?? ""),
                StringComparer.Ordinal.GetHashCode(obj.Element ?? ""));
    }

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new();
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    #endregion
}
