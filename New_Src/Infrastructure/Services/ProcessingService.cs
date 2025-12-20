using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Infrastructure.Services
{
    /// <summary>
    /// Service for processing project data and generating analytics.
    /// </summary>
    public class ProcessingService : IProcessingService
    {
        private readonly IsatisDbContext _db;
        private readonly IImportQueueService _queue;
        private readonly ILogger<ProcessingService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessingService"/> class.
        /// </summary>
        /// <param name="db">The database context.</param>
        /// <param name="queue">The import queue service for background jobs.</param>
        /// <param name="logger">The logger instance.</param>
        public ProcessingService(IsatisDbContext db, IImportQueueService queue, ILogger<ProcessingService> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public async Task<ProcessingResult> EnqueueProcessProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            if (projectId == Guid.Empty)
                return ProcessingResult.Failure("Invalid projectId", data: null);

            try
            {
                var jobId = await _queue.EnqueueProcessJobAsync(projectId);
                return ProcessingResult.Success(jobId, Array.Empty<string>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue processing job for project {ProjectId}", projectId);
                return ProcessingResult.Failure($"Failed to enqueue job: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<ProcessingResult> ProcessProjectAsync(Guid projectId, bool overwriteLatestState = true, CancellationToken cancellationToken = default)
        {
            if (projectId == Guid.Empty)
                return ProcessingResult.Failure("Invalid projectId");

            try
            {
                var project = await _db.Projects
                                       .AsNoTracking()
                                       .FirstOrDefaultAsync(p => p.ProjectId == projectId, cancellationToken);

                if (project is null)
                    return ProcessingResult.Failure("Project not found");

                var rawRows = await _db.Set<RawDataRow>()
                                       .AsNoTracking()
                                       .Where(r => r.ProjectId == projectId)
                                       .Select(r => r.ColumnData)
                                       .ToListAsync(cancellationToken);

                if (rawRows.Count == 0)
                {
                    var emptySnapshot = JsonSerializer.Serialize(new
                    {
                        projectId,
                        processedAt = DateTime.UtcNow,
                        rowCount = 0,
                        summary = new { }
                    });

                    var emptyState = new ProjectState
                    {
                        ProjectId = projectId,
                        Data = emptySnapshot,
                        Description = "Processed (no rows)",
                        Timestamp = DateTime.UtcNow
                    };

                    await _db.Set<ProjectState>().AddAsync(emptyState, cancellationToken);
                    await _db.SaveChangesAsync(cancellationToken);

                    return ProcessingResult.Success(emptyState.StateId, Array.Empty<string>());
                }

                var numericBuckets = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

                foreach (var cd in rawRows)
                {
                    if (string.IsNullOrWhiteSpace(cd))
                        continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(cd);
                        if (doc.RootElement.ValueKind != JsonValueKind.Object)
                            continue;

                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var d))
                            {
                                if (!numericBuckets.TryGetValue(prop.Name, out var list))
                                {
                                    list = new List<double>();
                                    numericBuckets[prop.Name] = list;
                                }
                                list.Add(d);
                            }
                        }
                    }
                    catch (JsonException jex)
                    {
                        _logger.LogWarning(jex, "Skipping malformed raw row for project {ProjectId}", projectId);
                        continue;
                    }
                }

                var summary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in numericBuckets)
                {
                    var arr = kv.Value;
                    var count = arr.Count;
                    var sum = arr.Sum();
                    var mean = count > 0 ? sum / count : 0.0;
                    summary[kv.Key] = new { count, mean };
                }

                var snapshot = new
                {
                    projectId,
                    processedAt = DateTime.UtcNow,
                    rowCount = rawRows.Count,
                    summary
                };

                var snapshotJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = false });

                var nextProcessedId = 1;
                try
                {
                    var maxProcessedId = await _db.Set<ProcessedData>()
                                                  .Where(p => p.ProjectId == projectId)
                                                  .MaxAsync(p => (int?)p.ProcessedId, cancellationToken);
                    nextProcessedId = (maxProcessedId ?? 0) + 1;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not compute max ProcessedId for project {ProjectId}, defaulting to 1", projectId);
                }

                var processed = new ProcessedData
                {
                    ProjectId = projectId,
                    ProcessedId = nextProcessedId,
                    AnalysisType = "Summary",
                    Data = snapshotJson,
                    CreatedAt = DateTime.UtcNow
                };

                // Get parent state (latest active or most recent)
                var parentState = await _db.Set<ProjectState>()
                    .Where(s => s.ProjectId == projectId)
                    .OrderByDescending(s => s.IsActive)
                    .ThenByDescending(s => s.Timestamp)
                    .FirstOrDefaultAsync(cancellationToken);

                // Get next version number
                var maxVersion = await _db.Set<ProjectState>()
                    .Where(s => s.ProjectId == projectId)
                    .MaxAsync(s => (int?)s.VersionNumber, cancellationToken) ?? 0;

                var state = new ProjectState
                {
                    ProjectId = projectId,
                    ParentStateId = parentState?.StateId,
                    VersionNumber = maxVersion + 1,
                    ProcessingType = ProcessingTypes.Import, // Default, should be passed from caller
                    Data = snapshotJson,
                    Description = "Auto-generated summary",
                    Timestamp = DateTime.UtcNow,
                    IsActive = true
                };

                // Use execution strategy for retry-safe transactions
                var strategy = _db.Database.CreateExecutionStrategy();

                return await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
                    try
                    {
                        // Deactivate all previous versions
                        await _db.Set<ProjectState>()
                            .Where(s => s.ProjectId == projectId && s.IsActive)
                            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), cancellationToken);

                        await _db.Set<ProcessedData>().AddAsync(processed, cancellationToken);
                        await _db.Set<ProjectState>().AddAsync(state, cancellationToken);

                        var existingProject = await _db.Projects.FirstOrDefaultAsync(p => p.ProjectId == projectId, cancellationToken);
                        if (existingProject != null)
                        {
                            existingProject.LastModifiedAt = DateTime.UtcNow;
                            _db.Projects.Update(existingProject);
                        }

                        await _db.SaveChangesAsync(cancellationToken);
                        await tx.CommitAsync(cancellationToken);

                        _logger.LogInformation("Processed project {ProjectId}: ProcessedId={ProcessedId}, ProjectStateId={StateId}, Version={Version}",
                            projectId, processed.ProcessedId, state.StateId, state.VersionNumber);

                        return ProcessingResult.Success(state.StateId, Array.Empty<string>());
                    }
                    catch (Exception ex)
                    {
                        try { await tx.RollbackAsync(cancellationToken); } catch { }
                        _logger.LogError(ex, "Processing failed for project {ProjectId}", projectId);
                        return ProcessingResult.Failure($"Processing failed: {ex.Message}");
                    }
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Processing cancelled for project {ProjectId}", projectId);
                return ProcessingResult.Failure("Processing cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while processing project {ProjectId}", projectId);
                return ProcessingResult.Failure(ex.Message);
            }
        }
    }
}