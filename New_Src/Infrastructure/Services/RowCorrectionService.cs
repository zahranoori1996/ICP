using Application.DTOs;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Wrapper;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Infrastructure.Services;

public class RowCorrectionService : BaseCorrectionService, IRowCorrectionService
{
    public RowCorrectionService(
        IsatisDbContext db,
        IChangeLogService changeLogService,
        ILogger<RowCorrectionService> logger)
        : base(db, changeLogService, logger)
    {
    }

    // ✅ Updated Method based on your request
    // در فایل RowCorrectionService.cs
    // متد FindEmptyRowsAsync را با کد زیر جایگزین کنید

    public async Task<Result<List<EmptyRowDto>>> FindEmptyRowsAsync(FindEmptyRowsRequest request)
    {
        try
        {
            // =================================================================================
            // گام ۱: دریافت داده‌های خام (مشابه pd.read_csv)
            // =================================================================================
            var rawRows = await _db.RawDataRows
                .AsNoTracking()
                .Where(r => r.ProjectId == request.ProjectId)
                .ToListAsync();

            if (!rawRows.Any())
                return Result<List<EmptyRowDto>>.Fail("No data found for this project.");

            // =================================================================================
            // گام ۲: پیش‌پردازش و تخت‌کردن داده‌ها (Flattening)
            // برای شبیه‌سازی رفتار Pandas، به جای Dictionary از لیست تخت استفاده می‌کنیم
            // تا اگر دیتای تکراری بود، از بین نرود.
            // =================================================================================

            var flatData = new List<ParsedDataPoint>();
            var allDetectedElements = new HashSet<string>(StringComparer.Ordinal);

            // اگر کاربر لیست خاصی داد، فیلتر می‌کنیم؛ وگرنه null (یعنی همه عناصر طبق پایتون)
            HashSet<string>? userSelectedElements = null;
            if (request.ElementsToCheck?.Any() == true)
            {
                userSelectedElements = request.ElementsToCheck.ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var row in rawRows)
            {
                try
                {
                    using var doc = JsonDocument.Parse(row.ColumnData);
                    var root = doc.RootElement;

                    // [Python: df[df["Type"] == "Samp"]]
                    if (!root.TryGetProperty("Type", out var typeEl) ||
                        !string.Equals(typeEl.GetString(), "Samp", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // استخراج نام نمونه
                    var sampleId = row.SampleId ?? "Unknown";
                    if (root.TryGetProperty("Solution Label", out var labelEl))
                    {
                        var lbl = labelEl.GetString();
                        if (!string.IsNullOrWhiteSpace(lbl)) sampleId = lbl;
                    }

                    // استخراج نام عنصر
                    if (!root.TryGetProperty("Element", out var elementEl)) continue;
                    var elementName = elementEl.GetString();
                    if (string.IsNullOrWhiteSpace(elementName)) continue;

                    // اعمال فیلتر اختیاری کاربر
                    if (userSelectedElements != null)
                    {
                        var prefix = elementName.Split(' ')[0];
                        if (!userSelectedElements.Contains(prefix)) continue;
                    }

                    // [Python: pd.to_numeric(..., errors='coerce')]
                    // تبدیل مقدار به عدد یا null (NaN)
                    decimal? value = null;
                    if (root.TryGetProperty("Corr Con", out var corrEl))
                    {
                        if (corrEl.ValueKind == JsonValueKind.Number)
                        {
                            value = corrEl.GetDecimal();
                        }
                        else if (corrEl.ValueKind == JsonValueKind.String)
                        {
                            var s = corrEl.GetString();
                            // اگر عدد نبود (مثلاً "<0.01")، مقدار null می‌شود (معادل NaN پایتون)
                            if (decimal.TryParse(s, out var parsed))
                                value = parsed;
                        }
                    }

                    // ذخیره داده (حتی اگر تکراری باشد اضافه می‌شود)
                    flatData.Add(new ParsedDataPoint(sampleId, elementName, value));

                    // [Python: elements = samples["Element"].unique()]
                    // جمع‌آوری تمام عناصر موجود در فایل
                    allDetectedElements.Add(elementName);
                }
                catch (JsonException) { /* Ignore parse errors */ }
            }

            if (!flatData.Any())
                return Result<List<EmptyRowDto>>.Success(new List<EmptyRowDto>());

            // =================================================================================
            // گام ۳: محاسبه میانگین ستون‌ها (Global Mean)
            // [Python: mean_val = vals.mean()]
            // *نکته*: در پایتون dropna() وجود دارد، پس nullها در میانگین نیستند.
            // =================================================================================

            var columnMeans = flatData
                .Where(x => x.Value.HasValue) // dropna
                .GroupBy(x => x.ElementName, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.Average(x => x.Value!.Value),
                    StringComparer.Ordinal
                );

            // محاسبه حد آستانه (Threshold Factor)
            decimal effectivePercent = request.ThresholdPercent > 0 ? request.ThresholdPercent : 70m;
            decimal thresholdFactor = 1m - (effectivePercent / 100m);

            var emptyRows = new List<EmptyRowDto>();

            // =================================================================================
            // گام ۴: گروه‌بندی بر اساس نمونه و بررسی شرط (Main Logic)
            // [Python: for label, g in samples.groupby("Solution Label")]
            // =================================================================================

            var groupedBySample = flatData.GroupBy(x => x.SampleId, StringComparer.Ordinal);

            foreach (var sampleGroup in groupedBySample)
            {
                var sampleId = sampleGroup.Key;

                // برای نمایش در خروجی (DTO) نیاز به دیکشنری داریم (آخرین مقدار را نگه می‌داریم برای نمایش)
                var rowValuesNullable = new Dictionary<string, decimal?>(StringComparer.Ordinal);
                var percentOfAverage = new Dictionary<string, decimal>(StringComparer.Ordinal);

                // ساخت Lookup سریع برای جستجوی مقادیر این نمونه
                // این اجازه می‌دهد اگر یک عنصر ۲ بار تکرار شده، هر ۲ مقدار را داشته باشیم
                var sampleValuesLookup = sampleGroup.ToLookup(x => x.ElementName, x => x.Value, StringComparer.Ordinal);

                int totalElementsChecked = 0;
                int belowThresholdCount = 0;

                // [Python: for el in elements] 
                // حتماً باید روی تمام عناصر کشف شده (allDetectedElements) گردش کنیم
                foreach (var elem in allDetectedElements)
                {
                    // [Python: vals = g[g["Element"] == el]...dropna()]
                    // دریافت تمام مقادیر این عنصر برای این نمونه (حذف nullها)
                    var validValues = sampleValuesLookup[elem]
                        .Where(v => v.HasValue)
                        .Select(v => v!.Value)
                        .ToList();

                    // اگر مقداری وجود نداشت (یا همه NaN بودند)
                    if (!validValues.Any())
                    {
                        // [Python: if vals.empty: continue]
                        // فقط برای نمایش، مقدار null ثبت می‌کنیم
                        rowValuesNullable[elem] = null;
                        continue;
                    }

                    // ثبت مقدار برای نمایش (اولی را می‌گیریم یا null)
                    rowValuesNullable[elem] = validValues.First();

                    // اگر میانگین کل نداشتیم، نمی‌توان مقایسه کرد
                    if (!columnMeans.TryGetValue(elem, out var mean))
                    {
                        percentOfAverage[elem] = 0m;
                        // چون در داده‌ها وجود داشته (validValues پر است)، شمارش می‌شود اما شرط را پاس نمی‌کند
                        totalElementsChecked++;
                        continue;
                    }

                    var threshold = mean * thresholdFactor;

                    // محاسبه درصد برای نمایش
                    var firstVal = validValues.First();
                    percentOfAverage[elem] = mean != 0m ? (firstVal / mean) * 100m : 0m;

                    totalElementsChecked++;

                    // [Python: below.append((sample_vals < threshold).all())]
                    // شرط پایتون: *تمام* مقادیر (تکراری‌ها) باید زیر آستانه باشند.
                    bool allBelow = validValues.All(v => v < threshold);

                    if (allBelow)
                    {
                        belowThresholdCount++;
                    }
                }

                if (totalElementsChecked == 0) continue;

                // محاسبه امتیاز نهایی
                decimal overallScore = ((decimal)belowThresholdCount / totalElementsChecked) * 100m;

                // [Python: if REQUIRE_ALL: if all(below)...]
                bool isEmpty = request.RequireAllElements
                    ? belowThresholdCount == totalElementsChecked
                    : overallScore >= 80m;

                if (isEmpty)
                {
                    // تبدیل میانگین‌ها برای DTO (جایگزینی null با 0 برای نمایش)
                    var meansForDto = columnMeans.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);

                    emptyRows.Add(new EmptyRowDto(
                        sampleId,
                        rowValuesNullable,
                        meansForDto,
                        percentOfAverage,
                        belowThresholdCount,
                        totalElementsChecked,
                        overallScore
                    ));
                }
            }

            _logger.LogInformation("Found {Count} empty rows", emptyRows.Count);
            return Result<List<EmptyRowDto>>.Success(emptyRows.OrderByDescending(x => x.OverallScore).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in FindEmptyRowsAsync");
            return Result<List<EmptyRowDto>>.Fail($"Error: {ex.Message}");
        }
    }

    // کلاس کمکی برای نگهداری داده‌های تخت (خارج از متد تعریف کنید یا داخل کلاس سرویس)
    private record ParsedDataPoint(string SampleId, string ElementName, decimal? Value);

    public async Task<Result<int>> DeleteRowsAsync(DeleteRowsRequest request)
    {
        try
        {
            _logger.LogInformation("Deleting {Count} rows from project {ProjectId}",
                request.SolutionLabels.Count, request.ProjectId);

            await SaveUndoStateAsync(request.ProjectId, $"Delete_{request.SolutionLabels.Count}_rows");

            var rawRows = await _db.RawDataRows
                .Where(r => r.ProjectId == request.ProjectId)
                .ToListAsync();

            var rowsToDelete = new List<RawDataRow>();

            var rmPattern = new Regex(
                @"^(OREAS|SRM|CRM|NIST|BCR|TILL|GBW)[\s\-_]*(\d+|BLANK)?",
                RegexOptions.IgnoreCase);

            foreach (var row in rawRows)
            {
                try
                {
                    var sampleId = row.SampleId ?? "Unknown";

                    if (rmPattern.IsMatch(sampleId))
                    {
                        continue;
                    }

                    if (request.SolutionLabels.Contains(sampleId))
                    {
                        rowsToDelete.Add(row);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse row for deletion check");
                }
            }

            if (rowsToDelete.Any())
            {
                _db.RawDataRows.RemoveRange(rowsToDelete);
                await _db.SaveChangesAsync();

                await _changeLogService.LogChangeAsync(
                    request.ProjectId,
                    "DeleteRows",
                    request.ChangedBy,
                    details: $"Deleted {rowsToDelete.Count} rows: {string.Join(", ", request.SolutionLabels.Take(10))}{(request.SolutionLabels.Count > 10 ? "..." : "")}"
                );
            }

            return Result<int>.Success(rowsToDelete.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete rows for project {ProjectId}", request.ProjectId);
            return Result<int>.Fail($"Failed to delete rows: {ex.Message}");
        }
    }

    public async Task<Result<CorrectionResultDto>> ApplyOptimizationAsync(ApplyOptimizationRequest request)
    {
        try
        {
            var rawRows = await _db.RawDataRows
                .Where(r => r.ProjectId == request.ProjectId)
                .ToListAsync();

            if (!rawRows.Any())
                return Result<CorrectionResultDto>.Fail("No data found for project");

            await SaveUndoStateAsync(request.ProjectId, "OptimizationApply");

            var correctedRows = 0;
            var changeLogEntries = new List<(string? SolutionLabel, string? Element, string? OldValue, string? NewValue)>();

            foreach (var row in rawRows)
            {
                try
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.ColumnData);
                    if (dict == null) continue;

                    var solutionLabel = dict.TryGetValue("Solution Label", out var sl)
                        ? sl.GetString() ?? row.SampleId
                        : row.SampleId;

                    var newDict = new Dictionary<string, object>();
                    var modified = false;

                    foreach (var kvp in dict)
                    {
                        if (request.ElementSettings.TryGetValue(kvp.Key, out var settings) &&
                            kvp.Value.ValueKind == JsonValueKind.Number)
                        {
                            var originalValue = kvp.Value.GetDecimal();
                            var correctedValue = (originalValue - settings.Blank) * settings.Scale;
                            newDict[kvp.Key] = correctedValue;
                            modified = true;

                            changeLogEntries.Add((solutionLabel, kvp.Key, originalValue.ToString(), correctedValue.ToString()));
                        }
                        else
                        {
                            newDict[kvp.Key] = GetJsonValue(kvp.Value);
                        }
                    }

                    if (modified)
                    {
                        row.ColumnData = JsonSerializer.Serialize(newDict);
                        correctedRows++;
                    }
                }
                catch (JsonException)
                {
                    continue;
                }
            }

            var project = await _db.Projects.FindAsync(request.ProjectId);
            if (project != null)
            {
                project.LastModifiedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            if (changeLogEntries.Any())
            {
                var elementSummary = string.Join(", ", request.ElementSettings.Select(e => $"{e.Key}(B={e.Value.Blank:F2},S={e.Value.Scale:F2})"));
                await _changeLogService.LogBatchChangesAsync(
                    request.ProjectId,
                    "BlankScale",
                    changeLogEntries,
                    request.ChangedBy,
                    $"Optimization applied: {elementSummary}"
                );
            }

            _logger.LogInformation("Optimization applied: {CorrectedRows} rows for project {ProjectId}",
                correctedRows, request.ProjectId);

            return Result<CorrectionResultDto>.Success(new CorrectionResultDto(
                rawRows.Count,
                correctedRows,
                new List<CorrectedSampleInfo>()
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply optimization for project {ProjectId}", request.ProjectId);
            return Result<CorrectionResultDto>.Fail($"Failed to apply optimization: {ex.Message}");
        }
    }
}
