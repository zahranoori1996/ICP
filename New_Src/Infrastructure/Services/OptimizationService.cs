using Application.DTOs;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Wrapper;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Infrastructure.Services;

/// <summary>
/// Blank & Scale optimization service (Python-aligned).
/// Optimization uses blank rows to compute blank adjustment; exposed Blank values are the
/// blank_adjust term used in: corrected = (original - blank_val + blank_adjust) * scale.
///
/// Key fixes:
/// - CRM duplicate-safe (CrmId duplicates won't crash)
/// - MatchWithCrm expects Dictionary CRM map
/// - ApplyManualBlankScaleAsync persists to DB + creates Undo snapshot (Drift-like)
/// </summary>
public class OptimizationService : IOptimizationService
{
    private readonly IsatisDbContext _db;
    private readonly ILogger<OptimizationService> _logger;
    private Random _random;
    private static readonly string[] CrmIds = { "258", "252", "906", "506", "233", "255", "263", "260" };
    private static readonly Regex CrmPattern = new Regex(
        $@"(?i)(?:(?:^|(?<=\s))(?:CRM|OREAS)?\s*({string.Join("|", CrmIds)})(?:[a-zA-Z0-9]{{0,2}})?\b)",
        RegexOptions.Compiled);
    private static readonly Regex BlankPattern = new Regex(
        @"(?i)(?:CRM\s*)?(?:BLANK|BLNK|Blank|blnk|blank)(?:\s+.*)?",
        RegexOptions.Compiled);

    private const double DefaultCR = 0.7;        // scipy default recombination
    private const double DefaultMutationMin = 0.5;
    private const double DefaultMutationMax = 1.0;
    private const int DefaultPopSizeMultiplier = 15;
    private const int DefaultMaxIterations = 1000;
    private const double Tolerance = 0.01;       // scipy default tol

    public OptimizationService(IsatisDbContext db, ILogger<OptimizationService> logger)
    {
        _db = db;
        _logger = logger;
        _random = new Random();
    }

    // ---------------------------
    // Public API
    // ---------------------------

    public async Task<Result<BlankScaleOptimizationResult>> OptimizeBlankScaleAsync(BlankScaleOptimizationRequest request)
    {
        try
        {
            // 1. تنظیم Seed برای تکرارپذیری (در صورت نیاز)
            if (request.Seed.HasValue)
                _random = new Random(request.Seed.Value);

            // 2. دریافت داده‌های پروژه (Read-Only برای محاسبات)
            var crmMaps = await GetCrmDataMapsAsync(request.CrmSelections);
            if (crmMaps.ByKey.Count == 0)
                return Result<BlankScaleOptimizationResult>.Fail("No CRM data found");

            var includedCrmIds = request.IncludedCrmIds != null
                ? new HashSet<string>(request.IncludedCrmIds, StringComparer.OrdinalIgnoreCase)
                : null;
            var excludedLabels = request.ExcludedSolutionLabels != null
                ? new HashSet<string>(request.ExcludedSolutionLabels, StringComparer.OrdinalIgnoreCase)
                : null;

            var rowSelections = await GetCrmSelectionMapAsync(request.ProjectId);
            var projectData = await GetProjectRmDataAsync(
                request.ProjectId,
                crmMaps,
                includedCrmIds,
                rowSelections,
                request.RangeLow,
                request.RangeMid,
                request.RangeHigh1,
                request.RangeHigh2,
                request.RangeHigh3,
                request.RangeHigh4);
            if (projectData.Count == 0)
                return Result<BlankScaleOptimizationResult>.Fail("No RM samples found in project");
            var baseBlanks = GetBaseBlankValues(projectData);

            // 4. تطبیق داده‌های پروژه با CRM
            var matchedData = MatchWithCrm(projectData, crmMaps, rowSelections);
            if (includedCrmIds != null && includedCrmIds.Count > 0)
            {
                matchedData = matchedData
                    .Where(m => includedCrmIds.Contains(ExtractCrmIdNumber(m.CrmId)))
                    .ToList();
            }
            if (matchedData.Count == 0)
                return Result<BlankScaleOptimizationResult>.Fail("No matching CRM data found for RM samples");

            // 5. مشخص کردن عناصر مورد نظر
            var elements = (request.Elements != null && request.Elements.Count > 0)
                ? request.Elements
                : GetCommonElements(matchedData);

            // 6. محاسبه آمار اولیه (قبل از بهینه‌سازی)
            var initialStats = CalculateStatistics(
                matchedData,
                elements,
                0m,
                1m,
                request.MinDiffPercent,
                request.MaxDiffPercent,
                excludedLabels,
                request.ScaleRangeMin,
                request.ScaleRangeMax,
                request.ScaleAbove50Only,
                request.RangeLow,
                request.RangeMid,
                request.RangeHigh1,
                request.RangeHigh2,
                request.RangeHigh3,
                request.RangeHigh4);

            var elementOptimizations = new Dictionary<string, ElementOptimization>(StringComparer.OrdinalIgnoreCase);
            var bestBlanks = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var bestScales = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            // 7. شروع حلقه بهینه‌سازی (محاسبات ریاضی)
            foreach (var element in elements)
            {
                string selectedModel;
                decimal optimalBlank, optimalScale;
                int passedAfter;

                if (request.UseMultiModel)
                {
                    (optimalBlank, optimalScale, passedAfter, selectedModel) = OptimizeElementMultiModel(
                        matchedData,
                        element,
                        request.MinDiffPercent,
                        request.MaxDiffPercent,
                        request.MaxIterations,
                        request.PopulationSize,
                        excludedLabels,
                        request.ScaleRangeMin,
                        request.ScaleRangeMax,
                        request.ScaleAbove50Only,
                        request.RangeLow,
                        request.RangeMid,
                        request.RangeHigh1,
                        request.RangeHigh2,
                        request.RangeHigh3,
                        request.RangeHigh4);
                }
                else
                {
                    (optimalBlank, optimalScale, passedAfter) = OptimizeElementImproved(
                        matchedData,
                        element,
                        request.MinDiffPercent,
                        request.MaxDiffPercent,
                        request.MaxIterations,
                        request.PopulationSize,
                        excludedLabels,
                        request.ScaleRangeMin,
                        request.ScaleRangeMax,
                        request.ScaleAbove50Only,
                        request.RangeLow,
                        request.RangeMid,
                        request.RangeHigh1,
                        request.RangeHigh2,
                        request.RangeHigh3,
                        request.RangeHigh4);
                    selectedModel = "A";
                }

                // ذخیره وضعیت آماری
                initialStats.ElementStats.TryGetValue(element, out var before);
                var passedBefore = before?.Passed ?? 0;
                var meanBefore = before?.MeanDiff ?? 0m;
                var meanAfter = CalculateMeanDiff(
                    matchedData,
                    element,
                    optimalBlank,
                    optimalScale,
                    excludedLabels,
                    request.ScaleRangeMin,
                    request.ScaleRangeMax,
                    request.ScaleAbove50Only);
                var baseBlank = GetBaseBlankValue(matchedData, element);
                var recommendedBlank = baseBlank - optimalBlank;
                elementOptimizations[element] = new ElementOptimization(
                    element,
                    recommendedBlank,
                    optimalScale,
                    passedBefore,
                    passedAfter,
                    meanBefore,
                    meanAfter,
                    selectedModel
                );

                // ذخیره بهترین مقادیر پیدا شده در دیکشنری موقت
                bestBlanks[element] = optimalBlank;
                bestScales[element] = optimalScale;
            }

            // =================================================================================
            // [اصلاح جدید] شروع فاز ذخیره‌سازی در دیتابیس
            // =================================================================================

            // الف) ایجاد اسنپ‌شات برای Undo (فقط یک بار برای کل عملیات)
            if (!request.PreviewOnly)
            {
                await SaveUndoStateAsync(request.ProjectId, "Optimization: Auto Blank/Scale (All Elements)");

                // ?) ??? ???? ???????? ??????? ???? ????? ??????? (Tracking ???? ???)
                var rowsToUpdate = await _db.RawDataRows
                    .Where(r => r.ProjectId == request.ProjectId)
                    .ToListAsync();

                var projectToUpdate = await _db.Projects
                    .FirstOrDefaultAsync(p => p.ProjectId == request.ProjectId);

                int totalDbUpdates = 0;

                // ?) ????? ??????? ??? JSON ?? ???? ?? ???? ?????? ????? ???
                foreach (var element in elements)
                {
                    if (bestBlanks.TryGetValue(element, out var b) && bestScales.TryGetValue(element, out var s))
                    {
                        // ??? ??? ?????? ColumnData ?? ?? rowsToUpdate ????? ??????
                        totalDbUpdates += ApplyBlankScaleToDatabase(rowsToUpdate, element, b, s, baseBlanks);
                    }
                }

                // ?) ??? ???? ??????? ? ????? ????? ?? ???????
                if (projectToUpdate != null)
                {
                    projectToUpdate.LastModifiedAt = DateTime.UtcNow;
                }

                if (totalDbUpdates > 0)
                {
                    await _db.SaveChangesAsync();
                    _logger.LogInformation("Auto-optimization applied to DB. Project: {Id}, Updated Rows: {Count}", request.ProjectId, totalDbUpdates);
                }
                else
                {
                    _logger.LogWarning("Auto-optimization calculated but no rows were updated in DB. Project: {Id}", request.ProjectId);
                }
            }
            else
            {
                _logger.LogInformation("PreviewOnly optimization requested. Skipping DB updates. Project: {Id}", request.ProjectId);
            }

            // =================================================================================
            // پایان فاز ذخیره‌سازی
            // =================================================================================

            // 8. آماده‌سازی داده‌های خروجی برای نمایش به کاربر
            var optimizedData = BuildOptimizedData(
                matchedData,
                elements,
                bestBlanks,
                bestScales,
                request.MinDiffPercent,
                request.MaxDiffPercent,
                excludedLabels,
                request.ScaleRangeMin,
                request.ScaleRangeMax,
                request.ScaleAbove50Only,
                request.RangeLow,
                request.RangeMid,
                request.RangeHigh1,
                request.RangeHigh2,
                request.RangeHigh3,
                request.RangeHigh4);

            var totalPassedBefore = elementOptimizations.Values.Sum(x => x.PassedBefore);
            var totalPassedAfter = elementOptimizations.Values.Sum(x => x.PassedAfter);

            var improvement = totalPassedBefore > 0
                ? ((decimal)(totalPassedAfter - totalPassedBefore) / totalPassedBefore) * 100m
                : 0m;

            MultiModelSummary? modelSummary = null;
            if (request.UseMultiModel)
            {
                var modelACounts = elementOptimizations.Values.Count(e => e.SelectedModel == "A");
                var modelBCounts = elementOptimizations.Values.Count(e => e.SelectedModel == "B");
                var modelCCounts = elementOptimizations.Values.Count(e => e.SelectedModel == "C");

                var mostUsedModel = new[] { ("A", modelACounts), ("B", modelBCounts), ("C", modelCCounts) }
                    .OrderByDescending(x => x.Item2).First().Item1;

                modelSummary = new MultiModelSummary(
                    modelACounts,
                    modelBCounts,
                    modelCCounts,
                    mostUsedModel,
                    $"Model A: {modelACounts} elements, Model B: {modelBCounts} elements, Model C: {modelCCounts} elements");
            }

            return Result<BlankScaleOptimizationResult>.Success(new BlankScaleOptimizationResult(
                matchedData.Count,
                totalPassedBefore,
                totalPassedAfter,
                improvement,
                elementOptimizations,
                optimizedData,
                modelSummary));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OptimizeBlankScale failed for {ProjectId}", request.ProjectId);
            return Result<BlankScaleOptimizationResult>.Fail("Failed to optimize: " + ex.Message);
        }
    }

