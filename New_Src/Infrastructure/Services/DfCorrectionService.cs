using Application.DTOs;
using Application.Services;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Wrapper;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Infrastructure.Services;

public class DfCorrectionService : BaseCorrectionService, IDfCorrectionService
{
    public DfCorrectionService(
        IsatisDbContext db,
        IChangeLogService changeLogService,
        ILogger<DfCorrectionService> logger)
        : base(db, changeLogService, logger)
    {
    }

    public async Task<Result<List<DfSampleDto>>> GetDfSamplesAsync(Guid projectId)
    {
        try
        {
            var rawRows = await _db.RawDataRows
                .AsNoTracking()
                .Where(r => r.ProjectId == projectId)
                .OrderBy(r => r.DataId)
                .ToListAsync();

            if (!rawRows.Any())
                return Result<List<DfSampleDto>>.Fail("No data found for project");

            var badDfSamples = new List<DfSampleDto>();
            var seenLabels = new HashSet<string>();
            int rowNum = 1;

            var dfPattern = new Regex(@"D(\d+)(?:-|\b|$)", RegexOptions.IgnoreCase);

            foreach (var row in rawRows)
            {
                try
                {
                    using var doc = JsonDocument.Parse(row.ColumnData);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("Type", out var typeElement) &&
                        typeElement.GetString() != "Samp")
                    {
                        rowNum++;
                        continue;
                    }

                    var solutionLabel = root.TryGetProperty("Solution Label", out var labelElement)
                        ? labelElement.GetString() ?? row.SampleId ?? $"Row-{rowNum}"
                        : row.SampleId ?? $"Row-{rowNum}";

                    if (seenLabels.Contains(solutionLabel))
                    {
                        rowNum++;
                        continue;
                    }

                    decimal actualDf = 1m;
                    if (root.TryGetProperty("DF", out var dfElement) && dfElement.ValueKind != JsonValueKind.Null)
                    {
                        if (dfElement.ValueKind == JsonValueKind.Number)
                            actualDf = dfElement.GetDecimal();
                        else if (dfElement.ValueKind == JsonValueKind.String &&
                                 decimal.TryParse(dfElement.GetString(), out var parsedDf))
                            actualDf = parsedDf;
                    }

                    decimal expectedDf = 1m;
                    var match = dfPattern.Match(solutionLabel);
                    if (match.Success && decimal.TryParse(match.Groups[1].Value, out var extractedDf))
                    {
                        expectedDf = extractedDf;
                    }

                    if (actualDf != expectedDf)
                    {
                        seenLabels.Add(solutionLabel);
                        badDfSamples.Add(new DfSampleDto(rowNum, solutionLabel, actualDf, "Samp"));
                    }

                    rowNum++;
                }
                catch (JsonException)
                {
                    rowNum++;
                    continue;
                }
            }

            return Result<List<DfSampleDto>>.Success(badDfSamples);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get DF samples for project {ProjectId}", projectId);
            return Result<List<DfSampleDto>>.Fail($"Failed: {ex.Message}");
        }
    }

    public async Task<Result<CorrectionResultDto>> ApplyDfCorrectionAsync(DfCorrectionRequest request)
    {
        try
        {
            if (request.NewDf <= 0)
                return Result<CorrectionResultDto>.Fail("New DF must be positive");

            var rawRows = await _db.RawDataRows
                .Where(r => r.ProjectId == request.ProjectId)
                .ToListAsync();

            if (!rawRows.Any())
                return Result<CorrectionResultDto>.Fail("No data found for project");

            await SaveUndoStateAsync(request.ProjectId, "DfCorrection");

            var correctedSamples = new List<CorrectedSampleInfo>();
            var changeLogEntries = new List<(string? SolutionLabel, string? Element, string? OldValue, string? NewValue)>();
            var correctedRows = 0;

            foreach (var row in rawRows)
            {
                try
                {
                    using var doc = JsonDocument.Parse(row.ColumnData);
                    var root = doc.RootElement;

                    var solutionLabel = root.TryGetProperty("Solution Label", out var labelElement)
                        ? labelElement.GetString() ?? row.SampleId
                        : row.SampleId;

                    if (solutionLabel == null || request.SolutionLabels == null || !request.SolutionLabels.Contains(solutionLabel))
                        continue;

                    if (!root.TryGetProperty("DF", out var dfElement))
                        continue;

                    if (dfElement.ValueKind == JsonValueKind.Null)
                        continue;

                    decimal oldDf;
                    if (dfElement.ValueKind == JsonValueKind.Number)
                        oldDf = dfElement.GetDecimal();
                    else if (dfElement.ValueKind == JsonValueKind.String &&
                             decimal.TryParse(dfElement.GetString(), out var parsedDf))
                        oldDf = parsedDf;
                    else
                        continue;

                    if (oldDf == 0) continue;

                    decimal oldCorrCon = 0m;
                    if (root.TryGetProperty("Corr Con", out var corrConElement) &&
                        corrConElement.ValueKind != JsonValueKind.Null)
                    {
                        if (corrConElement.ValueKind == JsonValueKind.Number)
                            oldCorrCon = corrConElement.GetDecimal();
                        else if (corrConElement.ValueKind == JsonValueKind.String &&
                                 decimal.TryParse(corrConElement.GetString(), out var parsedCorrCon))
                            oldCorrCon = parsedCorrCon;
                    }

                    // ✅ Python DF behavior: DO NOT scale Corr Con
                    var newCorrCon = oldCorrCon;

                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.ColumnData);
                    var newDict = new Dictionary<string, object>();

                    foreach (var kvp in dict!)
                    {
                        if (kvp.Key == "DF")
                            newDict[kvp.Key] = request.NewDf;
                        // ❌ do not modify Corr Con (keep existing JSON value)
                        else
                            newDict[kvp.Key] = GetJsonValue(kvp.Value);
                    }

                    row.ColumnData = JsonSerializer.Serialize(newDict);
                    correctedRows++;

                    if (!correctedSamples.Any(s => s.SolutionLabel == solutionLabel))
                    {
                        correctedSamples.Add(new CorrectedSampleInfo(
                            solutionLabel,
                            oldDf,
                            request.NewDf,
                            oldCorrCon,
                            newCorrCon
                        ));

                        changeLogEntries.Add((solutionLabel, "DF", oldDf.ToString(), request.NewDf.ToString()));
                        // ❌ Do NOT log Corr Con change (python DF does not change it)
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
                await _changeLogService.LogBatchChangesAsync(
                    request.ProjectId,
                    "DF",
                    changeLogEntries,
                    request.ChangedBy,
                    $"DF correction: {correctedSamples.Count} samples corrected to {request.NewDf}"
                );
            }

            _logger.LogInformation("DF correction applied: {CorrectedRows} rows for project {ProjectId}",
                correctedRows, request.ProjectId);

            return Result<CorrectionResultDto>.Success(new CorrectionResultDto(
                rawRows.Count,
                correctedRows,
                correctedSamples
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply DF correction for project {ProjectId}", request.ProjectId);
            return Result<CorrectionResultDto>.Fail($"Failed to apply DF correction: {ex.Message}");
        }
    }
}