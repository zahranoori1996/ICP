using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Wrapper;
using System.Text.Json;

namespace Infrastructure.Services;

public abstract class BaseCorrectionService
{
    protected readonly IsatisDbContext _db;
    protected readonly IChangeLogService _changeLogService;
    protected readonly ILogger _logger;

    protected BaseCorrectionService(
        IsatisDbContext db,
        IChangeLogService changeLogService,
        ILogger logger)
    {
        _db = db;
        _changeLogService = changeLogService;
        _logger = logger;
    }

    // --- Shared Methods (Undo & Helpers) ---

    public async Task<Result<bool>> UndoLastCorrectionAsync(Guid projectId)
    {
        try
        {
            var lastState = await _db.ProjectStates
                .Where(s => s.ProjectId == projectId && s.Description != null && s.Description.StartsWith("Undo:"))
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefaultAsync();

            if (lastState == null)
                return Result<bool>.Fail("No undo state found");

            var savedData = JsonSerializer.Deserialize<List<SavedRowData>>(lastState.Data);
            if (savedData == null)
                return Result<bool>.Fail("Invalid undo state data");

            var currentRows = await _db.RawDataRows
                .Where(r => r.ProjectId == projectId)
                .ToListAsync();

            _db.RawDataRows.RemoveRange(currentRows);

            foreach (var saved in savedData)
            {
                _db.RawDataRows.Add(new RawDataRow
                {
                    ProjectId = projectId,
                    SampleId = saved.SampleId,
                    ColumnData = saved.ColumnData
                });
            }

            _db.ProjectStates.Remove(lastState);
            await _db.SaveChangesAsync();

            await _changeLogService.LogChangeAsync(
                projectId,
                "Undo",
                changedBy: null,
                details: $"Undone correction: {lastState.Description}"
            );

            _logger.LogInformation("Undo applied for project {ProjectId}", projectId);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to undo for project {ProjectId}", projectId);
            return Result<bool>.Fail($"Failed to undo: {ex.Message}");
        }
    }

    protected async Task SaveUndoStateAsync(Guid projectId, string operation)
    {
        var rows = await _db.RawDataRows
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .Select(r => new SavedRowData(r.SampleId, r.ColumnData))
            .ToListAsync();

        var stateJson = JsonSerializer.Serialize(rows);

        _db.ProjectStates.Add(new ProjectState
        {
            ProjectId = projectId,
            Data = stateJson,
            Description = $"Undo:{operation}",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }

    protected static object GetJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetDecimal(out var d) ? d : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.GetRawText()
        };
    }

    protected record SavedRowData(string? SampleId, string ColumnData);
}