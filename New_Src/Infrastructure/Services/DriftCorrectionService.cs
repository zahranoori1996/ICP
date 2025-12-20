using Application.DTOs;
using Application.Services;
using DocumentFormat.OpenXml.Drawing.Charts;
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
/// - RM/CRM detection uses basePattern REGEX directly (no keyword extraction)
/// - RM exclusion in correction uses the same basePattern
/// - Segment boundaries and correction ranges use PivotIndex consistently
/// - Element drift in ANALYZE is computed per-segment refRM (Python-like), fallback to last/first
/// - Apply updates Corr Con per (SolutionLabel, GroupId, Element) in DB + snapshot undo
/// </summary>
public sealed class DriftCorrectionService : IDriftCorrectionService
{
    private readonly IsatisDbContext _db;
    private readonly IChangeLogService _changeLogService;
    private readonly ILogger<DriftCorrectionService> _logger;

    private const string DefaultBasePattern = @"^\s*RM\b";  // default if user doesn't pass basePattern
    private const int MaxCorrectedSamplesInResponse = 1500; // prevent huge response; DB update is still full

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

            var baseRx = BuildRegex(basePattern, DefaultBasePattern);
            var coneRx = BuildRegexOrNull(conePattern);

            var rmData = ExtractRmData(processed.PivotRows, baseRx);
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

            var baseRx = BuildRegex(null, DefaultBasePattern);
            var rmData = ExtractRmData(processed.PivotRows, baseRx);

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

    /// <summary>
    /// Undo last snapshot (LIFO).
    /// </summary>
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

            if (applyToDatabase)
            {
                project = await _db.Projects.FirstOrDefaultAsync(p => p.ProjectId == request.ProjectId);
                if (project == null)
                    return Result<DriftCorrectionResult>.Fail("Project not found");

                await SaveUndoStateAsync(request.ProjectId, $"DriftCorrection({request.Method})");
            }

            var processed = await LoadAndProcessDataAsync(request.ProjectId, tracking: applyToDatabase);
            if (processed.PivotRows.Count == 0)
                return Result<DriftCorrectionResult>.Fail("No data found.");

            var baseRx = BuildRegex(request.BasePattern, DefaultBasePattern);
            var coneRx = BuildRegexOrNull(request.ConePattern);

            // RM rows and segments
            var rmData = ExtractRmData(processed.PivotRows, baseRx);
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

            // corrections maps (for response + DB apply)
            var correctedIntensities = new Dictionary<PivotRow, Dictionary<string, decimal>>(ReferenceEqualityComparer<PivotRow>.Instance);
            var correctionFactors = new Dictionary<PivotRow, Dictionary<string, decimal>>(ReferenceEqualityComparer<PivotRow>.Instance);

            var allCorrectionsForDb = new Dictionary<(string Label, int GroupId, string Element), decimal>(TupleKeyComparer.Instance);

