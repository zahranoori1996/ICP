using Application.Services;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace Api.Controllers;

/// <summary>
/// Handles project management operations including listing, loading, deleting, and processing projects.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly IImportQueueService _queue;
    private readonly Application.Services.IProjectPersistenceService _persistence;
    private readonly ILogger<ProjectsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectsController"/> class.
    /// </summary>
    /// <param name="queue">The import queue service.</param>
    /// <param name="persistence">The project persistence service.</param>
    /// <param name="logger">The logger instance.</param>
    public ProjectsController(IImportQueueService queue, Application.Services.IProjectPersistenceService persistence, ILogger<ProjectsController> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves a paginated list of all projects.
    /// </summary>
    /// <param name="page">The page number (default: 1).</param>
    /// <param name="pageSize">The number of items per page (default: 20).</param>
    /// <returns>A paginated list of projects.</returns>
    [HttpGet]
    public async Task<IActionResult> GetProjects([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var result = await _persistence.ListProjectsAsync(page, pageSize);
            if (result.Succeeded)
            {
                return Ok(new ApiResponse<object>(true, result.Data, Array.Empty<string>()));
            }
            return BadRequest(new ApiResponse<object>(false, null, result.Messages?.ToArray() ?? new[] { "Failed to list projects" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list projects");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>(false, null, new[] { ex.Message }));
        }
    }

    /// <summary>
    /// Retrieves detailed information about a specific project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>The project details.</returns>
    [HttpGet("{projectId:guid}")]
    public async Task<IActionResult> GetProject([FromRoute] Guid projectId)
    {
        try
        {
            var result = await _persistence.LoadProjectAsync(projectId);
            if (result.Succeeded)
            {
                return Ok(new ApiResponse<object>(true, result.Data, Array.Empty<string>()));
            }
            return NotFound(new ApiResponse<object>(false, null, result.Messages?.ToArray() ?? new[] { "Project not found" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get project {ProjectId}", projectId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>(false, null, new[] { ex.Message }));
        }
    }

    /// <summary>
    /// Deletes a project and all its associated data.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project to delete.</param>
    /// <returns>Confirmation of the deletion.</returns>
    [HttpDelete("{projectId:guid}")]
    public async Task<IActionResult> DeleteProject([FromRoute] Guid projectId)
    {
        try
        {
            var result = await _persistence.DeleteProjectAsync(projectId);
            if (result.Succeeded)
            {
                return Ok(new ApiResponse<object>(true, new { deleted = true }, Array.Empty<string>()));
            }
            return NotFound(new ApiResponse<object>(false, null, result.Messages?.ToArray() ?? new[] { "Project not found" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete project {ProjectId}", projectId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>(false, null, new[] { ex.Message }));
        }
    }

    /// <summary>
    /// Enqueues a project for background processing.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project to process.</param>
    /// <param name="background">Indicates whether to process in the background (default: true).</param>
    /// <returns>The job ID for the background processing task.</returns>
    [HttpPost("{projectId:guid}/process")]
    public async Task<IActionResult> EnqueueProcess([FromRoute] Guid projectId, [FromQuery] bool background = true)
    {
        if (projectId == Guid.Empty)
            return BadRequest(new ApiResponse<object>(false, null, new[] { "projectId is required" }));

        try
        {
            var result = await _persistence.LoadProjectAsync(projectId);
            if (!result.Succeeded)
                return NotFound(new ApiResponse<object>(false, null, result.Messages?.ToArray() ?? new[] { "Project not found." }));

            if (background)
            {
                var jobId = await _queue.EnqueueProcessJobAsync(projectId);
                return Ok(new ApiResponse<object>(true, new { jobId }, Array.Empty<string>()));
            }
            else
            {
                return BadRequest(new ApiResponse<object>(false, null, new[] { "Synchronous processing not supported. Use ?background=true." }));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue process for project {ProjectId}", projectId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>(false, null, new[] { ex.Message }));
        }
    }

    // NOTE: Import endpoint moved to ImportController to avoid duplicate routes
    // Use POST /api/projects/import from ImportController instead

    /// <summary>
    /// Retrieves the status of a background import job.
    /// </summary>
    /// <param name="jobId">The unique identifier of the import job.</param>
    /// <returns>The current status of the import job.</returns>
    [HttpGet("import/{jobId:guid}/status")]
    public async Task<IActionResult> GetJobStatus([FromRoute] Guid jobId)
    {
        if (jobId == Guid.Empty)
            return BadRequest(new ApiResponse<object>(false, null, new[] { "jobId is required" }));

        try
        {
            var status = await _queue.GetStatusAsync(jobId);
            if (status == null)
                return NotFound(new ApiResponse<object>(false, null, new[] { "Job not found." }));

            return Ok(new ApiResponse<object>(true, status, Array.Empty<string>()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get job status for {JobId}", jobId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>(false, null, new[] { ex.Message }));
        }
    }

    /// <summary>
    /// Loads a project (alias for GetProject).
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>The project details.</returns>
    [HttpGet("{projectId:guid}/load")]
    public async Task<IActionResult> LoadProject([FromRoute] Guid projectId)
    {
        return await GetProject(projectId);
    }
}
