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
/// Implementation of IRmCheckService. 
/// Handles RM checking and weight/volume validation. 
/// Equivalent to rm_check.py in Python code.
/// </summary>
public class RmCheckService : IRmCheckService
{
    private readonly IsatisDbContext _db;
    private readonly ILogger<RmCheckService> _logger;

    // Default RM patterns (from Python code)
    private static readonly string[] DefaultRmPatterns =
    {
        "258", "252", "906", "506", "233", "255", "263", "260",
        "OREAS", "CRM", "STD", "BLK", "BLANK"
    };

    public RmCheckService(IsatisDbContext db, ILogger<RmCheckService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<RmCheckSummaryDto>> CheckRmAsync(RmCheckRequest request)
    {
        try
        {
            // 1. Load project with raw data
            var project = await _db.Projects
                .AsNoTracking()
                .Include(p => p.RawDataRows)
                .FirstOrDefaultAsync(p => p.ProjectId == request.ProjectId);

            if (project == null)
                return Result<RmCheckSummaryDto>.Fail("Project not found");

            var patterns = request.RmPatterns ?? DefaultRmPatterns.ToList();
            var results = new List<RmCheckResultDto>();
            var elementSummary = new Dictionary<string, List<(decimal? diff, bool passed)>>();

            // 2. Process each row
            foreach (var rawRow in project.RawDataRows)
            {
                var rowData = ParseRowData(rawRow.ColumnData);
                if (rowData == null) continue;

                var solutionLabel = GetSolutionLabel(rawRow.SampleId, rowData);
                if (string.IsNullOrWhiteSpace(solutionLabel)) continue;

                // Check if this is an RM sample
                var matchedRm = FindRmMatch(solutionLabel, patterns);
                if (matchedRm == null) continue;

                // 3. Find matching CRM in database
                var crmData = await _db.CrmData
                    .AsNoTracking()
                    .Where(c => c.CrmId.Contains(matchedRm))
                    .ToListAsync();

                if (!crmData.Any())
                {
                    results.Add(new RmCheckResultDto(
                        solutionLabel,
                        matchedRm,
                        "Unknown",
                        RmCheckStatus.NoReference,
                        new List<RmElementCheckDto>(),
                        0, 0, 0
                    ));
                    continue;
                }

                // Select preferred CRM (by analysis method if specified)
                CrmData selectedCrm;
                if (!string.IsNullOrWhiteSpace(request.AnalysisMethod))
                {
                    selectedCrm = crmData.FirstOrDefault(c => c.AnalysisMethod == request.AnalysisMethod)
                                  ?? crmData.First();
                }
                else
                {
                    var preferredMethods = new[] { "4-Acid Digestion", "Aqua Regia Digestion" };
                    selectedCrm = crmData.FirstOrDefault(c => preferredMethods.Contains(c.AnalysisMethod))
                                  ?? crmData.First();
                }

                // 4. Parse CRM element values
                var crmElements = ParseElementValues(selectedCrm.ElementValues);

                // 5.  Check each element
                var elementChecks = new List<RmElementCheckDto>();
                int passCount = 0, failCount = 0;

                foreach (var kvp in rowData)
                {
                    if (IsMetadataColumn(kvp.Key)) continue;

                    var elementSymbol = ExtractElementSymbol(kvp.Key);
                    if (string.IsNullOrEmpty(elementSymbol)) continue;

                    var sampleValue = ParseDecimalValue(kvp.Value);
                    decimal? refValue = null;

                    // Try to find reference value
                    if (crmElements.TryGetValue(elementSymbol, out var rv))
                        refValue = rv;
                    else if (crmElements.TryGetValue(kvp.Key, out var rv2))
                        refValue = rv2;

                    // Calculate diff
                    decimal? diffPercent = null;
                    var status = RmCheckStatus.Skipped;
                    string? message = null;

                    if (sampleValue.HasValue && refValue.HasValue && refValue.Value != 0)
                    {
                        diffPercent = ((sampleValue.Value - refValue.Value) / refValue.Value) * 100;
                        diffPercent = Math.Round(diffPercent.Value, 2);

                        if (diffPercent >= request.MinDiffPercent && diffPercent <= request.MaxDiffPercent)
                        {
                            status = RmCheckStatus.Pass;
                            passCount++;
                        }
                        else
                        {
                            status = RmCheckStatus.Fail;
                            failCount++;
                            message = $"Diff {diffPercent:F2}% out of range [{request.MinDiffPercent}, {request.MaxDiffPercent}]";
                        }

                        // Track for summary
                        if (!elementSummary.ContainsKey(elementSymbol))
                            elementSummary[elementSymbol] = new List<(decimal?, bool)>();
                        elementSummary[elementSymbol].Add((diffPercent, status == RmCheckStatus.Pass));
                    }
                    else if (!refValue.HasValue)
                    {
                        status = RmCheckStatus.NoReference;
                    }

                    elementChecks.Add(new RmElementCheckDto(
                        kvp.Key,
                        sampleValue,
                        refValue,
                        diffPercent,
                        status,
                        message
                    ));
                }

                // Determine overall status
                var overallStatus = failCount > 0 ? RmCheckStatus.Fail :
                                   passCount > 0 ? RmCheckStatus.Pass :
                                   RmCheckStatus.NoReference;

                results.Add(new RmCheckResultDto(
                    solutionLabel,
                    selectedCrm.CrmId,
                    selectedCrm.AnalysisMethod ?? "Unknown",
                    overallStatus,
                    elementChecks,
                    passCount,
                    failCount,
                    elementChecks.Count
                ));
            }

            // 6. Build element summary
            var elementSummaryDto = elementSummary.ToDictionary(
                kvp => kvp.Key,
                kvp => new RmElementSummaryDto(
                    kvp.Key,
                    kvp.Value.Count,
                    kvp.Value.Count(x => x.passed),
                    kvp.Value.Count(x => !x.passed),
                    kvp.Value.Where(x => x.diff.HasValue).Average(x => x.diff!.Value),
                    kvp.Value.Where(x => x.diff.HasValue).Max(x => x.diff!.Value),
                    kvp.Value.Where(x => x.diff.HasValue).Min(x => x.diff!.Value)
                )
            );

            // 7. Build summary
            var summary = new RmCheckSummaryDto(
                request.ProjectId,
                results.Count,
                results.Count(r => r.Status == RmCheckStatus.Pass),
                results.Count(r => r.Status == RmCheckStatus.Fail),
                results.Count(r => r.Status == RmCheckStatus.Warning),
                results,
                elementSummaryDto
            );

            return Result<RmCheckSummaryDto>.Success(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check RM for project {ProjectId}", request.ProjectId);
            return Result<RmCheckSummaryDto>.Fail($"Failed to check RM: {ex.Message}");
        }
    }

    public async Task<Result<List<string>>> GetRmSamplesAsync(Guid projectId, List<string>? patterns = null)
    {
        try
        {
            var project = await _db.Projects
                .AsNoTracking()
                .Include(p => p.RawDataRows)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);

            if (project == null)
                return Result<List<string>>.Fail("Project not found");

            patterns ??= DefaultRmPatterns.ToList();
            var rmSamples = new List<string>();

            foreach (var rawRow in project.RawDataRows)
            {
                var rowData = ParseRowData(rawRow.ColumnData);
                if (rowData == null) continue;

                var solutionLabel = GetSolutionLabel(rawRow.SampleId, rowData);
                if (string.IsNullOrWhiteSpace(solutionLabel)) continue;

                var matchedRm = FindRmMatch(solutionLabel, patterns);
                if (matchedRm != null)
                {
                    rmSamples.Add(solutionLabel);
                }
            }

            return Result<List<string>>.Success(rmSamples.Distinct().OrderBy(s => s).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get RM samples for project {ProjectId}", projectId);
            return Result<List<string>>.Fail($"Failed to get RM samples: {ex.Message}");
        }
    }

    public async Task<Result<WeightVolumeCheckSummaryDto>> CheckWeightVolumeAsync(WeightVolumeCheckRequest request)
    {
        try
        {
            var project = await _db.Projects
                .AsNoTracking()
                .Include(p => p.RawDataRows)
                .FirstOrDefaultAsync(p => p.ProjectId == request.ProjectId);

            if (project == null)
                return Result<WeightVolumeCheckSummaryDto>.Fail("Project not found");

            var results = new List<WeightVolumeCheckResultDto>();
            var weightOk = 0;
            var weightError = 0;
            var volumeOk = 0;
            var volumeError = 0;

            foreach (var rawRow in project.RawDataRows)
            {
                var rowData = ParseRowData(rawRow.ColumnData);
                if (rowData == null) continue;

                var solutionLabel = GetSolutionLabel(rawRow.SampleId, rowData);
                if (string.IsNullOrWhiteSpace(solutionLabel)) continue;

                // Get weight and volume values
                var weight = GetColumnValue(rowData, "Act Wgt", "Weight", "Wgt", "ActWgt");
                var volume = GetColumnValue(rowData, "Act Vol", "Volume", "Vol", "ActVol");

                // Check weight
                var weightStatus = WeightVolumeStatus.Skipped;
                string? weightMessage = null;

                if (weight.HasValue)
                {
                    if (request.MinWeight.HasValue && weight < request.MinWeight)
                    {
                        weightStatus = WeightVolumeStatus.TooLow;
                        weightMessage = $"Weight {weight:F4} < {request.MinWeight}";
                        weightError++;
                    }
                    else if (request.MaxWeight.HasValue && weight > request.MaxWeight)
                    {
                        weightStatus = WeightVolumeStatus.TooHigh;
                        weightMessage = $"Weight {weight:F4} > {request.MaxWeight}";
                        weightError++;
                    }
                    else if (request.ExpectedWeight.HasValue)
                    {
                        var tolerance = request.ExpectedWeight.Value * (request.TolerancePercent / 100);
                        if (Math.Abs(weight.Value - request.ExpectedWeight.Value) <= tolerance)
                        {
                            weightStatus = WeightVolumeStatus.Ok;
                            weightOk++;
                        }
                        else
                        {
                            weightStatus = weight < request.ExpectedWeight ?
                                WeightVolumeStatus.TooLow : WeightVolumeStatus.TooHigh;
                            weightMessage = $"Weight {weight:F4} outside tolerance of {request.ExpectedWeight} ± {tolerance:F4}";
                            weightError++;
                        }
                    }
                    else
                    {
                        weightStatus = WeightVolumeStatus.Ok;
                        weightOk++;
                    }
                }
                else
                {
                    weightStatus = WeightVolumeStatus.Missing;
                }

                // Check volume
                var volumeStatus = WeightVolumeStatus.Skipped;
                string? volumeMessage = null;

                if (volume.HasValue)
                {
                    if (request.MinVolume.HasValue && volume < request.MinVolume)
                    {
                        volumeStatus = WeightVolumeStatus.TooLow;
                        volumeMessage = $"Volume {volume:F2} < {request.MinVolume}";
                        volumeError++;
                    }
                    else if (request.MaxVolume.HasValue && volume > request.MaxVolume)
                    {
                        volumeStatus = WeightVolumeStatus.TooHigh;
                        volumeMessage = $"Volume {volume:F2} > {request.MaxVolume}";
                        volumeError++;
                    }
                    else if (request.ExpectedVolume.HasValue)
                    {
                        var tolerance = request.ExpectedVolume.Value * (request.TolerancePercent / 100);
                        if (Math.Abs(volume.Value - request.ExpectedVolume.Value) <= tolerance)
                        {
                            volumeStatus = WeightVolumeStatus.Ok;
                            volumeOk++;
                        }
                        else
                        {
                            volumeStatus = volume < request.ExpectedVolume ?
                                WeightVolumeStatus.TooLow : WeightVolumeStatus.TooHigh;
                            volumeMessage = $"Volume {volume:F2} outside tolerance of {request.ExpectedVolume} ± {tolerance:F2}";
                            volumeError++;
                        }
                    }
                    else
                    {
                        volumeStatus = WeightVolumeStatus.Ok;
                        volumeOk++;
                    }
                }
                else
                {
                    volumeStatus = WeightVolumeStatus.Missing;
                }

                results.Add(new WeightVolumeCheckResultDto(
                    solutionLabel,
                    weight,
                    volume,
                    weightStatus,
                    volumeStatus,
                    weightMessage,
                    volumeMessage
                ));
            }

            var summary = new WeightVolumeCheckSummaryDto(
                request.ProjectId,
                results.Count,
                weightOk,
                weightError,
                volumeOk,
                volumeError,
                results
            );

            return Result<WeightVolumeCheckSummaryDto>.Success(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check weight/volume for project {ProjectId}", request.ProjectId);
            return Result<WeightVolumeCheckSummaryDto>.Fail($"Failed to check weight/volume: {ex.Message}");
        }
    }

    public async Task<Result<List<WeightVolumeCheckResultDto>>> GetWeightVolumeIssuesAsync(Guid projectId)
    {
        var result = await CheckWeightVolumeAsync(new WeightVolumeCheckRequest(projectId));
        if (!result.Succeeded)
            return Result<List<WeightVolumeCheckResultDto>>.Fail(result.Messages.FirstOrDefault() ?? "Failed");

        var issues = result.Data!.Results
            .Where(r => r.WeightStatus != WeightVolumeStatus.Ok && r.WeightStatus != WeightVolumeStatus.Skipped ||
                       r.VolumeStatus != WeightVolumeStatus.Ok && r.VolumeStatus != WeightVolumeStatus.Skipped)
            .ToList();

        return Result<List<WeightVolumeCheckResultDto>>.Success(issues);
    }

    #region Private Helpers

    private Dictionary<string, object?>? ParseRowData(string? columnData)
    {
        if (string.IsNullOrWhiteSpace(columnData))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(columnData);
        }
        catch
        {
            return null;
        }
    }

    private string? GetSolutionLabel(string? sampleId, Dictionary<string, object?> rowData)
    {
        if (!string.IsNullOrWhiteSpace(sampleId))
            return sampleId;

        if (rowData.TryGetValue("Solution Label", out var sl) && sl != null)
            return sl.ToString();

        if (rowData.TryGetValue("SolutionLabel", out var sl2) && sl2 != null)
            return sl2.ToString();

        return null;
    }

    private string? FindRmMatch(string label, List<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        label = label.Trim();

        foreach (var pattern in patterns)
        {
            var regexPattern = $@"(?:CRM|OREAS)?[\s\-]*({Regex.Escape(pattern)}[a-zA-Z0-9]{{0,2}})\b";

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
                if (label.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return pattern;
                }
            }
        }

        return null;
    }

    private bool IsMetadataColumn(string columnName)
    {
        var metadataColumns = new[]
        {
            "Solution Label", "SolutionLabel", "SampleId", "Sample ID",
            "Type", "Act Wgt", "Act Vol", "DF", "Element", "Int", "Corr Con",
            "Weight", "Volume", "Wgt", "Vol"
        };
        return metadataColumns.Contains(columnName, StringComparer.OrdinalIgnoreCase);
    }

    private string ExtractElementSymbol(string columnName)
    {
        var match = Regex.Match(columnName, @"^([A-Z][a-z]?)");
        return match.Success ? match.Groups[1].Value : columnName;
    }

    private decimal? ParseDecimalValue(object? value)
    {
        if (value == null) return null;

        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetDecimal(out var d))
                return d;
            if (je.ValueKind == JsonValueKind.String && decimal.TryParse(je.GetString(), out var d2))
                return d2;
        }
        else if (decimal.TryParse(value.ToString(), out var d3))
        {
            return d3;
        }

        return null;
    }

    private Dictionary<string, decimal> ParseElementValues(string json)
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

    private decimal? GetColumnValue(Dictionary<string, object?> rowData, params string[] columnNames)
    {
        foreach (var name in columnNames)
        {
            if (rowData.TryGetValue(name, out var val) && val != null)
            {
                return ParseDecimalValue(val);
            }
        }
        return null;
    }

    #endregion
}