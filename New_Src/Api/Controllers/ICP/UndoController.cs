using System.Collections.Concurrent;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Provides generic undo endpoints for project-level snapshots.
/// </summary>
[ApiController]
[Route("api/undo")]
public class UndoController : ControllerBase
{
    private readonly IUndoService _undoService;
    private readonly ILogger<UndoController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly ConcurrentDictionary<Guid, UndoJobStatus> Jobs = new();
    private static readonly ConcurrentDictionary<Guid, Guid> ProjectJobs = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="UndoController"/> class.
    /// </summary>
    /// <param name="undoService">The undo service instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="scopeFactory">The DI scope factory for background work.</param>
    public UndoController(IUndoService undoService, ILogger<UndoController> logger, IServiceScopeFactory scopeFactory)
    {
        _undoService = undoService;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Restores the project data to the previous snapshot state (LIFO).
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="background">Run undo in background to avoid timeouts.</param>
    /// <returns>The result of the undo operation.</returns>
    [HttpPost("{projectId:guid}")]
    public async Task<ActionResult> Undo(Guid projectId, [FromQuery] bool background = false)
    {
        if (background)
        {
            var status = StartUndoJob(projectId);
            return Accepted(new { succeeded = true, data = status });
        }

        var result = await _undoService.UndoLastAsync(projectId);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Starts undo in background to avoid request timeouts.
    /// </summary>
    [HttpPost("{projectId:guid}/start")]
    public ActionResult StartUndo(Guid projectId)
    {
        var status = StartUndoJob(projectId);
        return Accepted(new { succeeded = true, data = status });
    }

    /// <summary>
    /// Returns background undo job status by job id.
    /// </summary>
    [HttpGet("jobs/{jobId:guid}")]
    public ActionResult GetJobStatus(Guid jobId)
    {
        if (Jobs.TryGetValue(jobId, out var status))
            return Ok(new { succeeded = true, data = status });

        return NotFound(new { succeeded = false, messages = new[] { "Job not found." } });
    }

    /// <summary>
    /// Returns latest job status for a project if one exists.
    /// </summary>
    [HttpGet("{projectId:guid}/status")]
    public ActionResult GetProjectStatus(Guid projectId)
    {
        if (ProjectJobs.TryGetValue(projectId, out var jobId) && Jobs.TryGetValue(jobId, out var status))
            return Ok(new { succeeded = true, data = status });

        return NotFound(new { succeeded = false, messages = new[] { "No job for project." } });
    }

    private UndoJobStatus StartUndoJob(Guid projectId)
    {
        if (ProjectJobs.TryGetValue(projectId, out var existingJobId) &&
            Jobs.TryGetValue(existingJobId, out var existingStatus) &&
            (existingStatus.State == "pending" || existingStatus.State == "running"))
        {
            return existingStatus;
        }

        var jobId = Guid.NewGuid();
        var status = new UndoJobStatus
        {
            JobId = jobId,
            ProjectId = projectId,
            State = "pending",
            StartedAt = DateTime.UtcNow
        };

        Jobs[jobId] = status;
        ProjectJobs[projectId] = jobId;

        _ = Task.Run(async () =>
        {
            status.State = "running";
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IUndoService>();
                var result = await service.UndoLastAsync(projectId);

                status.State = result.Succeeded ? "completed" : "failed";
                status.Message = result.Succeeded ? result.Data : result.Messages?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                status.State = "failed";
                status.Message = ex.Message;
                _logger.LogError(ex, "Background undo failed for {ProjectId}", projectId);
            }
            finally
            {
                status.FinishedAt = DateTime.UtcNow;
                ProjectJobs.TryRemove(projectId, out _);
            }
        });

        return status;
    }

    private sealed class UndoJobStatus
    {
        public Guid JobId { get; set; }
        public Guid ProjectId { get; set; }
        public string State { get; set; } = "pending";
        public string? Message { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
    }
}