    /// <summary>
    /// APPLY manual blank/scale (Drift-like):
    /// - Create snapshot for undo
    /// - Update DB ("Corr Con") for the target element
    /// </summary>
    public async Task<Result<ManualBlankScaleResult>> ApplyManualBlankScaleAsync(ManualBlankScaleRequest request)
    {
        try
        {
            var project = await _db.Projects.FirstOrDefaultAsync(p => p.ProjectId == request.ProjectId);
            if (project == null)
                return Result<ManualBlankScaleResult>.Fail("Project not found");

            // Snapshot BEFORE apply
            await SaveUndoStateAsync(request.ProjectId, $"Optimization(ManualBlankScale:{request.Element})");

            // Apply to DB
            var trackedRows = await _db.RawDataRows
                .Where(r => r.ProjectId == request.ProjectId)
                .OrderBy(r => r.DataId)
                .ToListAsync();

            var crmMaps = await GetCrmDataMapsAsync();
            var rowSelections = await GetCrmSelectionMapAsync(request.ProjectId);
            var projectData = await GetProjectRmDataAsync(
                request.ProjectId,
                crmMaps,
                null,
                rowSelections,
                2.0m,
                20.0m,
                10.0m,
                8.0m,
                5.0m,
                3.0m);
            var blankAdjust = GetBaseBlankValue(projectData, request.Element) - request.Blank;
            var baseBlanks = GetBaseBlankValues(projectData);

            var updated = ApplyBlankScaleToDatabase(trackedRows, request.Element, blankAdjust, request.Scale, baseBlanks);

            project.LastModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "ManualBlankScale applied. Project={ProjectId} Element={Element} Blank={Blank} Scale={Scale} UpdatedRows={Updated}",
                request.ProjectId, request.Element, request.Blank, request.Scale, updated);

            // Return preview-style result for UI
            return await PreviewBlankScaleAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplyManualBlankScale failed for {ProjectId}", request.ProjectId);
            return Result<ManualBlankScaleResult>.Fail("Failed to apply: " + ex.Message);
        }
    }

    public async Task<Result<ManualBlankScaleResult>> PreviewBlankScaleAsync(ManualBlankScaleRequest request)
    {
        try
        {
            var crmMaps = await GetCrmDataMapsAsync();
            var rowSelections = await GetCrmSelectionMapAsync(request.ProjectId);
            var projectData = await GetProjectRmDataAsync(
                request.ProjectId,
                crmMaps,
                null,
                rowSelections,
                2.0m,
                20.0m,
                10.0m,
                8.0m,
                5.0m,
                3.0m);
            var matchedData = MatchWithCrm(projectData, crmMaps, rowSelections);

            if (matchedData.Count == 0)
                return Result<ManualBlankScaleResult>.Fail("No matching CRM data found");

            var elements = new List<string> { request.Element };
            var blankAdjust = GetBaseBlankValue(matchedData, request.Element) - request.Blank;
            var blanks = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { [request.Element] = blankAdjust };
            var scales = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { [request.Element] = request.Scale };

            var beforeStats = CalculateStatistics(
                matchedData,
                elements,
                0m,
                1m,
                -10m,
                10m,
                null,
                null,
                null,
                false,
                2.0m,
                20.0m,
                10.0m,
                8.0m,
                5.0m,
                3.0m);
            var afterStats = CalculateStatistics(
                matchedData,
                elements,
                blankAdjust,
                request.Scale,
                -10m,
                10m,
                null,
                null,
                null,
                false,
                2.0m,
                20.0m,
                10.0m,
                8.0m,
                5.0m,
                3.0m);

            var optimizedData = BuildOptimizedData(
                matchedData,
                elements,
                blanks,
                scales,
                -10m,
                10m,
                null,
                null,
                null,
                false,
                2.0m,
                20.0m,
                10.0m,
                8.0m,
                5.0m,
                3.0m);

            var passedBefore = beforeStats.ElementStats.TryGetValue(request.Element, out var bs) ? bs.Passed : 0;
            var passedAfter = afterStats.ElementStats.TryGetValue(request.Element, out var afs) ? afs.Passed : 0;

            return Result<ManualBlankScaleResult>.Success(new ManualBlankScaleResult(
                request.Element, request.Blank, request.Scale, passedBefore, passedAfter, optimizedData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PreviewBlankScale failed");
            return Result<ManualBlankScaleResult>.Fail("Failed to preview: " + ex.Message);
        }
    }

    public async Task<Result<BlankScaleOptimizationResult>> GetCurrentStatisticsAsync(Guid projectId, decimal minDiff = -10m, decimal maxDiff = 10m)
    {
        try
        {
            var crmMaps = await GetCrmDataMapsAsync();
            var rowSelections = await GetCrmSelectionMapAsync(projectId);
            var projectData = await GetProjectRmDataAsync(
                projectId,
                crmMaps,
                null,
                rowSelections,
                2.0m,
                20.0m,
                10.0m,
                8.0m,
                5.0m,
                3.0m);
            var matchedData = MatchWithCrm(projectData, crmMaps, rowSelections);

            if (matchedData.Count == 0)
                return Result<BlankScaleOptimizationResult>.Fail("No matching CRM data found");

            var elements = GetCommonElements(matchedData);
            var stats = CalculateStatistics(
                matchedData,
                elements,
                0m,
                1m,
                minDiff,
                maxDiff,
                null,
                null,
                null,
                false,
                2.0m,
                20.0m,
                10.0m,
                8.0m,
                5.0m,
                3.0m);

            var elementOptimizations = elements.ToDictionary(
                e => e,
                e =>
                {
                    stats.ElementStats.TryGetValue(e, out var s);
                    var baseBlank = GetBaseBlankValue(matchedData, e);
                    return new ElementOptimization(e, baseBlank, 1m, s?.Passed ?? 0, s?.Passed ?? 0, s?.MeanDiff ?? 0m, s?.MeanDiff ?? 0m);
                },
                StringComparer.OrdinalIgnoreCase);

            var blanks = elements.ToDictionary(e => e, _ => 0m, StringComparer.OrdinalIgnoreCase);
            var scales = elements.ToDictionary(e => e, _ => 1m, StringComparer.OrdinalIgnoreCase);

            var optimizedData = BuildOptimizedData(
                matchedData,
                elements,
                blanks,
                scales,
                minDiff,
                maxDiff,
                null,
                null,
                null,
                false,
                2.0m,
                20.0m,
                10.0m,
                8.0m,
                5.0m,
                3.0m);

            return Result<BlankScaleOptimizationResult>.Success(new BlankScaleOptimizationResult(
                matchedData.Count,
                stats.TotalPassed,
                stats.TotalPassed,
                0m,
                elementOptimizations,
                optimizedData,
                null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCurrentStatistics failed for {ProjectId}", projectId);
            return Result<BlankScaleOptimizationResult>.Fail("Failed to get statistics: " + ex.Message);
        }
    }

    public async Task<Result<CrmOptionsResult>> GetCrmOptionsAsync(Guid projectId)
    {
        try
        {
            var rawRows = await _db.RawDataRows.AsNoTracking()
                .Where(r => r.ProjectId == projectId)
                .Select(r => new { r.ColumnData, r.SampleId })
                .ToListAsync();

            var crmNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rawRows)
            {
                if (string.IsNullOrWhiteSpace(row.ColumnData))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(row.ColumnData);
                    var root = doc.RootElement;
                    var label = TryGetString(root, "Solution Label") ?? row.SampleId ?? "";
                    var crmId = ExtractCrmId(label);
                    if (!string.IsNullOrWhiteSpace(crmId))
                        crmNumbers.Add(crmId);
                }
                catch
                {
                    // ignore parse errors
                }
            }

            var crmRecords = await _db.CrmData.AsNoTracking().ToListAsync();
            var preferredMethods = new[] { "4-Acid Digestion", "Aqua Regia Digestion" };
            var numberPattern = new Regex(@"(\d+)", RegexOptions.IgnoreCase);

            var options = new List<CrmMethodOptionDto>();

            foreach (var crmNumber in crmNumbers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var methods = crmRecords
                    .Where(r => r.CrmId != null && numberPattern.Match(r.CrmId).Success && numberPattern.Match(r.CrmId).Groups[1].Value == crmNumber)
                    .Select(r => r.AnalysisMethod)
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Select(m => m!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string? defaultMethod = null;
                foreach (var preferred in preferredMethods)
                {
                    if (methods.Any(m => string.Equals(m, preferred, StringComparison.OrdinalIgnoreCase)))
                    {
                        defaultMethod = preferred;
                        break;
                    }
                }

                defaultMethod ??= methods.FirstOrDefault();

                options.Add(new CrmMethodOptionDto(crmNumber, methods, defaultMethod));
            }

            return Result<CrmOptionsResult>.Success(new CrmOptionsResult(options));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load CRM options for project {ProjectId}", projectId);
            return Result<CrmOptionsResult>.Fail($"Failed to load CRM options: {ex.Message}");
        }
    }

    public async Task<Result<CrmSelectionOptionsResult>> GetCrmSelectionOptionsAsync(Guid projectId)
    {
        try
        {
            var crmMaps = await GetCrmDataMapsAsync();
            if (crmMaps.ByKey.Count == 0)
                return Result<CrmSelectionOptionsResult>.Fail("No CRM data found");

            var selections = await GetCrmSelectionMapAsync(projectId);
            var rows = await BuildCrmSelectionRowsAsync(projectId, crmMaps, selections);
            await SaveDefaultCrmSelectionsAsync(projectId, rows);

            return Result<CrmSelectionOptionsResult>.Success(new CrmSelectionOptionsResult(rows));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load CRM selection options for project {ProjectId}", projectId);
            return Result<CrmSelectionOptionsResult>.Fail($"Failed to load CRM selection options: {ex.Message}");
        }
    }

    public async Task<Result<bool>> SaveCrmSelectionsAsync(CrmSelectionSaveRequest request, string? selectedBy)
    {
        try
        {
            if (request.Selections == null || request.Selections.Count == 0)
                return Result<bool>.Success(true);

            var existing = await _db.CrmSelections
                .Where(s => s.ProjectId == request.ProjectId)
                .ToListAsync();

            foreach (var item in request.Selections)
            {
                if (string.IsNullOrWhiteSpace(item.SolutionLabel) || string.IsNullOrWhiteSpace(item.SelectedCrmKey))
                    continue;

                var match = existing.FirstOrDefault(e =>
                    string.Equals(e.SolutionLabel, item.SolutionLabel, StringComparison.OrdinalIgnoreCase) &&
                    e.RowIndex == item.RowIndex);

                if (match == null)
                {
                    _db.CrmSelections.Add(new CrmSelection
                    {
                        ProjectId = request.ProjectId,
                        SolutionLabel = item.SolutionLabel,
                        RowIndex = item.RowIndex,
                        SelectedCrmKey = item.SelectedCrmKey,
                        SelectedBy = selectedBy,
                        SelectedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    match.SelectedCrmKey = item.SelectedCrmKey;
                    match.SelectedBy = selectedBy;
                    match.SelectedAt = DateTime.UtcNow;
                }
            }

            await _db.SaveChangesAsync();
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save CRM selections for project {ProjectId}", request.ProjectId);
            return Result<bool>.Fail($"Failed to save CRM selections: {ex.Message}");
        }
    }

    private async Task<Dictionary<string, string>> GetCrmSelectionMapAsync(Guid projectId)
    {
        var rows = await _db.CrmSelections.AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .ToListAsync();

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var key = BuildRowKey(row.SolutionLabel, row.RowIndex);
            map[key] = row.SelectedCrmKey;
        }

        return map;
    }

    private async Task<List<CrmSelectionRowDto>> BuildCrmSelectionRowsAsync(
        Guid projectId,
        CrmDataMaps crmMaps,
        Dictionary<string, string> selections)
    {
        var pivotRows = await LoadPivotRowsAsync(projectId);
        var result = new List<CrmSelectionRowDto>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in pivotRows)
        {
            var crmId = ExtractCrmId(row.SolutionLabel);
            if (crmId == null)
                continue;

            if (!crmMaps.AllKeysByNumber.TryGetValue(crmId, out var allKeys) || allKeys.Count == 0)
                continue;

            var preferred = crmMaps.PreferredKeysByNumber.TryGetValue(crmId, out var pref) && pref.Count > 0
                ? pref
                : allKeys;
            var preferredOrdered = preferred.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            var allOrdered = allKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

            var rowKey = BuildRowKey(row.SolutionLabel, row.RowIndex);
            if (added.Contains(rowKey))
                continue;

            selections.TryGetValue(rowKey, out var selectedKey);
            if (preferredOrdered.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(selectedKey) ||
                    !preferredOrdered.Contains(selectedKey, StringComparer.OrdinalIgnoreCase))
                {
                    selectedKey = preferredOrdered[^1];
                }
            }

            result.Add(new CrmSelectionRowDto(
                row.SolutionLabel,
                row.RowIndex,
                crmId,
                preferredOrdered,
                allOrdered,
                selectedKey));

            added.Add(rowKey);
        }

        return result;
    }

    private async Task SaveDefaultCrmSelectionsAsync(Guid projectId, List<CrmSelectionRowDto> rows)
    {
        var defaults = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.SelectedOption))
            .Select(r => new CrmSelectionItemDto(r.SolutionLabel, r.RowIndex, r.SelectedOption!))
            .ToList();

        if (defaults.Count == 0)
            return;

        var request = new CrmSelectionSaveRequest(projectId, defaults);
        var result = await SaveCrmSelectionsAsync(request, null);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Failed to auto-save default CRM selections for project {ProjectId}.", projectId);
        }
    }

    public async Task<object> GetDebugSamplesAsync(Guid projectId)
    {
        var labels = await _db.RawDataRows.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .Select(r => r.SampleId)
            .Where(x => x != null && x != "")
            .Distinct()
            .ToListAsync();

        var crmIds = await _db.CrmData.AsNoTracking()
            .Select(c => c.CrmId)
            .Where(x => x != null && x != "")
            .Distinct()
            .ToListAsync();

        return new
        {
            totalLabels = labels.Count,
            sampleLabels = labels.Take(50).ToList(),
            totalCrm = crmIds.Count,
            sampleCrm = crmIds.Take(50).ToList()
        };
    }

    public async Task<object> GetPivotPreviewAsync(Guid projectId, int take, IEnumerable<string>? elements)
    {
        var pivotRows = await LoadPivotRowsAsync(projectId);
        var safeTake = take <= 0 ? 10 : take;
        var selected = pivotRows.Take(safeTake).ToList();

        var elementList = elements?
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!.Trim())
            .DistinctBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (elementList.Count == 0)
        {
            var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in pivotRows)
            {
                foreach (var key in row.Values.Keys)
                    discovered.Add(key);

                if (discovered.Count >= 8)
                    break;
            }

            elementList = discovered.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var rows = selected
            .Select(r => new
            {
                r.RowIndex,
                r.SolutionLabel,
                Values = elementList.ToDictionary(
                    key => key,
                    key => r.Values.TryGetValue(key, out var v) ? v : null)
            })
            .ToList();

        return new
        {
            elements = elementList,
            rows
        };
    }

    public async Task<object> GetCrmPreviewAsync(string crmId, string? method, IEnumerable<string>? elements)
    {
        if (string.IsNullOrWhiteSpace(crmId))
        {
            return new { found = false, message = "crmId is required." };
        }

        var normalized = crmId.Trim();
        var crmPrefix = $"OREAS {normalized}";
        var query = _db.CrmData.AsNoTracking()
            .Where(c => c.CrmId != null && c.CrmId.StartsWith(crmPrefix));

        if (!string.IsNullOrWhiteSpace(method))
        {
            var trimmed = method.Trim();
            query = query.Where(c => c.AnalysisMethod != null && c.AnalysisMethod == trimmed);
        }

        var crm = await query.OrderBy(c => c.CrmId).FirstOrDefaultAsync();
        if (crm == null)
        {
            return new { found = false, message = "CRM record not found." };
        }

        Dictionary<string, decimal>? values;
        try
        {
            values = JsonSerializer.Deserialize<Dictionary<string, decimal>>(crm.ElementValues);
        }
        catch
        {
            values = null;
        }

        values ??= new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        var elementList = elements?
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!.Trim())
            .DistinctBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (elementList == null || elementList.Count == 0)
        {
            elementList = values.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        }

        var selected = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in elementList)
        {
            var symbol = key.Split(' ')[0];
            selected[key] = values.TryGetValue(symbol, out var v) ? v : null;
        }

        return new
        {
            found = true,
            crmId = crm.CrmId,
            method = crm.AnalysisMethod,
            values = selected
        };
    }

    public async Task<object> GetBlankPreviewAsync(
        Guid projectId,
        IEnumerable<string>? elements,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        var pivotRows = await LoadPivotRowsAsync(projectId);
        if (pivotRows.Count == 0)
        {
            return new { found = false, message = "No pivot rows found." };
        }

        var crmMaps = await GetCrmDataMapsAsync();
        if (crmMaps.ByKey.Count == 0)
        {
            return new { found = false, message = "No CRM data found." };
        }

        var elementList = elements?
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!.Trim())
            .DistinctBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (elementList == null || elementList.Count == 0)
        {
            var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in pivotRows)
            {
                foreach (var key in row.Values.Keys)
                    discovered.Add(key);

                if (discovered.Count >= 8)
                    break;
            }
            elementList = discovered.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var blankCandidates = new Dictionary<string, List<decimal>>(StringComparer.OrdinalIgnoreCase);
        var blankDetails = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
        var crmSamples = new Dictionary<string, List<(decimal SampleValue, decimal CrmValue)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in pivotRows)
        {
            if (string.IsNullOrWhiteSpace(row.SolutionLabel))
                continue;

            if (BlankPattern.IsMatch(row.SolutionLabel))
            {
                foreach (var kvp in row.Values)
                {
                    if (!kvp.Value.HasValue)
                        continue;

                    if (!blankCandidates.TryGetValue(kvp.Key, out var list))
                    {
                        list = new List<decimal>();
                        blankCandidates[kvp.Key] = list;
                    }
                    list.Add(kvp.Value.Value);

                    if (!blankDetails.TryGetValue(kvp.Key, out var detailList))
                    {
                        detailList = new List<object>();
                        blankDetails[kvp.Key] = detailList;
                    }
                    detailList.Add(new { rowIndex = row.RowIndex, label = row.SolutionLabel, value = kvp.Value.Value });
                }
                continue;
            }

            var crmId = ExtractCrmId(row.SolutionLabel);
            if (crmId == null)
                continue;

            crmMaps.DefaultKeyByNumber.TryGetValue(crmId, out var selectedKey);
            if (string.IsNullOrWhiteSpace(selectedKey) || !crmMaps.ByKey.TryGetValue(selectedKey!, out var crmValues))
                continue;

            foreach (var kvp in row.Values)
            {
                if (!kvp.Value.HasValue)
                    continue;

                var symbol = ExtractElementSymbol(kvp.Key);
                if (!crmValues.TryGetValue(symbol, out var crmValue))
                    continue;

                if (!crmSamples.TryGetValue(kvp.Key, out var list))
                {
                    list = new List<(decimal SampleValue, decimal CrmValue)>();
                    crmSamples[kvp.Key] = list;
                }
                list.Add((kvp.Value.Value, crmValue));
            }
        }

        var results = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in elementList)
        {
            if (!blankCandidates.TryGetValue(element, out var candidates))
            {
                results[element] = new { bestBlank = 0m, candidates = Array.Empty<decimal>() };
                continue;
            }

            var sampleList = crmSamples.TryGetValue(element, out var samples) ? samples : new List<(decimal, decimal)>();
            var bestBlank = 0m;
            var minDistance = decimal.MaxValue;
            var inRangeFound = false;

            foreach (var candidate in candidates)
            {
                foreach (var (sampleValue, crmValue) in sampleList)
                {
                    var corrected = sampleValue - candidate;
                    var range = CalculateDynamicRange(
                        crmValue,
                        rangeLow,
                        rangeMid,
                        rangeHigh1,
                        rangeHigh2,
                        rangeHigh3,
                        rangeHigh4);
                    var lower = crmValue - range;
                    var upper = crmValue + range;

                    if (corrected >= lower && corrected <= upper)
                    {
                        bestBlank = candidate;
                        inRangeFound = true;
                        break;
                    }

                    var distance = Math.Abs(corrected - crmValue);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        bestBlank = candidate;
                    }
                }

                if (inRangeFound)
                    break;
            }

            results[element] = new
            {
                bestBlank,
                candidateOrder = candidates.ToList(),
                candidates = candidates.Distinct().OrderBy(x => x).ToList(),
                candidateDetails = blankDetails.TryGetValue(element, out var details) ? details : new List<object>(),
                sampleCount = sampleList.Count
            };
        }

        return new
        {
            found = true,
            elements = results
        };
    }

    // ---------------------------
    // Snapshot + DB Apply (Drift-like)
    // ---------------------------

    private async Task SaveUndoStateAsync(Guid projectId, string operation)
    {
        var rows = await _db.RawDataRows.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.DataId)
            .Select(r => new SavedRowData
            {
                DataId = (int)r.DataId,
                SampleId = r.SampleId,
                ColumnData = r.ColumnData ?? ""
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

    /// <summary>
    /// Apply Python formula: corrected = (original - blank_val + blank_adjust) * scale.
    /// </summary>
    private int ApplyBlankScaleToDatabase(
        List<Domain.Entities.RawDataRow> trackedRows,
        string targetElement,
        decimal blankAdjust,
        decimal scale,
        Dictionary<string, decimal>? blankValues = null)
    {
        if (trackedRows.Count == 0) return 0;

        // Get blank_val for target element
        var blankVal = blankValues != null && blankValues.TryGetValue(targetElement, out var bv) ? bv : 0m;

        int updated = 0;

        foreach (var row in trackedRows)
        {
            if (string.IsNullOrWhiteSpace(row.ColumnData))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(row.ColumnData);
                var root = doc.RootElement;

                // Element
                string? rawElement = null;
                if (root.TryGetProperty("Element", out var elProp))
                    rawElement = elProp.ValueKind == JsonValueKind.String ? elProp.GetString() : elProp.ToString();

                if (string.IsNullOrWhiteSpace(rawElement))
                    continue;

                var cleanElement = ExtractElementKey(rawElement);
                if (string.IsNullOrWhiteSpace(cleanElement))
                    continue;
                if (!string.Equals(cleanElement, targetElement, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Corr Con
                if (!TryGetDecimal(root, "Corr Con", out var corrCon))
                    continue;

                // Python formula: (original - blank_val + blank_adjust) * scale
                var corrected = (corrCon - blankVal + blankAdjust) * scale;

                row.ColumnData = UpdateJsonNumber(row.ColumnData, "Corr Con", corrected);
                updated++;
            }
            catch
            {
                // ignore parse errors
            }
        }

        return updated;
    }

    private static bool TryGetDecimal(JsonElement root, string propName, out decimal value)
    {
        value = 0m;
        if (!root.TryGetProperty(propName, out var p))
            return false;

        if (p.ValueKind == JsonValueKind.Number)
            return p.TryGetDecimal(out value);

        if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), out var d))
        {
            value = d;
            return true;
        }

        return false;
    }

    private static string UpdateJsonNumber(string json, string key, decimal newValue)
    {
        using var doc = JsonDocument.Parse(json);

        var props = new List<(string Name, JsonElement Value)>();
        foreach (var p in doc.RootElement.EnumerateObject())
            props.Add((p.Name, p.Value.Clone()));

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();

            bool wrote = false;
            foreach (var (name, val) in props)
            {
                if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteNumber(key, newValue);
                    wrote = true;
                }
                else
                {
                    writer.WritePropertyName(name);
                    val.WriteTo(writer);
                }
            }

            if (!wrote)
                writer.WriteNumber(key, newValue);

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private sealed class SavedRowData
    {
        public int DataId { get; set; }
        public string? SampleId { get; set; }
        public string ColumnData { get; set; } = "";
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
        public string ColumnKey { get; set; } = "";
    }

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
        public Dictionary<string, decimal?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static string? TryGetString(JsonElement root, string propName)
    {
        if (!root.TryGetProperty(propName, out var p))
            return null;

        if (p.ValueKind == JsonValueKind.String)
            return p.GetString();

        return p.ToString();
    }

    private static string ExtractElementKey(string rawElement)
    {
        if (string.IsNullOrWhiteSpace(rawElement))
            return string.Empty;

        return rawElement.Trim();
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
        var cleaned = ExtractElementKey(rawElement);
        if (string.IsNullOrWhiteSpace(cleaned))
            return string.Empty;

        var parts = cleaned.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0].Trim() : cleaned.Trim();
    }

    private static string ExtractElementSymbol(string elementKey)
    {
        if (string.IsNullOrWhiteSpace(elementKey))
            return string.Empty;

        var noUnderscore = elementKey.Split('_', StringSplitOptions.RemoveEmptyEntries)[0];
        var parts = noUnderscore.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : noUnderscore.Trim();
    }

    private static bool IsSampleType(string type)
    {
        return string.Equals(type, "Samp", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "Sample", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildCrmIdLookup(Dictionary<string, Dictionary<string, decimal>> crmData)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var numberPattern = new Regex(@"(\d+)", RegexOptions.IgnoreCase);

        foreach (var key in crmData.Keys)
        {
            var m = numberPattern.Match(key);
            if (!m.Success)
                continue;

            var num = m.Groups[1].Value;
            if (!CrmIds.Contains(num))
                continue;

            if (!map.ContainsKey(num))
                map[num] = key;
        }

        return map;
    }

    private static string? ExtractCrmId(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var match = CrmPattern.Match(label);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string ExtractCrmIdNumber(string crmId)
    {
        if (string.IsNullOrWhiteSpace(crmId))
            return string.Empty;

        var match = Regex.Match(crmId, @"(\d+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : crmId;
    }

    private static string BuildCrmSelectionKey(CrmData crm)
    {
        var method = string.IsNullOrWhiteSpace(crm.AnalysisMethod) ? "Unknown" : crm.AnalysisMethod.Trim();
        return $"{crm.CrmId} ({method})";
    }

    private static string BuildRowKey(string solutionLabel, int rowIndex)
        => $"{solutionLabel}::{rowIndex}";

    private Dictionary<string, decimal> ComputeBestBlankValues(
        List<PivotRow> rows,
        CrmDataMaps crmMaps,
        HashSet<string>? includedCrmIds,
        Dictionary<string, string>? rowSelections,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        var blankCandidates = new Dictionary<string, List<decimal>>(StringComparer.OrdinalIgnoreCase);
        var crmSamples = new Dictionary<string, List<(decimal SampleValue, decimal CrmValue)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.SolutionLabel))
                continue;

            if (BlankPattern.IsMatch(row.SolutionLabel))
            {
                foreach (var kvp in row.Values)
                {
                    if (!kvp.Value.HasValue)
                        continue;

                    if (!blankCandidates.TryGetValue(kvp.Key, out var blankList))
                    {
                        blankList = new List<decimal>();
                        blankCandidates[kvp.Key] = blankList;
                    }

                    blankList.Add(kvp.Value.Value);
                }
                continue;
            }

            var crmId = ExtractCrmId(row.SolutionLabel);
            if (crmId == null)
                continue;
            if (includedCrmIds != null && includedCrmIds.Count > 0 && !includedCrmIds.Contains(crmId))
                continue;

            var rowKey = BuildRowKey(row.SolutionLabel, row.RowIndex);
            string? selectedKey = null;
            if (rowSelections != null && rowSelections.TryGetValue(rowKey, out var selectionValue))
                selectedKey = selectionValue;
            if (string.IsNullOrWhiteSpace(selectedKey))
                crmMaps.DefaultKeyByNumber.TryGetValue(crmId, out selectedKey);

            if (string.IsNullOrWhiteSpace(selectedKey) || !crmMaps.ByKey.TryGetValue(selectedKey!, out var crmValues))
                continue;

            foreach (var kvp in row.Values)
            {
                if (!kvp.Value.HasValue)
                    continue;

                var crmSymbol = ExtractElementSymbol(kvp.Key);
                if (!crmValues.TryGetValue(crmSymbol, out var crmValue))
                    continue;

                if (!crmSamples.TryGetValue(kvp.Key, out var crmList))
                {
                    crmList = new List<(decimal SampleValue, decimal CrmValue)>();
                    crmSamples[kvp.Key] = crmList;
                }

                crmList.Add((kvp.Value.Value, crmValue));
            }
        }

        var best = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in blankCandidates)
        {
            var element = kvp.Key;
            var candidates = kvp.Value;
            if (candidates.Count == 0)
                continue;

            if (!crmSamples.TryGetValue(element, out var samples) || samples.Count == 0)
            {
                best[element] = 0m;
                continue;
            }

            var bestBlank = 0m;
            var minDistance = decimal.MaxValue;
            var inRangeFound = false;

            foreach (var candidate in candidates)
            {
                foreach (var (sampleValue, crmValue) in samples)
                {
                    var corrected = sampleValue - candidate;
                    var range = CalculateDynamicRange(
                        crmValue,
                        rangeLow,
                        rangeMid,
                        rangeHigh1,
                        rangeHigh2,
                        rangeHigh3,
                        rangeHigh4);
                    var lower = crmValue - range;
                    var upper = crmValue + range;

                    if (corrected >= lower && corrected <= upper)
                    {
                        bestBlank = candidate;
                        inRangeFound = true;
                        break;
                    }

                    var distance = Math.Abs(corrected - crmValue);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        bestBlank = candidate;
                    }
                }

                if (inRangeFound)
                    break;
            }

            best[element] = bestBlank;
        }

        return best;
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
        {
            result = result == 0 ? v : GCD(result, v);
        }
        return result == 0 ? 1 : result;
    }

    private async Task<List<PivotRow>> LoadPivotRowsAsync(Guid projectId)
    {
        var rawRows = await _db.RawDataRows.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.DataId)
            .ToListAsync();

        var samples = new List<SampleRecord>();
        var solutionGroups = new Dictionary<string, List<SampleRecord>>(StringComparer.OrdinalIgnoreCase);
        int sampleIndex = 0;

        foreach (var row in rawRows)
        {
            if (string.IsNullOrWhiteSpace(row.ColumnData))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(row.ColumnData);
                var root = doc.RootElement;

                var solutionLabel = TryGetString(root, "Solution Label") ?? row.SampleId ?? "";
                var type = TryGetString(root, "Type") ?? "";
                if (!IsSampleType(type))
                    continue;

                var elementRaw = TryGetString(root, "Element") ?? "";
                var element = NormalizeElementForPivot(elementRaw);

                decimal? corrCon = TryGetDecimal(root, "Corr Con", out var cc) ? cc : null;
                var value = corrCon;

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
            return new List<PivotRow>();

        var mostCommonSizes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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

        var repeatCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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

        var rowBuckets = new Dictionary<string, PivotRowBucket>(StringComparer.OrdinalIgnoreCase);

        if (hasRepeats)
        {
            var occCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in samples)
            {
                var key = $"{rec.SolutionLabel}::{rec.GroupId}::{rec.Element}";
                occCounts[key] = occCounts.GetValueOrDefault(key) + 1;
            }

            var occCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
                bucket.Values[rec.ColumnKey] = rec.Value;
            }
        }
        else
        {
            var uidMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
                bucket.Values[rec.Element] = rec.Value;
            }
        }

        var ordered = rowBuckets.Values
            .OrderBy(r => r.FirstIndex)
            .ToList();

        var pivotRows = new List<PivotRow>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            var bucket = ordered[i];
            pivotRows.Add(new PivotRow(bucket.SolutionLabel, i, bucket.Values));
        }

        return pivotRows;
    }

    // ---------------------------
    // Data Loading / Matching
    // ---------------------------

    private async Task<List<RmSampleData>> GetProjectRmDataAsync(
        Guid projectId,
        CrmDataMaps crmMaps,
        HashSet<string>? includedCrmIds,
        Dictionary<string, string>? rowSelections,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        var pivotRows = await LoadPivotRowsAsync(projectId);

        var blankValues = ComputeBestBlankValues(
            pivotRows,
            crmMaps,
            includedCrmIds,
            rowSelections,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);
        var result = new List<RmSampleData>();

        foreach (var row in pivotRows)
        {
            if (string.IsNullOrWhiteSpace(row.SolutionLabel))
                continue;

            var crmId = ExtractCrmId(row.SolutionLabel);
            if (crmId == null)
                continue;
            if (includedCrmIds != null && includedCrmIds.Count > 0 && !includedCrmIds.Contains(crmId))
                continue;

            if (row.Values.Count == 0)
                continue;

            var values = new Dictionary<string, decimal?>(row.Values, StringComparer.OrdinalIgnoreCase);
            var blanks = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in row.Values.Keys)
            {
                blanks[key] = blankValues.TryGetValue(key, out var bv) ? bv : 0m;
            }

            result.Add(new RmSampleData(row.SolutionLabel, row.RowIndex, values, blanks));
        }

        return result;
    }

    /// <summary>
    /// Builds CRM maps keyed by "CRM ID (Method)" plus default key per CRM number.
    /// </summary>
    private async Task<CrmDataMaps> GetCrmDataMapsAsync(Dictionary<string, string>? crmSelections = null)
    {
        var crmRecords = await _db.CrmData.AsNoTracking()
            .OrderBy(c => c.Id)
            .ToListAsync();
        var preferredMethods = new[] { "4-Acid Digestion", "Aqua Regia Digestion" };

        var byKey = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase);
        var allKeysByNumber = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var preferredKeysByNumber = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var methodOrderByNumber = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var keyToMethod = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var crm in crmRecords)
        {
            if (string.IsNullOrWhiteSpace(crm.CrmId))
                continue;

            var number = ExtractCrmIdNumber(crm.CrmId);
            if (string.IsNullOrWhiteSpace(number))
                continue;

            var key = BuildCrmSelectionKey(crm);
            keyToMethod[key] = crm.AnalysisMethod;

            Dictionary<string, decimal>? values;
            try
            {
                values = JsonSerializer.Deserialize<Dictionary<string, decimal>>(crm.ElementValues);
            }
            catch
            {
                continue;
            }

            if (values == null || values.Count == 0)
                continue;

            byKey[key] = new Dictionary<string, decimal>(values, StringComparer.OrdinalIgnoreCase);

            if (!allKeysByNumber.TryGetValue(number, out var allList))
            {
                allList = new List<string>();
                allKeysByNumber[number] = allList;
            }
            allList.Add(key);

            if (!methodOrderByNumber.TryGetValue(number, out var methodList))
            {
                methodList = new List<string>();
                methodOrderByNumber[number] = methodList;
            }
            if (!string.IsNullOrWhiteSpace(crm.AnalysisMethod) &&
                !methodList.Contains(crm.AnalysisMethod, StringComparer.OrdinalIgnoreCase))
            {
                methodList.Add(crm.AnalysisMethod);
            }

            if (!string.IsNullOrWhiteSpace(crm.AnalysisMethod) &&
                preferredMethods.Any(m => string.Equals(m, crm.AnalysisMethod, StringComparison.OrdinalIgnoreCase)))
            {
                if (!preferredKeysByNumber.TryGetValue(number, out var prefList))
                {
                    prefList = new List<string>();
                    preferredKeysByNumber[number] = prefList;
                }
                prefList.Add(key);
            }
        }

        var defaultKeyByNumber = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in allKeysByNumber)
        {
            var number = kvp.Key;
            var allKeys = kvp.Value.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            var prefKeys = preferredKeysByNumber.TryGetValue(number, out var pref)
                ? pref.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();
            var methodOrder = methodOrderByNumber.TryGetValue(number, out var methods)
                ? methods
                : new List<string>();

            string? selected = null;
            if (crmSelections != null && crmSelections.TryGetValue(number, out var selectedMethod))
            {
                selected = allKeys.FirstOrDefault(k =>
                    string.Equals(keyToMethod.GetValueOrDefault(k), selectedMethod, StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrWhiteSpace(selected))
            {
                string? chosenMethod = null;
                if (methodOrder.Count > 0)
                {
                    chosenMethod = methodOrder.FirstOrDefault(m =>
                        preferredMethods.Any(p => string.Equals(p, m, StringComparison.OrdinalIgnoreCase)))
                        ?? methodOrder[0];
                }

                if (!string.IsNullOrWhiteSpace(chosenMethod))
                {
                    var matches = allKeys
                        .Where(k => string.Equals(keyToMethod.GetValueOrDefault(k), chosenMethod, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    selected = matches.LastOrDefault();
                }
            }

            selected ??= allKeys.LastOrDefault();

            if (!string.IsNullOrWhiteSpace(selected))
                defaultKeyByNumber[number] = selected!;
        }

        return new CrmDataMaps(byKey, defaultKeyByNumber, allKeysByNumber, preferredKeysByNumber, keyToMethod);
    }

    private List<MatchedSample> MatchWithCrm(
        List<RmSampleData> projectData,
        CrmDataMaps crmMaps,
        Dictionary<string, string>? rowSelections)
    {
        var result = new List<MatchedSample>();
        if (projectData.Count == 0 || crmMaps.ByKey.Count == 0) return result;

        foreach (var sample in projectData)
        {
            var sampleCrmId = ExtractCrmId(sample.SolutionLabel);
            if (string.IsNullOrEmpty(sampleCrmId))
                continue;

            var rowKey = BuildRowKey(sample.SolutionLabel, sample.RowIndex);
            var selectedKey = rowSelections != null && rowSelections.TryGetValue(rowKey, out var sk) ? sk : null;
            if (string.IsNullOrWhiteSpace(selectedKey))
                crmMaps.DefaultKeyByNumber.TryGetValue(sampleCrmId, out selectedKey);
            if (string.IsNullOrWhiteSpace(selectedKey) || !crmMaps.ByKey.TryGetValue(selectedKey!, out var crmValuesBySymbol))
                continue;

            var crmValues = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);

            foreach (var sampleKey in sample.Values.Keys)
            {
                var symbol = ExtractElementSymbol(sampleKey);
                if (crmValuesBySymbol.TryGetValue(symbol, out var value))
                {
                    crmValues[sampleKey] = value;
                }
            }

            result.Add(new MatchedSample(sample.SolutionLabel, sample.RowIndex, selectedKey!, sample.Values, crmValues, sample.BlankValues));
        }

        return result;
    }

    private static List<string> GetCommonElements(List<MatchedSample> data)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in data)
        {
            foreach (var k in s.SampleValues.Keys)
            {
                if (s.CrmValues.ContainsKey(k))
                    set.Add(k);
            }
        }

        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------------------------
    // Statistics + Build result data
    // ---------------------------

    /// <summary>
    /// Python formula: adjusted = (sample_val - blank_val + blank_adjust) * scale
    /// Here: blank = blank_adjust (optimization parameter)
    ///       blank_val = from sample's BlankValues dictionary
    /// </summary>
    private (int TotalPassed, Dictionary<string, ElementStats> ElementStats) CalculateStatistics(
        List<MatchedSample> data,
        List<string> elements,
        decimal blankAdjust,
        decimal scale,
        decimal minDiff,
        decimal maxDiff,
        HashSet<string>? excludedLabels,
        decimal? scaleRangeMin,
        decimal? scaleRangeMax,
        bool scaleAbove50Only,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        int total = 0;
        var stats = new Dictionary<string, ElementStats>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in elements)
        {
            int passed = 0;
            var diffs = new List<decimal>();

            foreach (var s in data)
            {
                if (!s.SampleValues.TryGetValue(element, out var sv) || !sv.HasValue) continue;
                if (!s.CrmValues.TryGetValue(element, out var cv) || !cv.HasValue || cv.Value == 0m) continue;

                // Get blank_val for this element (default to 0 if not found)
                var blankVal = s.BlankValues.TryGetValue(element, out var bv) && bv.HasValue ? bv.Value : 0m;

                var applyCorrection = ShouldApplyCorrection(
                    s.SolutionLabel,
                    sv.Value,
                    excludedLabels,
                    scaleRangeMin,
                    scaleRangeMax,
                    scaleAbove50Only);

                // Python formula: (sample_val - blank_val + blank_adjust) * scale
                var corrected = applyCorrection
                    ? (sv.Value - blankVal + blankAdjust) * scale
                    : sv.Value;
                var diff = ((corrected - cv.Value) / cv.Value) * 100m;

                diffs.Add(diff);

                if (IsInDynamicRange(
                    corrected,
                    cv.Value,
                    rangeLow,
                    rangeMid,
                    rangeHigh1,
                    rangeHigh2,
                    rangeHigh3,
                    rangeHigh4))
                    passed++;
            }

            var mean = diffs.Count > 0 ? diffs.Average() : 0m;
            stats[element] = new ElementStats(passed, mean);
            total += passed;
        }

        return (total, stats);
    }

    private decimal CalculateMeanDiff(
        List<MatchedSample> data,
        string element,
        decimal blankAdjust,
        decimal scale,
        HashSet<string>? excludedLabels,
        decimal? scaleRangeMin,
        decimal? scaleRangeMax,
        bool scaleAbove50Only)
    {
        var diffs = new List<decimal>();

        foreach (var s in data)
        {
            if (!s.SampleValues.TryGetValue(element, out var sv) || !sv.HasValue) continue;
            if (!s.CrmValues.TryGetValue(element, out var cv) || !cv.HasValue || cv.Value == 0m) continue;

            // Get blank_val for this element
            var blankVal = s.BlankValues.TryGetValue(element, out var bv) && bv.HasValue ? bv.Value : 0m;

            var applyCorrection = ShouldApplyCorrection(
                s.SolutionLabel,
                sv.Value,
                excludedLabels,
                scaleRangeMin,
                scaleRangeMax,
                scaleAbove50Only);

            // Python formula: (sample_val - blank_val + blank_adjust) * scale
            var corrected = applyCorrection
                ? (sv.Value - blankVal + blankAdjust) * scale
                : sv.Value;
            var diff = ((corrected - cv.Value) / cv.Value) * 100m;
            diffs.Add(diff);
        }

        return diffs.Count > 0 ? diffs.Average() : 0m;
    }

    private static bool IsInDynamicRange(
        decimal corrected,
        decimal crmValue,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        var range = CalculateDynamicRange(
            crmValue,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);
        return corrected >= crmValue - range && corrected <= crmValue + range;
    }

    private static bool ShouldApplyCorrection(
        string solutionLabel,
        decimal value,
        HashSet<string>? excludedLabels,
        decimal? scaleRangeMin,
        decimal? scaleRangeMax,
        bool scaleAbove50Only)
    {
        if (excludedLabels != null && excludedLabels.Contains(solutionLabel))
            return false;

        if (scaleAbove50Only && value <= 50m)
            return false;

        if (scaleRangeMin.HasValue && scaleRangeMax.HasValue)
        {
            if (value < scaleRangeMin.Value || value > scaleRangeMax.Value)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Calculate dynamic acceptable range for a given value, matching Python's magnitude-based logic.
    /// Uses configurable default percentages for different value ranges:
    /// - |x| < 10: ±2.0 (absolute)
    /// - 10 ≤ |x| < 100: ±20%
    /// - 100 ≤ |x| < 1000: ±10%
    /// - 1000 ≤ |x| < 10000: ±8%
    /// - 10000 ≤ |x| < 100000: ±5%
    /// - |x| ≥ 100000: ±3%
    /// </summary>
    private static decimal CalculateDynamicRange(
        decimal value,
        decimal rangeLow = 2.0m,
        decimal rangeMid = 20.0m,
        decimal rangeHigh1 = 10.0m,
        decimal rangeHigh2 = 8.0m,
        decimal rangeHigh3 = 5.0m,
        decimal rangeHigh4 = 3.0m)
    {
        var absValue = Math.Abs(value);

        if (absValue < 10m)
            return rangeLow;
        else if (absValue < 100m)
            return absValue * (rangeMid / 100m);
        else if (absValue < 1000m)
            return absValue * (rangeHigh1 / 100m);
        else if (absValue < 10000m)
            return absValue * (rangeHigh2 / 100m);
        else if (absValue < 100000m)
            return absValue * (rangeHigh3 / 100m);
        else
            return absValue * (rangeHigh4 / 100m);
    }

    private static List<OptimizedSampleDto> BuildOptimizedData(
        List<MatchedSample> data,
        List<string> elements,
        Dictionary<string, decimal> blankAdjusts,
        Dictionary<string, decimal> scales,
        decimal minDiff,
        decimal maxDiff,
        HashSet<string>? excludedLabels,
        decimal? scaleRangeMin,
        decimal? scaleRangeMax,
        bool scaleAbove50Only,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        var result = new List<OptimizedSampleDto>();

        foreach (var sample in data)
        {
            var optimizedValues = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
            var diffBefore = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var diffAfter = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var passBefore = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var passAfter = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in elements)
            {
                if (!sample.SampleValues.TryGetValue(element, out var sv) || !sv.HasValue) continue;
                if (!sample.CrmValues.TryGetValue(element, out var cv) || !cv.HasValue || cv.Value == 0m) continue;

                var blankAdjust = blankAdjusts.TryGetValue(element, out var ba) ? ba : 0m;
                var scale = scales.TryGetValue(element, out var ss) ? ss : 1m;

                // Get blank_val for this element
                var blankVal = sample.BlankValues.TryGetValue(element, out var bv) && bv.HasValue ? bv.Value : 0m;

                var original = sv.Value;
                var applyCorrection = ShouldApplyCorrection(
                    sample.SolutionLabel,
                    original,
                    excludedLabels,
                    scaleRangeMin,
                    scaleRangeMax,
                    scaleAbove50Only);

                // Python formula: (sample_val - blank_val + blank_adjust) * scale
                var optimized = applyCorrection
                    ? (original - blankVal + blankAdjust) * scale
                    : original;

                optimizedValues[element] = optimized;

                var correctedBefore = applyCorrection ? (original - blankVal) : original;
                var db = ((correctedBefore - cv.Value) / cv.Value) * 100m;
                var da = ((optimized - cv.Value) / cv.Value) * 100m;

                diffBefore[element] = db;
                diffAfter[element] = da;

                passBefore[element] = IsInDynamicRange(
                    correctedBefore,
                    cv.Value,
                    rangeLow,
                    rangeMid,
                    rangeHigh1,
                    rangeHigh2,
                    rangeHigh3,
                    rangeHigh4);
                passAfter[element] = IsInDynamicRange(
                    optimized,
                    cv.Value,
                    rangeLow,
                    rangeMid,
                    rangeHigh1,
                    rangeHigh2,
                    rangeHigh3,
                    rangeHigh4);
            }

            result.Add(new OptimizedSampleDto(
                sample.SolutionLabel,
                sample.CrmId,
                sample.SampleValues,
                sample.CrmValues,
                optimizedValues,
                diffBefore,
                diffAfter,
                passBefore,
                passAfter
            ));
        }

        return result;
    }

    // ---------------------------
    // Optimization algorithms
    // ---------------------------

    private (decimal Blank, decimal Scale, int Passed) OptimizeElementImproved(
        List<MatchedSample> data,
        string element,
        decimal minDiff,
        decimal maxDiff,
        int maxIterations,
        int populationSize,
        HashSet<string>? excludedLabels,
        decimal? scaleRangeMin,
        decimal? scaleRangeMax,
        bool scaleAbove50Only,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        var samples = BuildElementSamples(
            data,
            element,
            excludedLabels,
            scaleRangeMin,
            scaleRangeMax,
            scaleAbove50Only);
        if (samples.Count == 0)
            return (0m, 1m, 0);

        var outcome = OptimizeModelA(
            samples,
            maxIterations,
            populationSize,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);
        return (outcome.Blank, outcome.Scale, outcome.Passed);
    }

    private (decimal Blank, decimal Scale, int Passed, string SelectedModel) OptimizeElementMultiModel(
        List<MatchedSample> data,
        string element,
        decimal minDiff,
        decimal maxDiff,
        int maxIterations,
        int populationSize,
        HashSet<string>? excludedLabels,
        decimal? scaleRangeMin,
        decimal? scaleRangeMax,
        bool scaleAbove50Only,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        var samples = BuildElementSamples(
            data,
            element,
            excludedLabels,
            scaleRangeMin,
            scaleRangeMax,
            scaleAbove50Only);
        if (samples.Count == 0)
            return (0m, 1m, 0, "A");

        var modelA = OptimizeModelA(
            samples,
            maxIterations,
            populationSize,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);
        var modelB = OptimizeModelB(
            samples,
            maxIterations,
            populationSize,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);
        var modelC = OptimizeModelC(
            samples,
            maxIterations,
            populationSize,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);

        var candidates = new List<ModelCandidate>
        {
            new("A", modelA.Blank, modelA.Scale, modelA.Passed, double.PositiveInfinity),
            new("B", modelB.Blank, modelB.Scale, modelB.Passed, modelB.AvgDistance),
            new("C", modelC.Blank, modelC.Scale, modelC.Passed, modelC.AvgDistance)
        };

        var best = candidates
            .OrderByDescending(c => c.Passed)
            .ThenBy(c => c.Distance ?? double.PositiveInfinity)
            .First();

        return (best.Blank, best.Scale, best.Passed, best.Model);
    }

    private static List<ElementSample> BuildElementSamples(
        List<MatchedSample> data,
        string element,
        HashSet<string>? excludedLabels,
        decimal? scaleRangeMin,
        decimal? scaleRangeMax,
        bool scaleAbove50Only)
    {
        var samples = new List<ElementSample>();

        foreach (var s in data)
        {
            if (!s.SampleValues.TryGetValue(element, out var sv) || !sv.HasValue) continue;
            if (!s.CrmValues.TryGetValue(element, out var cv) || !cv.HasValue || cv.Value == 0m) continue;

            var blankVal = s.BlankValues.TryGetValue(element, out var bv) && bv.HasValue ? bv.Value : 0m;
            var applyCorrection = ShouldApplyCorrection(
                s.SolutionLabel,
                sv.Value,
                excludedLabels,
                scaleRangeMin,
                scaleRangeMax,
                scaleAbove50Only);
            samples.Add(new ElementSample((double)sv.Value, (double)cv.Value, (double)blankVal, applyCorrection));
        }

        return samples;
    }

    private static decimal GetBaseBlankValue(List<MatchedSample> data, string element)
    {
        foreach (var sample in data)
        {
            if (sample.BlankValues.TryGetValue(element, out var bv) && bv.HasValue)
                return bv.Value;
        }

        return 0m;
    }

    private static Dictionary<string, decimal> GetBaseBlankValues(List<RmSampleData> data)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var sample in data)
        {
            foreach (var kvp in sample.BlankValues)
            {
                if (kvp.Value.HasValue && !result.ContainsKey(kvp.Key))
                {
                    result[kvp.Key] = kvp.Value.Value;
                }
            }
        }

        return result;
    }

    private static decimal GetBaseBlankValue(List<RmSampleData> data, string element)
    {
        foreach (var sample in data)
        {
            if (sample.BlankValues.TryGetValue(element, out var bv) && bv.HasValue)
                return bv.Value;
        }

        return 0m;
    }

    private ModelOutcome OptimizeModelA(
        List<ElementSample> samples,
        int maxIterations,
        int populationSize,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        var total = samples.Count;
        var (blankMin, blankMax) = GetBlankBounds(samples);
        var (scaleMin, scaleMax) = GetScaleBounds();

        double Objective(double blankAdjust, double scale)
        {
            var inRange = CountInRange(
                samples,
                blankAdjust,
                scale,
                rangeLow,
                rangeMid,
                rangeHigh1,
                rangeHigh2,
                rangeHigh3,
                rangeHigh4);
            var reg = 10.0 * Math.Abs(scale - 1.0);
            return -inRange + (reg / total);
        }

        var result = RunDifferentialEvolution(Objective, (blankMin, blankMax), (scaleMin, scaleMax), maxIterations, populationSize);
        var blank = result.Blank;
        var scale = result.Scale;
        var inRangeAfter = CountInRange(
            samples,
            blank,
            scale,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);

        var blankOnly = RunDifferentialEvolution((b, _) => Objective(b, 1.0), (blankMin, blankMax), (1.0, 1.0), maxIterations, populationSize);
        var inRangeBlank = CountInRange(
            samples,
            blankOnly.Blank,
            1.0,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);

        if (inRangeBlank > inRangeAfter || (inRangeBlank == inRangeAfter && (double)inRangeBlank / total >= 0.75))
        {
            blank = blankOnly.Blank;
            scale = 1.0;
            inRangeAfter = inRangeBlank;
        }

        var avgDistance = AverageDistance(
            samples,
            blank,
            scale,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);
        return new ModelOutcome("A", (decimal)blank, (decimal)scale, inRangeAfter, avgDistance);
    }

    private ModelOutcome OptimizeModelB(
        List<ElementSample> samples,
        int maxIterations,
        int populationSize,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        var total = samples.Count;
        var meanAbsSample = AverageAbsSample(samples);
        var (blankMin, blankMax) = GetBlankBounds(samples);
        var (scaleMin, scaleMax) = GetScaleBounds();

        double Objective(double blankAdjust, double scale)
        {
            var totalDistance = TotalHuberDistance(
                samples,
                blankAdjust,
                scale,
                rangeLow,
                rangeMid,
                rangeHigh1,
                rangeHigh2,
                rangeHigh3,
                rangeHigh4);
            var reg = 0.1 * (Math.Abs(scale - 1.0) * meanAbsSample);
            return (totalDistance / total) + reg;
        }

        var result = RunDifferentialEvolution(Objective, (blankMin, blankMax), (scaleMin, scaleMax), maxIterations, populationSize);
        var blank = result.Blank;
        var scale = result.Scale;
        var inRangeAfter = CountInRange(
            samples,
            blank,
            scale,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);
        var avgDistance = AverageDistance(
            samples,
            blank,
            scale,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);

        var blankOnly = RunDifferentialEvolution((b, _) => Objective(b, 1.0), (blankMin, blankMax), (1.0, 1.0), maxIterations, populationSize);
        var inRangeBlank = CountInRange(
            samples,
            blankOnly.Blank,
            1.0,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);
        var avgDistanceBlank = AverageDistance(
            samples,
            blankOnly.Blank,
            1.0,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);

        if (inRangeBlank > inRangeAfter || (inRangeBlank == inRangeAfter && (double)inRangeBlank / total >= 0.75))
        {
            blank = blankOnly.Blank;
            scale = 1.0;
            inRangeAfter = inRangeBlank;
            avgDistance = avgDistanceBlank;
        }

        return new ModelOutcome("B", (decimal)blank, (decimal)scale, inRangeAfter, avgDistance);
    }

    private ModelOutcome OptimizeModelC(
        List<ElementSample> samples,
        int maxIterations,
        int populationSize,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        var total = samples.Count;
        var meanAbsSample = AverageAbsSample(samples);
        var (blankMin, blankMax) = GetBlankBounds(samples);
        var (scaleMin, scaleMax) = GetScaleBounds();

        double Objective(double blankAdjust, double scale)
        {
            var totalSse = TotalSse(
                samples,
                blankAdjust,
                scale,
                rangeLow,
                rangeMid,
                rangeHigh1,
                rangeHigh2,
                rangeHigh3,
                rangeHigh4);
            var reg = 0.1 * (Math.Abs(scale - 1.0) * meanAbsSample);
            return (totalSse / total) + reg;
        }

        var result = RunDifferentialEvolution(Objective, (blankMin, blankMax), (scaleMin, scaleMax), maxIterations, populationSize);
        var blank = result.Blank;
        var scale = result.Scale;
        var inRangeAfter = CountInRange(
            samples,
            blank,
            scale,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);
        var avgDistance = AverageDistance(
            samples,
            blank,
            scale,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);

        var blankOnly = RunDifferentialEvolution((b, _) => Objective(b, 1.0), (blankMin, blankMax), (1.0, 1.0), maxIterations, populationSize);
        var inRangeBlank = CountInRange(
            samples,
            blankOnly.Blank,
            1.0,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);
        var avgDistanceBlank = AverageDistance(
            samples,
            blankOnly.Blank,
            1.0,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);

        if (inRangeBlank > inRangeAfter || (inRangeBlank == inRangeAfter && (double)inRangeBlank / total >= 0.75))
        {
            blank = blankOnly.Blank;
            scale = 1.0;
            inRangeAfter = inRangeBlank;
            avgDistance = avgDistanceBlank;
        }

        return new ModelOutcome("C", (decimal)blank, (decimal)scale, inRangeAfter, avgDistance);
    }

    private static (double Min, double Max) GetBlankBounds(List<ElementSample> samples)
    {
        var avgCert = samples.Average(s => s.CrmValue);
        var maxVal = samples
            .Select(s => Math.Abs(s.SampleValue))
            .Concat(samples.Select(s => Math.Abs(s.BlankValue)))
            .Append(Math.Abs(avgCert))
            .Append(1.0)
            .Max();

        var bound = maxVal * 10.0;
        return (-bound, bound);
    }

    private static (double Min, double Max) GetScaleBounds()
        => (1.0 - 0.3, 1.0 + 0.3);

    private static double ComputeCorrectedValue(ElementSample sample, double blankAdjust, double scale)
    {
        if (!sample.ApplyCorrection)
            return sample.SampleValue;

        return (sample.SampleValue - sample.BlankValue + blankAdjust) * scale;
    }

    private static int CountInRange(
        List<ElementSample> samples,
        double blankAdjust,
        double scale,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        int passed = 0;

        foreach (var sample in samples)
        {
            var corrected = ComputeCorrectedValue(sample, blankAdjust, scale);
            var range = CalculateDynamicRange(
                sample.CrmValue,
                rangeLow,
                rangeMid,
                rangeHigh1,
                rangeHigh2,
                rangeHigh3,
                rangeHigh4);
            var lower = sample.CrmValue - range;
            var upper = sample.CrmValue + range;

            if (corrected >= lower && corrected <= upper)
                passed++;
        }

        return passed;
    }

    private static double AverageAbsSample(List<ElementSample> samples)
    {
        if (samples.Count == 0)
            return 0.0;

        return samples.Average(s => Math.Abs(s.SampleValue));
    }

    private static double AverageDistance(
        List<ElementSample> samples,
        double blankAdjust,
        double scale,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        if (samples.Count == 0)
            return 0.0;

        var distances = samples.Select(s => DistanceToRange(
            ComputeCorrectedValue(s, blankAdjust, scale),
            s.CrmValue,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4));
        return distances.Average();
    }

    private static double TotalHuberDistance(
        List<ElementSample> samples,
        double blankAdjust,
        double scale,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        double total = 0.0;

        foreach (var sample in samples)
        {
            var corrected = ComputeCorrectedValue(sample, blankAdjust, scale);
            var dist = DistanceToRange(
                corrected,
                sample.CrmValue,
                rangeLow,
                rangeMid,
                rangeHigh1,
                rangeHigh2,
                rangeHigh3,
                rangeHigh4);
            total += Huber(1.0, dist);
        }

        return total;
    }

    private static double TotalSse(
        List<ElementSample> samples,
        double blankAdjust,
        double scale,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        double total = 0.0;

        foreach (var sample in samples)
        {
            var corrected = ComputeCorrectedValue(sample, blankAdjust, scale);
            var diff = corrected - sample.CrmValue;
            total += diff * diff;
        }

        return total;
    }

    private (double Blank, double Scale, double Objective) RunDifferentialEvolution(
        Func<double, double, double> objective,
        (double Min, double Max) blankBounds,
        (double Min, double Max) scaleBounds,
        int maxIterations,
        int populationSize)
    {
        var dimensions = 2;
        var popMultiplier = populationSize > 0 ? Math.Max(populationSize, DefaultPopSizeMultiplier) : DefaultPopSizeMultiplier;
        var size = Math.Max(popMultiplier * dimensions, 6);
        var iterations = maxIterations > 0 ? Math.Max(maxIterations, DefaultMaxIterations) : DefaultMaxIterations;
        var population = new List<(double Blank, double Scale)>(size);

        for (int i = 0; i < size; i++)
        {
            population.Add((
                _random.NextDouble() * (blankBounds.Max - blankBounds.Min) + blankBounds.Min,
                _random.NextDouble() * (scaleBounds.Max - scaleBounds.Min) + scaleBounds.Min));
        }

        var fitness = population
            .Select(p => -objective(p.Blank, p.Scale))
            .ToList();

        for (int iter = 0; iter < iterations; iter++)
        {
            int bestIdx = fitness.IndexOf(fitness.Max());
            var best = population[bestIdx];

            for (int i = 0; i < size; i++)
            {
                double F = DefaultMutationMin + _random.NextDouble() * (DefaultMutationMax - DefaultMutationMin);
                double CR = DefaultCR;

                var candidates = Enumerable.Range(0, size)
                    .Where(j => j != i && j != bestIdx)
                    .OrderBy(_ => _random.Next())
                    .Take(2)
                    .ToList();

                var r1 = population[candidates[0]];
                var r2 = population[candidates[1]];

                var mutantBlank = BounceBack(best.Blank + F * (r1.Blank - r2.Blank), blankBounds.Min, blankBounds.Max);
                var mutantScale = BounceBack(best.Scale + F * (r1.Scale - r2.Scale), scaleBounds.Min, scaleBounds.Max);

                var current = population[i];
                int jRand = _random.Next(2);

                var trialBlank = (jRand == 0 || _random.NextDouble() < CR) ? mutantBlank : current.Blank;
                var trialScale = (jRand == 1 || _random.NextDouble() < CR) ? mutantScale : current.Scale;

                var trialFitness = -objective(trialBlank, trialScale);
                if (trialFitness > fitness[i])
                {
                    population[i] = (trialBlank, trialScale);
                    fitness[i] = trialFitness;
                }
            }
        }

        int finalBestIdx = fitness.IndexOf(fitness.Max());
        var finalBest = population[finalBestIdx];
        var finalObjective = -fitness[finalBestIdx];

        return (finalBest.Blank, finalBest.Scale, finalObjective);
    }

    private static double CalculateDynamicRange(
        double value,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        var absValue = Math.Abs(value);
        if (absValue < 10)
            return (double)rangeLow;
        if (absValue < 100)
            return absValue * ((double)rangeMid / 100.0);
        if (absValue < 1000)
            return absValue * ((double)rangeHigh1 / 100.0);
        if (absValue < 10000)
            return absValue * ((double)rangeHigh2 / 100.0);
        if (absValue < 100000)
            return absValue * ((double)rangeHigh3 / 100.0);
        return absValue * ((double)rangeHigh4 / 100.0);
    }

    private static double DistanceToRange(
        double corrected,
        double crmValue,
        decimal rangeLow,
        decimal rangeMid,
        decimal rangeHigh1,
        decimal rangeHigh2,
        decimal rangeHigh3,
        decimal rangeHigh4)
    {
        var range = CalculateDynamicRange(
            crmValue,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);
        var lower = crmValue - range;
        var upper = crmValue + range;

        if (corrected < lower)
            return lower - corrected;
        if (corrected > upper)
            return corrected - upper;

        return 0.0;
    }

    private static double Huber(double delta, double x)
    {
        var abs = Math.Abs(x);
        if (abs <= delta)
            return 0.5 * abs * abs;

        return delta * (abs - 0.5 * delta);
    }

    private static double BounceBack(double value, double min, double max)
    {
        if (value < min) return min + (min - value);
        if (value > max) return max - (value - max);
        return value;
    }
    // ---------------------------
    // Internal types
    // ---------------------------

    private sealed record CrmDataMaps(
        Dictionary<string, Dictionary<string, decimal>> ByKey,
        Dictionary<string, string> DefaultKeyByNumber,
        Dictionary<string, List<string>> AllKeysByNumber,
        Dictionary<string, List<string>> PreferredKeysByNumber,
        Dictionary<string, string?> KeyToMethod);

    private sealed record RmSampleData(
        string SolutionLabel,
        int RowIndex,
        Dictionary<string, decimal?> Values,
        Dictionary<string, decimal?> BlankValues);

    private sealed record MatchedSample(
        string SolutionLabel,
        int RowIndex,
        string CrmId,
        Dictionary<string, decimal?> SampleValues,
        Dictionary<string, decimal?> CrmValues,
        Dictionary<string, decimal?> BlankValues);

    private sealed record ElementSample(double SampleValue, double CrmValue, double BlankValue, bool ApplyCorrection);

    private sealed record ModelOutcome(string Model, decimal Blank, decimal Scale, int Passed, double AvgDistance);

    private sealed record ModelCandidate(string Model, decimal Blank, decimal Scale, int Passed, double? Distance);

    private sealed record ElementStats(int Passed, decimal MeanDiff);
}
