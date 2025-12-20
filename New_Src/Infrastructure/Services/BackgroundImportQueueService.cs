using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Infrastructure.Services
{
    /// <summary>
    /// Background service that manages the import queue and processes import jobs.
    /// </summary>
    public class BackgroundImportQueueService : BackgroundService, IImportQueueService, IDisposable
    {
        private readonly Channel<ImportRequest> _channel;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BackgroundImportQueueService> _logger;
        private readonly ConcurrentDictionary<Guid, Shared.Models.ImportJobStatusDto> _statuses;

        /// <summary>
        /// Initializes a new instance of the <see cref="BackgroundImportQueueService"/> class.
        /// </summary>
        /// <param name="serviceProvider">The service provider to access scoped services.</param>
        /// <param name="logger">The logger instance.</param>
        public BackgroundImportQueueService(IServiceProvider serviceProvider, ILogger<BackgroundImportQueueService> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _channel = Channel.CreateUnbounded<ImportRequest>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            _statuses = new ConcurrentDictionary<Guid, Shared.Models.ImportJobStatusDto>();
        }

        /// <inheritdoc/>
        public async Task<Guid> EnqueueImportAsync(Stream csvStream, string projectName, string? owner = null, string? stateJson = null)
        {
            if (string.IsNullOrWhiteSpace(projectName)) throw new ArgumentException("projectName is required", nameof(projectName));
            if (csvStream == null) throw new ArgumentNullException(nameof(csvStream));

            var jobId = Guid.NewGuid();

            try
            {
                using var scope = CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IsatisDbContext>();

                var entity = new ProjectImportJob
                {
                    JobId = jobId,
                    ProjectId = null,
                    ResultProjectId = null,
                    JobType = "import",
                    State = (int)Shared.Models.ImportJobState.Pending,
                    Message = "Queued",
                    Percent = 0,
                    Attempts = 0,
                    LastError = null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    ProjectName = projectName,
                    TempFilePath = null,
                    TotalRows = 0,
                    ProcessedRows = 0,
                    OperationId = Guid.NewGuid()
                };

                db.ProjectImportJobs.Add(entity);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist import job {JobId}", jobId);
                throw;
            }

            var copy = new MemoryStream();
            try { if (csvStream.CanSeek) csvStream.Position = 0; } catch { }
            await csvStream.CopyToAsync(copy);
            copy.Position = 0;

            var req = new ImportRequest
            {
                JobId = jobId,
                JobType = "import",
                Stream = copy,
                ProjectName = projectName,
                Owner = owner,
                StateJson = stateJson,
                ProjectId = null
            };

            _statuses[jobId] = new Shared.Models.ImportJobStatusDto(jobId, Shared.Models.ImportJobState.Pending, 0, 0, "Queued", null, 0);

            _logger.LogInformation("EnqueueImportAsync: enqueuing import job {JobId} projectName={ProjectName}", jobId, projectName);
            await _channel.Writer.WriteAsync(req);

            return jobId;
        }

        /// <inheritdoc/>
        public async Task<Guid> EnqueueProcessJobAsync(Guid projectId)
        {
            if (projectId == Guid.Empty) throw new ArgumentException("projectId is required", nameof(projectId));

            // Validate project exists
            using (var scopeVal = CreateScope())
            {
                var dbVal = scopeVal.ServiceProvider.GetRequiredService<IsatisDbContext>();
                var exists = await dbVal.Projects.FindAsync(projectId);
                if (exists == null) throw new InvalidOperationException($"Project {projectId} not found.");
            }

            var jobId = Guid.NewGuid();
            try
            {
                using var scope = CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IsatisDbContext>();

                var entity = new ProjectImportJob
                {
                    JobId = jobId,
                    JobType = "process",
                    ProjectId = projectId,
                    ResultProjectId = null,
                    State = (int)Shared.Models.ImportJobState.Pending,
                    Message = "Queued",
                    Percent = 0,
                    Attempts = 0,
                    LastError = null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    ProjectName = null,
                    TempFilePath = null,
                    TotalRows = 0,
                    ProcessedRows = 0,
                    OperationId = Guid.NewGuid()
                };

                db.ProjectImportJobs.Add(entity);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist process job {JobId} for project {ProjectId}", jobId, projectId);
                throw;
            }

            var req = new ImportRequest
            {
                JobId = jobId,
                JobType = "process",
                ProjectId = projectId
            };

            _statuses[jobId] = new Shared.Models.ImportJobStatusDto(jobId, Shared.Models.ImportJobState.Pending, 0, 0, "Queued", null, 0);

            _logger.LogInformation("EnqueueProcessJobAsync: enqueuing process job {JobId} for project {ProjectId}", jobId, projectId);
            await _channel.Writer.WriteAsync(req);

            return jobId;
        }

        private IServiceScope CreateScope() => _serviceProvider.CreateScope();

        /// <inheritdoc/>
        public async Task<Shared.Models.ImportJobStatusDto?> GetStatusAsync(Guid jobId)
        {
            if (_statuses.TryGetValue(jobId, out var st)) return st;

            try
            {
                using var scope = CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IsatisDbContext>();
                var entity = await db.ProjectImportJobs.FindAsync(jobId);
                if (entity == null) return null;

                var state = Shared.Models.ImportJobState.Pending;
                if (entity.State == (int)Shared.Models.ImportJobState.Running) state = Shared.Models.ImportJobState.Running;
                else if (entity.State == (int)Shared.Models.ImportJobState.Completed) state = Shared.Models.ImportJobState.Completed;
                else if (entity.State == (int)Shared.Models.ImportJobState.Failed) state = Shared.Models.ImportJobState.Failed;

                var dto = new Shared.Models.ImportJobStatusDto(entity.JobId, state, entity.TotalRows, entity.ProcessedRows, entity.Message, entity.ResultProjectId, entity.Percent);
                _statuses[jobId] = dto;
                return dto;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetStatusAsync: fallback read failed for job {JobId}", jobId);
                return null;
            }
        }

        /// <inheritdoc/>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BackgroundImportQueueService started.");
            _logger.LogDebug("BackgroundImportQueueService: channel reader starting.");

            try
            {
                while (await _channel.Reader.WaitToReadAsync(stoppingToken))
                {
                    while (_channel.Reader.TryRead(out var req))
                    {
                        if (stoppingToken.IsCancellationRequested)
                        {
                            _logger.LogInformation("Stopping requested - aborting processing of job {JobId}", req.JobId);
                            break;
                        }

                        // Mark running in-memory and DB (best-effort)
                        _statuses[req.JobId] = new Shared.Models.ImportJobStatusDto(req.JobId, Shared.Models.ImportJobState.Running, 0, 0, "Running", null, 0);
                        try
                        {
                            using var scopeDbRun = CreateScope();
                            var dbRun = scopeDbRun.ServiceProvider.GetRequiredService<IsatisDbContext>();
                            var row = await dbRun.ProjectImportJobs.FindAsync(req.JobId);
                            if (row != null)
                            {
                                row.State = (int)Shared.Models.ImportJobState.Running;
                                row.Message = "Running";
                                row.UpdatedAt = DateTime.UtcNow;
                                dbRun.ProjectImportJobs.Update(row);
                                await dbRun.SaveChangesAsync();
                            }
                        }
                        catch (Exception exSaveRun)
                        {
                            _logger.LogWarning(exSaveRun, "Failed to update job {JobId} to Running in DB", req.JobId);
                        }

                        _logger.LogInformation("Channel reader picked job {JobId} (type={JobType}, projectId={ProjectId})", req.JobId, req.JobType, req.ProjectId);

                        try
                        {
                            var resultProjectId = await ProcessRequestAsync(req, stoppingToken);

                            // Persist completion
                            try
                            {
                                using var scopeDb = CreateScope();
                                var db = scopeDb.ServiceProvider.GetRequiredService<IsatisDbContext>();
                                var jobEntity = await db.ProjectImportJobs.FindAsync(req.JobId);
                                if (jobEntity != null)
                                {
                                    jobEntity.State = (int)Shared.Models.ImportJobState.Completed;
                                    jobEntity.Message = "Completed";
                                    jobEntity.Percent = 100;
                                    jobEntity.UpdatedAt = DateTime.UtcNow;
                                    if (resultProjectId.HasValue) jobEntity.ResultProjectId = resultProjectId.Value;
                                    db.ProjectImportJobs.Update(jobEntity);
                                    await db.SaveChangesAsync();
                                }
                            }
                            catch (Exception exSaveCompleted)
                            {
                                _logger.LogWarning(exSaveCompleted, "Failed to persist completion for job {JobId}", req.JobId);
                            }

                            _statuses[req.JobId] = new Shared.Models.ImportJobStatusDto(req.JobId, Shared.Models.ImportJobState.Completed, 0, 0, "Completed", resultProjectId, 100);
                            _logger.LogInformation("Processing job {JobId} completed. ResultProjectId={ResultProjectId}", req.JobId, resultProjectId);
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogInformation("Processing cancelled for job {JobId}", req.JobId);
                            try
                            {
                                using var scopeCancel = CreateScope();
                                var dbCancel = scopeCancel.ServiceProvider.GetRequiredService<IsatisDbContext>();
                                var e = await dbCancel.ProjectImportJobs.FindAsync(req.JobId);
                                if (e != null)
                                {
                                    e.State = (int)Shared.Models.ImportJobState.Failed;
                                    e.Message = "Cancelled";
                                    e.UpdatedAt = DateTime.UtcNow;
                                    dbCancel.ProjectImportJobs.Update(e);
                                    await dbCancel.SaveChangesAsync();
                                }
                            }
                            catch { /* swallow */ }

                            _statuses[req.JobId] = new Shared.Models.ImportJobStatusDto(req.JobId, Shared.Models.ImportJobState.Failed, 0, 0, "Cancelled", null, 0);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Unhandled exception while processing job {JobId}", req.JobId);

                            try
                            {
                                using var scopeFail = CreateScope();
                                var dbFail = scopeFail.ServiceProvider.GetRequiredService<IsatisDbContext>();
                                var e = await dbFail.ProjectImportJobs.FindAsync(req.JobId);
                                if (e != null)
                                {
                                    e.State = (int)Shared.Models.ImportJobState.Failed;
                                    e.Message = ex.Message;
                                    e.LastError = ex.ToString();
                                    e.Attempts += 1;
                                    e.UpdatedAt = DateTime.UtcNow;
                                    dbFail.ProjectImportJobs.Update(e);
                                    await dbFail.SaveChangesAsync();
                                }
                            }
                            catch (Exception innerFail)
                            {
                                _logger.LogWarning(innerFail, "Failed to persist failure for job {JobId}", req.JobId);
                            }

                            _statuses[req.JobId] = new Shared.Models.ImportJobStatusDto(req.JobId, Shared.Models.ImportJobState.Failed, 0, 0, ex.Message, null, 0);
                        }
                        finally
                        {
                            try { req.Stream?.Dispose(); } catch { }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("BackgroundImportQueueService cancellation requested - exiting ExecuteAsync.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected exception in BackgroundImportQueueService.ExecuteAsync.");
            }
            finally
            {
                _logger.LogInformation("BackgroundImportQueueService stopping.");
            }
        }

        private static string? ExtractMessageFromResult(object? result)
        {
            if (result == null) return null;

            // Try common property names in order
            var t = result.GetType();

            // Messages: IEnumerable<string>
            var prop = t.GetProperty("Messages") ?? t.GetProperty("Errors");
            if (prop != null)
            {
                var val = prop.GetValue(result) as System.Collections.IEnumerable;
                if (val != null)
                {
                    var items = new List<string>();
                    foreach (var o in val) items.Add(o?.ToString() ?? string.Empty);
                    if (items.Count > 0) return string.Join("; ", items);
                }
            }

            // Message (single string)
            prop = t.GetProperty("Message") ?? t.GetProperty("Error");
            if (prop != null)
            {
                var v = prop.GetValue(result);
                if (v != null) return v.ToString();
            }

            // Fallback: ToString()
            return result.ToString();
        }

        /// <summary>
        /// Processes a single import request.
        /// </summary>
        /// <param name="req">The import request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result project identifier, if applicable.</returns>
        private async Task<Guid?> ProcessRequestAsync(ImportRequest req, CancellationToken cancellationToken)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            if (string.Equals(req.JobType, "process", StringComparison.OrdinalIgnoreCase))
            {
                if (!req.ProjectId.HasValue || req.ProjectId == Guid.Empty) throw new InvalidOperationException("Process job missing ProjectId");

                using var scope = CreateScope();
                var processing = scope.ServiceProvider.GetRequiredService<IProcessingService>();

                _logger.LogInformation("Starting processing for job {JobId} project {ProjectId}", req.JobId, req.ProjectId);

                var result = await processing.ProcessProjectAsync(req.ProjectId.Value, overwriteLatestState: true, cancellationToken: cancellationToken);

                // Expect result to have Succeeded and possibly Messages/Errors
                var succeededProp = result?.GetType().GetProperty("Succeeded");
                var succeeded = succeededProp != null && (bool?)succeededProp.GetValue(result) == true;

                if (!succeeded)
                {
                    var msg = ExtractMessageFromResult(result) ?? "Processing failed";
                    throw new InvalidOperationException(msg);
                }

                // Return the project id as the result
                return req.ProjectId;
            }
            else if (string.Equals(req.JobType, "import", StringComparison.OrdinalIgnoreCase))
            {
                using var scope = CreateScope();
                var importService = scope.ServiceProvider.GetRequiredService<IImportService>();

                _logger.LogInformation("Starting import job {JobId} project {ProjectName}", req.JobId, req.ProjectName);
                
                // Use the new IImportService method which likely takes Stream, name, owner, state
                // Assuming ImportCsvAsync(Stream stream, string projectName, string? owner, string? stateJson)
                // If IImportService has been updated to use DTO, adjust accordingly.
                // Based on ImportController, it uses ImportCsvAsync.
                
                if (req.Stream == null) throw new InvalidOperationException("Import stream is null");

                var result = await importService.ImportCsvAsync(req.Stream, req.ProjectName!, req.Owner, req.StateJson);

                if (!result.Succeeded)
                {
                    var msg = (result.Messages?.FirstOrDefault()) ?? "Import failed";
                    throw new InvalidOperationException(msg);
                }

                if (result.Data != null)
                {
                     // Result is ProjectSaveResult
                     // Serializing/Deserializing is a robust way to handle the object if we don't want to add reference to DTOs here
                     // But we have access to Application!
                     // Actually, ImportCsvAsync returns Result<ProjectSaveResult>.
                     // But Wait! IImportService interface defines it.
                     // The interface likely returns Task<Result<ProjectSaveResult>>.

                     // However, since we are using `var result` from the interface call, we KNOW the type if we look at the interface.
                     // The issue is IImportService.ImportCsvAsync returns Result<ProjectSaveResult>.
                     // So we can just access result.Data.ProjectId directly!
                     
                     return result.Data.ProjectId;
                }

                // If we can't get ID, returns null but job succeeds
                return null;
            }

            throw new InvalidOperationException($"Unknown job type: {req.JobType}");
        }

        public override void Dispose()
        {
            try { _channel.Writer.Complete(); } catch { }
            base.Dispose();
        }

        private sealed class ImportRequest
        {
            public Guid JobId { get; set; }
            public MemoryStream? Stream { get; set; }
            public string? ProjectName { get; set; }
            public string? Owner { get; set; }
            public string? StateJson { get; set; }
            public string? JobType { get; set; }
            public Guid? ProjectId { get; set; }
        }
    }
}