using System.Text.Json;
using Application.Services;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Wrapper;

namespace Infrastructure.Services;

/// <summary>
/// Implements project-level undo functionality by restoring the last saved snapshot (LIFO).
/// Snapshots are stored in <c>ProjectStates</c> with a <c>Description</c> prefix of <c>"Undo:"</c>.
/// </summary>
public sealed class UndoService : IUndoService
{
    private readonly IsatisDbContext _db;
    private readonly ILogger<UndoService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UndoService"/> class.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="logger">The logger instance.</param>
    public UndoService(IsatisDbContext db, ILogger<UndoService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Restores the project data to the previous snapshot state (LIFO).
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>The result of the undo operation.</returns>
    public async Task<Result<string>> UndoLastAsync(Guid projectId)
    {
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var lastState = await _db.ProjectStates
                    .Where(s => s.ProjectId == projectId &&
                                s.Description != null &&
                                EF.Functions.Like(s.Description, "Undo:%"))
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
        });
    }


    /// <summary>
    /// Represents a snapshot entry for a raw data row stored inside the undo state.
    /// </summary>
    private sealed class SavedRowData
    {
        /// <summary>
        /// Gets or sets the raw data row identifier.
        /// </summary>
        public int DataId { get; set; }

        /// <summary>
        /// Gets or sets the sample identifier.
        /// </summary>
        public string? SampleId { get; set; }

        /// <summary>
        /// Gets or sets the serialized row column data.
        /// </summary>
        public string ColumnData { get; set; } = "";
    }
}
