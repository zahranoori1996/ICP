using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Implements the change logging service using the core database context.
/// </summary>
public class ChangeLogService : IChangeLogService
{
    private readonly IsatisDbContext _db;
    private readonly ILogger<ChangeLogService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangeLogService"/> class.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="logger">The logger instance.</param>
    public ChangeLogService(IsatisDbContext db, ILogger<ChangeLogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task LogChangeAsync(Guid projectId, string changeType, string? solutionLabel = null,
        string? element = null, string? oldValue = null, string? newValue = null,
        string? changedBy = null, string? details = null, Guid? batchId = null)
    {
        try
        {
            var log = new ChangeLog
            {
                ProjectId = projectId,
                ChangeType = changeType,
                SolutionLabel = solutionLabel,
                Element = element,
                OldValue = oldValue,
                NewValue = newValue,
                ChangedBy = changedBy,
                Details = details,
                BatchId = batchId,
                Timestamp = DateTime.UtcNow
            };

            _db.ChangeLogs.Add(log);
            await _db.SaveChangesAsync();

            _logger.LogDebug("Logged change: {ChangeType} for {SolutionLabel}", changeType, solutionLabel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log change for project {ProjectId}", projectId);
        }
    }

    /// <inheritdoc/>
    public async Task LogBatchChangesAsync(Guid projectId, string changeType,
        IEnumerable<(string? SolutionLabel, string? Element, string? OldValue, string? NewValue)> changes,
        string? changedBy = null, string? details = null)
    {
        var batchId = Guid.NewGuid();
        var logs = changes.Select(c => new ChangeLog
        {
            ProjectId = projectId,
            ChangeType = changeType,
            SolutionLabel = c.SolutionLabel,
            Element = c.Element,
            OldValue = c.OldValue,
            NewValue = c.NewValue,
            ChangedBy = changedBy,
            Details = details,
            BatchId = batchId,
            Timestamp = DateTime.UtcNow
        }).ToList();

        _db.ChangeLogs.AddRange(logs);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Logged batch of {Count} changes for project {ProjectId}", logs.Count, projectId);
    }

    /// <inheritdoc/>
    public async Task<List<ChangeLog>> GetChangeLogAsync(Guid projectId, int page = 1, int pageSize = 50)
    {
        return await _db.ChangeLogs
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<List<ChangeLog>> GetChangesByTypeAsync(Guid projectId, string changeType)
    {
        return await _db.ChangeLogs
            .Where(c => c.ProjectId == projectId && c.ChangeType == changeType)
            .OrderByDescending(c => c.Timestamp)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<List<ChangeLog>> GetChangesBySampleAsync(Guid projectId, string solutionLabel)
    {
        return await _db.ChangeLogs
            .Where(c => c.ProjectId == projectId && c.SolutionLabel == solutionLabel)
            .OrderByDescending(c => c.Timestamp)
            .ToListAsync();
    }
}