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
/// Python formula: corrected = (original - blank) * scale
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

    private const double DefaultCR = 0.7;     // scipy-like default crossover
    private const double Tolerance = 0.01;    // convergence tolerance
    private const int ConvergenceWindow = 10; // generations without improvement

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
            var projectData = await GetProjectRmDataAsync(request.ProjectId);
            if (projectData.Count == 0)
                return Result<BlankScaleOptimizationResult>.Fail("No RM samples found in project");

            // 3. دریافت داده‌های CRM
            var crmData = await GetCrmDataAsync();
            if (crmData.Count == 0)
                return Result<BlankScaleOptimizationResult>.Fail("No CRM data found");

            // 4. تطبیق داده‌های پروژه با CRM
            var matchedData = MatchWithCrm(projectData, crmData);
            if (matchedData.Count == 0)
                return Result<BlankScaleOptimizationResult>.Fail("No matching CRM data found for RM samples");

            // 5. مشخص کردن عناصر مورد نظر
            var elements = (request.Elements != null && request.Elements.Count > 0)
                ? request.Elements
                : GetCommonElements(matchedData);

            // 6. محاسبه آمار اولیه (قبل از بهینه‌سازی)
            var initialStats = CalculateStatistics(matchedData, elements, 0m, 1m, request.MinDiffPercent, request.MaxDiffPercent);

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
                        matchedData, element,
                        request.MinDiffPercent, request.MaxDiffPercent,
                        request.MaxIterations, request.PopulationSize);
                }
                else
                {
                    (optimalBlank, optimalScale, passedAfter) = OptimizeElementImproved(
                        matchedData, element,
                        request.MinDiffPercent, request.MaxDiffPercent,
                        request.MaxIterations, request.PopulationSize);
                    selectedModel = "A";
                }

                // ذخیره وضعیت آماری
                initialStats.ElementStats.TryGetValue(element, out var before);
                var passedBefore = before?.Passed ?? 0;
                var meanBefore = before?.MeanDiff ?? 0m;
                var meanAfter = CalculateMeanDiff(matchedData, element, optimalBlank, optimalScale);

                elementOptimizations[element] = new ElementOptimization(
                    element,
                    optimalBlank,
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
            await SaveUndoStateAsync(request.ProjectId, "Optimization: Auto Blank/Scale (All Elements)");

            // ب) لود کردن ردیف‌های دیتابیس برای اعمال تغییرات (Tracking فعال است)
            var rowsToUpdate = await _db.RawDataRows
                .Where(r => r.ProjectId == request.ProjectId)
                .ToListAsync();

            var projectToUpdate = await _db.Projects
                .FirstOrDefaultAsync(p => p.ProjectId == request.ProjectId);

            int totalDbUpdates = 0;

            // ج) اعمال تغییرات روی JSON هر ردیف بر اساس مقادیر بهینه شده
            foreach (var element in elements)
            {
                if (bestBlanks.TryGetValue(element, out var b) && bestScales.TryGetValue(element, out var s))
                {
                    // این متد محتوای ColumnData را در rowsToUpdate تغییر می‌دهد
                    totalDbUpdates += ApplyBlankScaleToDatabase(rowsToUpdate, element, b, s);
                }
            }

            // د) ثبت زمان تغییرات و ذخیره نهایی در دیتابیس
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
                request.MaxDiffPercent);

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

            var updated = ApplyBlankScaleToDatabase(trackedRows, request.Element, request.Blank, request.Scale);

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
            var projectData = await GetProjectRmDataAsync(request.ProjectId);
            var crmData = await GetCrmDataAsync();
            var matchedData = MatchWithCrm(projectData, crmData);

            if (matchedData.Count == 0)
                return Result<ManualBlankScaleResult>.Fail("No matching CRM data found");

            var elements = new List<string> { request.Element };
            var blanks = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { [request.Element] = request.Blank };
            var scales = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { [request.Element] = request.Scale };

            var beforeStats = CalculateStatistics(matchedData, elements, 0m, 1m, -10m, 10m);
            var afterStats = CalculateStatistics(matchedData, elements, request.Blank, request.Scale, -10m, 10m);

            var optimizedData = BuildOptimizedData(matchedData, elements, blanks, scales, -10m, 10m);

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
            var projectData = await GetProjectRmDataAsync(projectId);
            var crmData = await GetCrmDataAsync();
            var matchedData = MatchWithCrm(projectData, crmData);

            if (matchedData.Count == 0)
                return Result<BlankScaleOptimizationResult>.Fail("No matching CRM data found");

            var elements = GetCommonElements(matchedData);
            var stats = CalculateStatistics(matchedData, elements, 0m, 1m, minDiff, maxDiff);

            var elementOptimizations = elements.ToDictionary(
                e => e,
                e =>
                {
                    stats.ElementStats.TryGetValue(e, out var s);
                    return new ElementOptimization(e, 0m, 1m, s?.Passed ?? 0, s?.Passed ?? 0, s?.MeanDiff ?? 0m, s?.MeanDiff ?? 0m);
                },
                StringComparer.OrdinalIgnoreCase);

            var blanks = elements.ToDictionary(e => e, _ => 0m, StringComparer.OrdinalIgnoreCase);
            var scales = elements.ToDictionary(e => e, _ => 1m, StringComparer.OrdinalIgnoreCase);

            var optimizedData = BuildOptimizedData(matchedData, elements, blanks, scales, minDiff, maxDiff);

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
    /// Apply Python formula: corrected = (original - blank_val + blank_adjust) * scale
    /// blank_val is loaded from Blank rows (Type=Blk) for each element
    /// </summary>
    private int ApplyBlankScaleToDatabase(
        List<Domain.Entities.RawDataRow> trackedRows,
        string targetElement,
        decimal blankAdjust,
        decimal scale,
        Dictionary<string, decimal>? blankValues = null)
    {
        if (trackedRows.Count == 0) return 0;

        // First, find blank_val for this element if not provided
        if (blankValues == null)
        {
            blankValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var elementPattern = new Regex(@"([A-Za-z]+)", RegexOptions.IgnoreCase);

            foreach (var row in trackedRows)
            {
                if (string.IsNullOrWhiteSpace(row.ColumnData)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(row.ColumnData);
                    var root = doc.RootElement;

                    // Check if this is a Blank row
                    if (root.TryGetProperty("Type", out var typeProp))
                    {
                        var typeVal = typeProp.ValueKind == JsonValueKind.String ? typeProp.GetString() : typeProp.ToString();
                        if (string.Equals(typeVal, "Blk", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(typeVal, "Blank", StringComparison.OrdinalIgnoreCase))
                        {
                            if (root.TryGetProperty("Element", out var eProp))
                            {
                                var rawElement = eProp.ValueKind == JsonValueKind.String ? eProp.GetString() : eProp.ToString();
                                var m = elementPattern.Match(rawElement ?? "");
                                if (m.Success)
                                {
                                    var element = m.Groups[1].Value;
                                    if (TryGetDecimal(root, "Soln Conc", out var blankConc))
                                    {
                                        if (!blankValues.ContainsKey(element))
                                            blankValues[element] = blankConc;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        // Get blank_val for target element
        var blankVal = blankValues.TryGetValue(targetElement, out var bv) ? bv : 0m;

        // "Ag 328.068" -> "Ag"
        var elemPattern = new Regex(@"([A-Za-z]+)", RegexOptions.IgnoreCase);

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

                var match = elemPattern.Match(rawElement);
                if (!match.Success)
                    continue;

                var cleanElement = match.Groups[1].Value;
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

    // ---------------------------
    // Data Loading / Matching
    // ---------------------------

    private async Task<List<RmSampleData>> GetProjectRmDataAsync(Guid projectId)
    {
        var rawRows = await _db.RawDataRows.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .ToListAsync();

        // First pass: collect Blank values per element
        var blankValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var elementPattern = new Regex(@"([A-Za-z]+)", RegexOptions.IgnoreCase);

        foreach (var row in rawRows)
        {
            if (string.IsNullOrWhiteSpace(row.ColumnData))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(row.ColumnData);
                var root = doc.RootElement;

                // Check if this is a Blank row (Type = "Blk" or "Blank")
                if (root.TryGetProperty("Type", out var typeProp))
                {
                    var typeVal = typeProp.ValueKind == JsonValueKind.String ? typeProp.GetString() : typeProp.ToString();
                    if (string.Equals(typeVal, "Blk", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(typeVal, "Blank", StringComparison.OrdinalIgnoreCase))
                    {
                        // Get element
                        if (root.TryGetProperty("Element", out var eProp))
                        {
                            var rawElement = eProp.ValueKind == JsonValueKind.String ? eProp.GetString() : eProp.ToString();
                            var m = elementPattern.Match(rawElement ?? "");
                            if (m.Success)
                            {
                                var element = m.Groups[1].Value;

                                // Get Soln Conc as blank value
                                if (TryGetDecimal(root, "Soln Conc", out var blankConc))
                                {
                                    // Keep first blank value per element (or could average them)
                                    if (!blankValues.ContainsKey(element))
                                        blankValues[element] = blankConc;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        _logger.LogDebug("Found {Count} blank values for elements", blankValues.Count);

        // Second pass: collect RM sample data with their blank values
        var result = new List<RmSampleData>();
        var rmPattern = new Regex(@"^(OREAS|SRM|CRM|NIST|BCR|TILL|GBW|RM|PAR)\b", RegexOptions.IgnoreCase);

        foreach (var row in rawRows)
        {
            if (string.IsNullOrWhiteSpace(row.SampleId))
                continue;

            // only RM-like rows
            if (!rmPattern.IsMatch(row.SampleId) &&
                !row.SampleId.Contains("par", StringComparison.OrdinalIgnoreCase) &&
                !row.SampleId.Contains("rm", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.ColumnData))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(row.ColumnData);
                var root = doc.RootElement;

                // element
                string element = "";
                if (root.TryGetProperty("Element", out var eProp))
                {
                    var rawElement = eProp.ValueKind == JsonValueKind.String ? eProp.GetString() : eProp.ToString();
                    var m = elementPattern.Match(rawElement ?? "");
                    if (m.Success)
                        element = m.Groups[1].Value;
                }

                if (string.IsNullOrWhiteSpace(element))
                    continue;

                // concentration
                decimal? conc = null;
                if (TryGetDecimal(root, "Corr Con", out var cc))
                    conc = cc;
                else if (TryGetDecimal(root, "Soln Conc", out var sc))
                    conc = sc;

                if (!conc.HasValue)
                    continue;

                var values = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase)
                {
                    [element] = conc.Value
                };

                // Get blank value for this element
                var blanks = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
                if (blankValues.TryGetValue(element, out var bv))
                    blanks[element] = bv;
                else
                    blanks[element] = 0m; // Default to 0 if no blank found

                result.Add(new RmSampleData(row.SampleId!, values, blanks));
            }
            catch
            {
                // ignore broken json row
            }
        }

        return result;
    }

    /// <summary>
    /// Returns CRM map: CrmId -> (Element -> Value)
    /// Duplicate CrmId rows will be merged safely (prevents "same key added" crash).
    /// </summary>
    private async Task<Dictionary<string, Dictionary<string, decimal>>> GetCrmDataAsync()
    {
        var crmRecords = await _db.CrmData.AsNoTracking().ToListAsync();

        var result = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase);

        foreach (var crm in crmRecords)
        {
            if (string.IsNullOrWhiteSpace(crm.CrmId))
                continue;

            if (string.IsNullOrWhiteSpace(crm.ElementValues))
                continue;

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

            if (!result.TryGetValue(crm.CrmId, out var dict))
            {
                dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                result[crm.CrmId] = dict;
            }

            // merge strategy: keep MAX (safe + stable)
            foreach (var kv in values)
            {
                if (!dict.TryGetValue(kv.Key, out var old))
                    dict[kv.Key] = kv.Value;
                else
                    dict[kv.Key] = Math.Max(old, kv.Value);
            }
        }

        return result;
    }

    private List<MatchedSample> MatchWithCrm(List<RmSampleData> projectData, Dictionary<string, Dictionary<string, decimal>> crmData)
    {
        var result = new List<MatchedSample>();
        if (projectData.Count == 0 || crmData.Count == 0) return result;

        // extracts first number (e.g. "OREAS 100a" -> "100", "CRM 252 y" -> "252")
        var numberPattern = new Regex(@"(\d+)", RegexOptions.IgnoreCase);

        foreach (var sample in projectData)
        {
            var sm = numberPattern.Match(sample.SolutionLabel);
            var sampleNum = sm.Success ? sm.Groups[1].Value : "";
            if (string.IsNullOrEmpty(sampleNum))
                continue;

            string? matchedCrmId = null;

            foreach (var crmId in crmData.Keys)
            {
                var cm = numberPattern.Match(crmId);
                if (!cm.Success) continue;

                if (cm.Groups[1].Value == sampleNum)
                {
                    matchedCrmId = crmId;
                    break;
                }
            }

            if (matchedCrmId == null)
                continue;

            var crmValues = crmData[matchedCrmId]
                .ToDictionary(k => k.Key, v => (decimal?)v.Value, StringComparer.OrdinalIgnoreCase);

            result.Add(new MatchedSample(sample.SolutionLabel, matchedCrmId, sample.Values, crmValues, sample.BlankValues));
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
        decimal maxDiff)
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

                // Python formula: (sample_val - blank_val + blank_adjust) * scale
                var corrected = (sv.Value - blankVal + blankAdjust) * scale;
                var diff = ((corrected - cv.Value) / cv.Value) * 100m;

                diffs.Add(diff);

                if (diff >= minDiff && diff <= maxDiff)
                    passed++;
            }

            var mean = diffs.Count > 0 ? diffs.Average() : 0m;
            stats[element] = new ElementStats(passed, mean);
            total += passed;
        }

        return (total, stats);
    }

    private decimal CalculateMeanDiff(List<MatchedSample> data, string element, decimal blankAdjust, decimal scale)
    {
        var diffs = new List<decimal>();

        foreach (var s in data)
        {
            if (!s.SampleValues.TryGetValue(element, out var sv) || !sv.HasValue) continue;
            if (!s.CrmValues.TryGetValue(element, out var cv) || !cv.HasValue || cv.Value == 0m) continue;

            // Get blank_val for this element
            var blankVal = s.BlankValues.TryGetValue(element, out var bv) && bv.HasValue ? bv.Value : 0m;

            // Python formula: (sample_val - blank_val + blank_adjust) * scale
            var corrected = (sv.Value - blankVal + blankAdjust) * scale;
            var diff = ((corrected - cv.Value) / cv.Value) * 100m;
            diffs.Add(diff);
        }

        return diffs.Count > 0 ? diffs.Average() : 0m;
    }

    private static List<OptimizedSampleDto> BuildOptimizedData(
        List<MatchedSample> data,
        List<string> elements,
        Dictionary<string, decimal> blankAdjusts,
        Dictionary<string, decimal> scales,
        decimal minDiff,
        decimal maxDiff)
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

                // Python formula: (sample_val - blank_val + blank_adjust) * scale
                var optimized = (original - blankVal + blankAdjust) * scale;

                optimizedValues[element] = optimized;

                var db = ((original - cv.Value) / cv.Value) * 100m;
                var da = ((optimized - cv.Value) / cv.Value) * 100m;

                diffBefore[element] = db;
                diffAfter[element] = da;

                passBefore[element] = db >= minDiff && db <= maxDiff;
                passAfter[element] = da >= minDiff && da <= maxDiff;
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
        int populationSize)
    {
        var blankBounds = (-100.0, 100.0);
        var scaleBounds = (0.5, 2.0);

        var population = new List<(double Blank, double Scale)>(populationSize);
        for (int i = 0; i < populationSize; i++)
        {
            population.Add((
                _random.NextDouble() * (blankBounds.Item2 - blankBounds.Item1) + blankBounds.Item1,
                _random.NextDouble() * (scaleBounds.Item2 - scaleBounds.Item1) + scaleBounds.Item1));
        }

        var fitness = population.Select(p => EvaluateFitness(data, element, (decimal)p.Blank, (decimal)p.Scale, minDiff, maxDiff)).ToList();

        double previousBest = fitness.Max();
        int stagnant = 0;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            int bestIdx = fitness.IndexOf(fitness.Max());
            var best = population[bestIdx];

            for (int i = 0; i < populationSize; i++)
            {
                double F = 0.5 + _random.NextDouble() * 0.5; // dithering
                double CR = DefaultCR;

                var candidates = Enumerable.Range(0, populationSize)
                    .Where(j => j != i && j != bestIdx)
                    .OrderBy(_ => _random.Next())
                    .Take(2)
                    .ToList();

                var r1 = population[candidates[0]];
                var r2 = population[candidates[1]];

                var mutantBlank = BounceBack(best.Blank + F * (r1.Blank - r2.Blank), blankBounds.Item1, blankBounds.Item2);
                var mutantScale = BounceBack(best.Scale + F * (r1.Scale - r2.Scale), scaleBounds.Item1, scaleBounds.Item2);

                var current = population[i];
                int jRand = _random.Next(2);

                var trialBlank = (jRand == 0 || _random.NextDouble() < CR) ? mutantBlank : current.Blank;
                var trialScale = (jRand == 1 || _random.NextDouble() < CR) ? mutantScale : current.Scale;

                var trialFitness = EvaluateFitness(data, element, (decimal)trialBlank, (decimal)trialScale, minDiff, maxDiff);
                if (trialFitness > fitness[i])
                {
                    population[i] = (trialBlank, trialScale);
                    fitness[i] = trialFitness;
                }
            }

            var currentBest = fitness.Max();
            if (Math.Abs(currentBest - previousBest) < Tolerance)
                stagnant++;
            else
                stagnant = 0;

            if (stagnant >= ConvergenceWindow)
                break;

            previousBest = currentBest;
        }

        int finalBestIdx = fitness.IndexOf(fitness.Max());
        var finalBest = population[finalBestIdx];

        var passed = CountPassed(data, element, (decimal)finalBest.Blank, (decimal)finalBest.Scale, minDiff, maxDiff);
        return ((decimal)finalBest.Blank, (decimal)finalBest.Scale, passed);
    }

    private (decimal Blank, decimal Scale, int Passed, string SelectedModel) OptimizeElementMultiModel(
        List<MatchedSample> data,
        string element,
        decimal minDiff,
        decimal maxDiff,
        int maxIterations,
        int populationSize)
    {
        // اگر multi-model دقیق‌تری داشتی، همینجا جایگزین کن.
        // فعلاً برای پایداری: Model A
        var (b, s, p) = OptimizeElementImproved(data, element, minDiff, maxDiff, maxIterations, populationSize);
        return (b, s, p, "A");
    }

    private static double EvaluateFitness(List<MatchedSample> data, string element, decimal blankAdjust, decimal scale, decimal minDiff, decimal maxDiff)
        => CountPassed(data, element, blankAdjust, scale, minDiff, maxDiff);

    private static int CountPassed(List<MatchedSample> data, string element, decimal blankAdjust, decimal scale, decimal minDiff, decimal maxDiff)
    {
        int passed = 0;

        foreach (var s in data)
        {
            if (!s.SampleValues.TryGetValue(element, out var sv) || !sv.HasValue) continue;
            if (!s.CrmValues.TryGetValue(element, out var cv) || !cv.HasValue || cv.Value == 0m) continue;

            // Get blank_val for this element
            var blankVal = s.BlankValues.TryGetValue(element, out var bv) && bv.HasValue ? bv.Value : 0m;

            // Python formula: (sample_val - blank_val + blank_adjust) * scale
            var corrected = (sv.Value - blankVal + blankAdjust) * scale;
            var diff = ((corrected - cv.Value) / cv.Value) * 100m;

            if (diff >= minDiff && diff <= maxDiff)
                passed++;
        }

        return passed;
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

    private sealed record RmSampleData(string SolutionLabel, Dictionary<string, decimal?> Values, Dictionary<string, decimal?> BlankValues);

    private sealed record MatchedSample(
        string SolutionLabel,
        string CrmId,
        Dictionary<string, decimal?> SampleValues,
        Dictionary<string, decimal?> CrmValues,
        Dictionary<string, decimal?> BlankValues);

    private sealed record ElementStats(int Passed, decimal MeanDiff);
}