            foreach (var element in elements)
            {
                var corrections = request.Method == DriftMethod.Stepwise
                    ? CalculateStepwiseCorrections(processed.PivotRows, rmData, segments, element, baseRx)
                    : CalculateUniformCorrections(processed.PivotRows, rmData, segments, element, baseRx);

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

                    if (applyToDatabase)
                        allCorrectionsForDb[(sample.SolutionLabel, sample.GroupId, element)] = res.CorrectedValue;
                }
            }

            // Apply to DB
            int savedCount = 0;
            if (applyToDatabase && allCorrectionsForDb.Count > 0)
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

            // Build correctedData response (only samples that actually changed; capped)
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

            var driftSegments = segments.Select(s => new DriftSegment(
                s.SegmentId,
                s.StartIndex,
                s.EndIndex,
                s.StartStandard,
                s.EndStandard,
                s.SampleCount
            )).ToList();

            var result = new DriftCorrectionResult(
                TotalSamples: processed.PivotRows.Count,
                CorrectedSamples: applyToDatabase ? savedCount : correctedSamplesDto.Count,
                SegmentsFound: segments.Count,
                Segments: driftSegments,
                ElementDrifts: elementDrifts,
                CorrectedData: correctedSamplesDto
            );

            return Result<DriftCorrectionResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execute drift failed for {ProjectId}", request.ProjectId);
            return Result<DriftCorrectionResult>.Fail(ex.Message);
        }
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

            if (data == null) continue;

            var type = GetJsonString(data, "Type");
            if (!string.IsNullOrWhiteSpace(type) &&
                !string.Equals(type, "Samp", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(type, "Sample", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var solutionLabel = GetJsonString(data, "Solution Label") ?? row.SampleId ?? $"Row_{row.DataId}";
            var element = GetJsonString(data, "Element");
            var corrCon = GetJsonDecimal(data, "Corr Con");

            if (string.IsNullOrWhiteSpace(element))
                continue;

            parsed.Add(new RawDataItem
            {
                DataId = row.DataId,
                SolutionLabel = solutionLabel,
                Element = element!,
                CorrCon = corrCon,
                OriginalIndex = parsed.Count
            });
        }

        var setSizes = CalculateSetSizes(parsed);

        var labelCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in parsed)
        {
            if (!labelCounts.TryGetValue(item.SolutionLabel, out var c))
                c = 0;

            var setSize = setSizes.TryGetValue(item.SolutionLabel, out var ss) ? ss : 1;
            if (setSize <= 0) setSize = 1;

            item.RowId = c;
            item.GroupId = c / setSize;

            labelCounts[item.SolutionLabel] = c + 1;
        }

        var pivot = CreatePivotTable(parsed);

        return new ProcessedData(rawRows, parsed, pivot);
    }

    private Dictionary<string, int> CalculateSetSizes(List<RawDataItem> rows)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in rows.GroupBy(r => r.SolutionLabel, StringComparer.OrdinalIgnoreCase))
        {
            var counts = group.GroupBy(r => r.Element, StringComparer.OrdinalIgnoreCase)
                              .Select(g => g.Count())
                              .ToList();

            if (counts.Count == 0)
            {
                result[group.Key] = 1;
                continue;
            }

            int gcd = counts[0];
            for (int i = 1; i < counts.Count; i++)
                gcd = Gcd(gcd, counts[i]);

            var total = group.Count();
            result[group.Key] = (gcd > 0 && total % gcd == 0) ? total / gcd : total;
        }

        return result;
    }

    private List<PivotRow> CreatePivotTable(List<RawDataItem> rows)
    {
        var result = new List<PivotRow>();

        foreach (var group in rows.GroupBy(r => (r.SolutionLabel, r.GroupId)))
        {
            var pivot = new PivotRow
            {
                SolutionLabel = group.Key.SolutionLabel,
                GroupId = group.Key.GroupId,
                OriginalIndex = group.Min(r => r.OriginalIndex),
                Values = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase)
            };

            foreach (var r in group.OrderBy(x => x.OriginalIndex))
            {
                if (!pivot.Values.ContainsKey(r.Element))
                    pivot.Values[r.Element] = r.CorrCon;
            }

            result.Add(pivot);
        }

        result = result.OrderBy(p => p.OriginalIndex).ToList();

        for (int i = 0; i < result.Count; i++)
            result[i].PivotIndex = i;

        return result;
    }

    #endregion

    #region RM + Segments

    private static Regex BuildRegex(string? pattern, string fallbackPattern)
    {
        var p = string.IsNullOrWhiteSpace(pattern) ? fallbackPattern : pattern!;
        return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static Regex? BuildRegexOrNull(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        return new Regex(pattern!, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static List<PivotRow> ExtractRmData(List<PivotRow> pivotRows, Regex baseRx)
    {
        var list = pivotRows
            .Where(p => baseRx.IsMatch(p.SolutionLabel))
            .OrderBy(p => p.PivotIndex)
            .ToList();

        foreach (var rm in list)
        {
            var (num, type) = ExtractRmInfo(rm.SolutionLabel);
            rm.RmNum = num;
            rm.RmType = type;
        }

        return list;
    }

    // Python-like parsing:
    // - Type: Cone if contains "cone"; Check if contains "check/chek"; else Base
    // - Number: find digits anywhere after stripping leading RM/CRM token
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

        // strip leading RM/CRM token (if present)
        var cleaned = Regex.Replace(raw, @"^\s*(RM|CRM)\s*[-_ ]*\s*", "", RegexOptions.IgnoreCase).Trim();

        // find first number anywhere
        var mNum = Regex.Match(cleaned, @"(\d+)");
        int num = 0;
        if (mNum.Success)
            int.TryParse(mNum.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out num);

        return (num, type);
    }

    private static List<SegmentInfo> BuildSegments(List<PivotRow> rmData, Regex? coneRx)
    {
        // Segment splits on conePattern OR on RM rows whose ExtractRmInfo says Cone.
        var segments = new List<SegmentInfo>();
        if (rmData.Count == 0) return segments;

        int segmentId = 0;
        int? refRmNum = null;

        int segStartIdx = 0;
        string segStartStd = rmData[0].SolutionLabel;

        for (int i = 0; i < rmData.Count; i++)
        {
            var rm = rmData[i];
            bool isCone = rm.RmType.Equals("Cone", StringComparison.OrdinalIgnoreCase)
                          || (coneRx != null && coneRx.IsMatch(rm.SolutionLabel));

            if (isCone && i != segStartIdx)
            {
                // close previous segment
                var prev = BuildSegment(segmentId, rmData, segStartIdx, i - 1, refRmNum, segStartStd);
                segments.Add(prev);

                // start new
                segmentId++;
                refRmNum = null;
                segStartIdx = i;
                segStartStd = rm.SolutionLabel;
            }

            // choose first base/check as ref within segment
            if (!refRmNum.HasValue && (rm.RmType.Equals("Base", StringComparison.OrdinalIgnoreCase) || rm.RmType.Equals("Check", StringComparison.OrdinalIgnoreCase)))
                refRmNum = rm.RmNum;
        }

        // last segment
        segments.Add(BuildSegment(segmentId, rmData, segStartIdx, rmData.Count - 1, refRmNum, segStartStd));

        return segments;
    }

    private static SegmentInfo BuildSegment(int id, List<PivotRow> rmData, int start, int end, int? refRmNum, string startStd)
    {
        var rms = rmData.GetRange(start, end - start + 1);

        // build positions using PivotIndex for both min/max
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
            RefRmNum = refRmNum ?? positions.FirstOrDefault(p => p.RmType is "Base" or "Check")?.RmNum ?? 0,
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

        // === Python-like behavior ===
        // Drift is calculated ONLY on reference RM (ref_rm_num) of the segment
        // using: last(refRM) / first(refRM)

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

        // === fallback (if no valid segment refRM found) ===
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


    private static ElementDriftInfo? CalculateElementDriftFallback(List<PivotRow> rmData, string element)
    {
        var first = rmData.FirstOrDefault(r => r.Values.TryGetValue(element, out var v) && v.HasValue && v.Value != 0m);
        var last = rmData.LastOrDefault(r => r.Values.TryGetValue(element, out var v) && v.HasValue && v.Value != 0m);

        if (first == null || last == null)
            return null;

        var firstVal = first.Values[element]!.Value;
        var lastVal = last.Values[element]!.Value;

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

    #endregion

    #region Corrections (Math - Fixed)

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

                // NOTE: This is your current "Uniform" implementation.
                // We keep structure unchanged as requested.
                var avgStd = (vFrom.Value + vTo.Value) / 2m;
                if (avgStd == 0) continue;

                var correctionFactor = vFrom.Value / avgStd;

                int rangeStart = from.Max;
                int rangeEnd = to.Max;

                var samples = pivotData
                    .Where(p =>
                        p.PivotIndex > rangeStart &&
                        p.PivotIndex < rangeEnd &&
                        !baseRx.IsMatch(p.SolutionLabel) &&
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

                // NOTE: This is your current Stepwise implementation.
                // We keep structure unchanged as requested.
                decimal startVal = vFrom.Value;
                decimal endVal = vTo.Value;

                int rangeStart = from.Max;
                int rangeEnd = to.Max;

                var samples = pivotData
                    .Where(p =>
                        p.PivotIndex > rangeStart &&
                        p.PivotIndex < rangeEnd &&
                        !baseRx.IsMatch(p.SolutionLabel) &&
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

        var setSizes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in infos.GroupBy(x => x.Label, StringComparer.OrdinalIgnoreCase))
        {
            var counts = g.GroupBy(x => x.Element, StringComparer.OrdinalIgnoreCase).Select(gg => gg.Count()).ToList();
            if (counts.Count == 0) { setSizes[g.Key] = 1; continue; }

            int gcd = counts[0];
            for (int i = 1; i < counts.Count; i++)
                gcd = Gcd(gcd, counts[i]);

            var total = g.Count();
            setSizes[g.Key] = (gcd > 0 && total % gcd == 0) ? total / gcd : total;
        }

        var labelCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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
               && string.Equals(x.Label, y.Label, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Element, y.Element, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Label, int GroupId, string Element) obj)
            => HashCode.Combine(obj.GroupId,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Label ?? ""),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Element ?? ""));
    }

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new();
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    #endregion
}
