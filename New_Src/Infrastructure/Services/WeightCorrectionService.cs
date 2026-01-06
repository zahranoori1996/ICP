using Application.DTOs;
using Application.Services;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Wrapper;
using System.Text.Json;

namespace Infrastructure.Services;

public class WeightCorrectionService : BaseCorrectionService, IWeightCorrectionService
{
    public WeightCorrectionService(
        IsatisDbContext db,
        IChangeLogService changeLogService,
        ILogger<WeightCorrectionService> logger)
        : base(db, changeLogService, logger)
    {
    }

    public async Task<Result<List<BadSampleDto>>> FindBadWeightsAsync(FindBadWeightsRequest request)
    {
        try
        {
            var rawRows = await _db.RawDataRows
                .AsNoTracking()
                .Where(r => r.ProjectId == request.ProjectId)
                .ToListAsync();

            if (!rawRows.Any())
                return Result<List<BadSampleDto>>.Fail("No data found for project");

            var badWeights = new List<BadSampleDto>();
            var expectedWeight = (request.WeightMin + request.WeightMax) / 2;

            foreach (var row in rawRows)
            {
                try
                {
                    using var doc = JsonDocument.Parse(row.ColumnData);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("Type", out var typeElement) &&
                        typeElement.GetString() != "Samp")
                        continue;

                    decimal corrCon;
                    if (root.TryGetProperty("Corr Con", out var corrConElement) &&
                        corrConElement.ValueKind != JsonValueKind.Null)
                    {
                        if (corrConElement.ValueKind == JsonValueKind.Number)
                            corrCon = corrConElement.GetDecimal();
                        else if (corrConElement.ValueKind == JsonValueKind.String &&
                                 decimal.TryParse(corrConElement.GetString(), out var parsedCorrCon))
                            corrCon = parsedCorrCon;
                        else
                            continue;
                    }
                    else
                    {
                        continue;
                    }

                    if (!root.TryGetProperty("Act Wgt", out var weightElement))
                        continue;

                    if (weightElement.ValueKind == JsonValueKind.Null)
                        continue;

                    decimal weight;
                    if (weightElement.ValueKind == JsonValueKind.Number)
                        weight = weightElement.GetDecimal();
                    else if (weightElement.ValueKind == JsonValueKind.String &&
                             decimal.TryParse(weightElement.GetString(), out var parsedWeight))
                        weight = parsedWeight;
                    else
                        continue;

                    if (weight < request.WeightMin || weight > request.WeightMax)
                    {
                        var solutionLabel = root.TryGetProperty("Solution Label", out var labelElement)
                            ? labelElement.GetString() ?? row.SampleId ?? "Unknown"
                            : row.SampleId ?? "Unknown";

                        if (!badWeights.Any(b => b.SolutionLabel == solutionLabel))
                        {
                            badWeights.Add(new BadSampleDto(
                                solutionLabel,
                                weight,
                                corrCon,
                                expectedWeight,
                                Math.Abs(weight - expectedWeight)
                            ));
                        }
                    }
                }
                catch (JsonException)
                {
                    continue;
                }
            }

            return Result<List<BadSampleDto>>.Success(badWeights.OrderByDescending(b => b.Deviation).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find bad weights for project {ProjectId}", request.ProjectId);
            return Result<List<BadSampleDto>>.Fail($"Failed to find bad weights: {ex.Message}");
        }
    }

    public async Task<Result<CorrectionResultDto>> ApplyWeightCorrectionAsync(WeightCorrectionRequest request)
    {
        try
        {
            if (request.NewWeight <= 0)
                return Result<CorrectionResultDto>.Fail("New weight must be positive");

            var rawRows = await _db.RawDataRows
                .Where(r => r.ProjectId == request.ProjectId)
                .ToListAsync();

            if (!rawRows.Any())
                return Result<CorrectionResultDto>.Fail("No data found for project");

            await SaveUndoStateAsync(request.ProjectId, "WeightCorrection");

            var correctedSamples = new List<CorrectedSampleInfo>();
            var changeLogEntries = new List<(string? SolutionLabel, string? Element, string? OldValue, string? NewValue)>();
            int correctedRows = 0;

            foreach (var row in rawRows)
            {
                try
                {
                    using var doc = JsonDocument.Parse(row.ColumnData);
                    var root = doc.RootElement;

                    var solutionLabel = root.TryGetProperty("Solution Label", out var labelElement)
                        ? labelElement.GetString() ?? row.SampleId
                        : row.SampleId;

                    if (solutionLabel == null || !request.SolutionLabels.Contains(solutionLabel))
                        continue;

                    if (root.TryGetProperty("Type", out var typeElement) &&
                        typeElement.GetString() != "Samp")
                        continue;

                    decimal oldCorrCon;
                    if (root.TryGetProperty("Corr Con", out var corrConElement) &&
                        corrConElement.ValueKind != JsonValueKind.Null)
                    {
                        if (corrConElement.ValueKind == JsonValueKind.Number)
                            oldCorrCon = corrConElement.GetDecimal();
                        else if (corrConElement.ValueKind == JsonValueKind.String &&
                                 decimal.TryParse(corrConElement.GetString(), out var parsedCorrCon))
                            oldCorrCon = parsedCorrCon;
                        else
                            continue;
                    }
                    else
                    {
                        continue;
                    }

                    if (!root.TryGetProperty("Act Wgt", out var weightElement))
                        continue;

                    if (weightElement.ValueKind == JsonValueKind.Null)
                        continue;

                    decimal oldWeight;
                    if (weightElement.ValueKind == JsonValueKind.Number)
                        oldWeight = weightElement.GetDecimal();
                    else if (!decimal.TryParse(weightElement.GetString(), out oldWeight))
                        continue;

                    if (oldWeight == 0)
                        continue;


                    // ✅ Python math: newCorrCon = oldCorrCon * (newWeight / oldWeight)
                    var newCorrCon = oldCorrCon * (request.NewWeight / oldWeight);

                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(row.ColumnData);
                    if (dict != null)
                    {
                        dict["Act Wgt"] = request.NewWeight;
                        dict["Corr Con"] = newCorrCon;

                        row.ColumnData = JsonSerializer.Serialize(dict);
                        correctedRows++;

                        if (!correctedSamples.Any(s => s.SolutionLabel == solutionLabel))
                        {
                            correctedSamples.Add(new CorrectedSampleInfo(
                                solutionLabel,
                                oldWeight,
                                request.NewWeight,
                                oldCorrCon,
                                newCorrCon
                            ));
                        }

                        var element = dict.ContainsKey("Element") ? dict["Element"]?.ToString() : "Unknown";
                        changeLogEntries.Add((solutionLabel, element, oldWeight.ToString(), request.NewWeight.ToString()));
                    }
                }
                catch
                {
                }
            }

            await _db.SaveChangesAsync();
            await _changeLogService.LogBatchChangesAsync(request.ProjectId, "WeightCorrection", changeLogEntries);

            return Result<CorrectionResultDto>.Success(new CorrectionResultDto(
                rawRows.Count,
                correctedRows,
                correctedSamples
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply weight correction");
            return Result<CorrectionResultDto>.Fail($"Error: {ex.Message}");
        }
    }
}
