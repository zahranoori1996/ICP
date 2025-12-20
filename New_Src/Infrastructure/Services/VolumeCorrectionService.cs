using Application.DTOs;
using Application.Services;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Wrapper;
using System.Text.Json;

namespace Infrastructure.Services;

public class VolumeCorrectionService : BaseCorrectionService, IVolumeCorrectionService
{
    public VolumeCorrectionService(
        IsatisDbContext db,
        IChangeLogService changeLogService,
        ILogger<VolumeCorrectionService> logger)
        : base(db, changeLogService, logger)
    {
    }

    public async Task<Result<List<BadSampleDto>>> FindBadVolumesAsync(FindBadVolumesRequest request)
    {
        try
        {
            var rawRows = await _db.RawDataRows
                .AsNoTracking()
                .Where(r => r.ProjectId == request.ProjectId)
                .ToListAsync();

            if (!rawRows.Any())
                return Result<List<BadSampleDto>>.Fail("No data found for project");

            var badVolumes = new List<BadSampleDto>();

            foreach (var row in rawRows)
            {
                try
                {
                    using var doc = JsonDocument.Parse(row.ColumnData);
                    var root = doc.RootElement;

                    // 1. Check Type == "Samp"
                    if (root.TryGetProperty("Type", out var typeElement) &&
                        typeElement.GetString() != "Samp")
                        continue;

                    // 2. Strict Check for Corr Con (Matches Python's notna() filter)
                    // If Corr Con is missing, null, or invalid, we skip the row entirely.
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
                            continue; // Invalid number format -> Skip
                    }
                    else
                    {
                        continue; // Missing or Null -> Skip
                    }

                    // 3. Check Act Vol
                    if (!root.TryGetProperty("Act Vol", out var volumeElement))
                        continue;

                    if (volumeElement.ValueKind == JsonValueKind.Null)
                        continue;

                    decimal volume;
                    if (volumeElement.ValueKind == JsonValueKind.Number)
                        volume = volumeElement.GetDecimal();
                    else if (volumeElement.ValueKind == JsonValueKind.String &&
                             decimal.TryParse(volumeElement.GetString(), out var parsedVolume))
                        volume = parsedVolume;
                    else
                        continue;

                    // 4. Compare with Expected Volume
                    if (volume != request.ExpectedVolume)
                    {
                        var solutionLabel = root.TryGetProperty("Solution Label", out var labelElement)
                            ? labelElement.GetString() ?? row.SampleId ?? "Unknown"
                            : row.SampleId ?? "Unknown";

                        if (!badVolumes.Any(b => b.SolutionLabel == solutionLabel))
                        {
                            badVolumes.Add(new BadSampleDto(
                                solutionLabel,
                                volume,
                                corrCon,
                                request.ExpectedVolume,
                                Math.Abs(volume - request.ExpectedVolume)
                            ));
                        }
                    }
                }
                catch (JsonException)
                {
                    continue;
                }
            }

            return Result<List<BadSampleDto>>.Success(badVolumes.OrderByDescending(b => b.Deviation).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find bad volumes for project {ProjectId}", request.ProjectId);
            return Result<List<BadSampleDto>>.Fail($"Failed to find bad volumes: {ex.Message}");
        }
    }

    public async Task<Result<CorrectionResultDto>> ApplyVolumeCorrectionAsync(VolumeCorrectionRequest request)
    {
        try
        {
            if (request.NewVolume <= 0)
                return Result<CorrectionResultDto>.Fail("New volume must be positive");

            var rawRows = await _db.RawDataRows
                .Where(r => r.ProjectId == request.ProjectId)
                .ToListAsync();

            if (!rawRows.Any())
                return Result<CorrectionResultDto>.Fail("No data found for project");

            await SaveUndoStateAsync(request.ProjectId, "VolumeCorrection");

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

                    if (solutionLabel == null || !request.SolutionLabels.Contains(solutionLabel))
                        continue;

                    // 1. Check Type == "Samp"
                    if (root.TryGetProperty("Type", out var typeElement) &&
                        typeElement.GetString() != "Samp")
                        continue;

                    // 2. Strict Check for Corr Con (Matches Python's notna() filter)
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
                            continue; // Invalid format -> Skip
                    }
                    else
                    {
                        continue; // Missing or Null -> Skip
                    }

                    // 3. Get Old Volume
                    if (!root.TryGetProperty("Act Vol", out var volumeElement))
                        continue;

                    if (volumeElement.ValueKind == JsonValueKind.Null)
                        continue;

                    decimal oldVolume;
                    if (volumeElement.ValueKind == JsonValueKind.Number)
                        oldVolume = volumeElement.GetDecimal();
                    else if (volumeElement.ValueKind == JsonValueKind.String &&
                             decimal.TryParse(volumeElement.GetString(), out var parsedVolume))
                        oldVolume = parsedVolume;
                    else
                        continue;

                    // 4. Calculate New Values (Matches Python logic exactly)
                    // Python: corrected_corr_con = (new_vol / cur_vol) * cur_corr if cur_vol != 0 else cur_corr
                    var newCorrCon = oldVolume != 0
                        ? (request.NewVolume / oldVolume) * oldCorrCon
                        : oldCorrCon;

                    // Prepare new JSON dictionary
                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.ColumnData);
                    var newDict = new Dictionary<string, object>();

                    foreach (var kvp in dict!)
                    {
                        if (kvp.Key == "Act Vol")
                            newDict[kvp.Key] = request.NewVolume; // Always update volume
                        else if (kvp.Key == "Corr Con")
                            newDict[kvp.Key] = newCorrCon; // Update Corr Con
                        else
                            newDict[kvp.Key] = GetJsonValue(kvp.Value);
                    }

                    row.ColumnData = JsonSerializer.Serialize(newDict);
                    correctedRows++;

                    if (!correctedSamples.Any(s => s.SolutionLabel == solutionLabel))
                    {
                        correctedSamples.Add(new CorrectedSampleInfo(
                            solutionLabel,
                            oldVolume,
                            request.NewVolume,
                            oldCorrCon,
                            newCorrCon
                        ));

                        changeLogEntries.Add((solutionLabel, "Act Vol", oldVolume.ToString(), request.NewVolume.ToString()));
                        // Only log Corr Con change if it actually changed (to avoid clutter if volume was 0)
                        if (oldCorrCon != newCorrCon)
                        {
                            changeLogEntries.Add((solutionLabel, "Corr Con", oldCorrCon.ToString(), newCorrCon.ToString()));
                        }
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
                    "Volume",
                    changeLogEntries,
                    request.ChangedBy,
                    $"Volume correction: {correctedSamples.Count} samples corrected to {request.NewVolume}"
                );
            }

            _logger.LogInformation("Volume correction applied: {CorrectedRows} rows for project {ProjectId}",
                correctedRows, request.ProjectId);

            return Result<CorrectionResultDto>.Success(new CorrectionResultDto(
                rawRows.Count,
                correctedRows,
                correctedSamples
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply volume correction for project {ProjectId}", request.ProjectId);
            return Result<CorrectionResultDto>.Fail($"Failed to apply volume correction: {ex.Message}");
        }
    }
}