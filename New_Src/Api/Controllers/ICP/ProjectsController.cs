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

    public record ProjectUpdateRequest(string? ProjectName, string? Device, string? FileType, string? Description);

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
    public async Task<IActionResult> GetProject([FromRoute] Guid projectId, [FromQuery] bool includeRaw = false, [FromQuery] bool includeState = false)
    {
        try
        {
            var result = await _persistence.LoadProjectAsync(projectId, includeRaw, includeState);
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
    /// Retrieves a page of raw rows for the project.
    /// </summary>
    [HttpGet("{projectId:guid}/raw")]
    public async Task<IActionResult> GetProjectRawRows([FromRoute] Guid projectId, [FromQuery] int skip = 0, [FromQuery] int take = 1000)
    {
        try
        {
            var result = await _persistence.GetRawDataRowsAsync(projectId, skip, take);
            if (result.Succeeded)
            {
                return Ok(new ApiResponse<List<RawDataDto>>(true, result.Data, Array.Empty<string>()));
            }

            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<List<RawDataDto>>(false, null, result.Messages?.ToArray() ?? new[] { "Failed to load raw rows" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load raw rows for project {ProjectId}", projectId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<List<RawDataDto>>(false, null, new[] { ex.Message }));
        }
    }

    /// <summary>
    /// Retrieves the latest serialized state for the project.
    /// </summary>
    [HttpGet("{projectId:guid}/state/latest")]
    public async Task<IActionResult> GetLatestProjectState([FromRoute] Guid projectId)
    {
        try
        {
            var result = await _persistence.GetLatestProjectStateJsonAsync(projectId);
            if (result.Succeeded)
            {
                return Ok(new ApiResponse<string?>(true, result.Data, Array.Empty<string>()));
            }

            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<string?>(false, null, result.Messages?.ToArray() ?? new[] { "Failed to load project state" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load latest state for project {ProjectId}", projectId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<string?>(false, null, new[] { ex.Message }));
        }
    }

    /// <summary>
    /// Retrieves the latest serialized state for the project in compressed form.
    /// </summary>
    [HttpGet("{projectId:guid}/state/latest/compressed")]
    public async Task<IActionResult> GetLatestProjectStateCompressed([FromRoute] Guid projectId)
    {
        try
        {
            var result = await _persistence.GetLatestProjectStateCompressedAsync(projectId);
            if (!result.Succeeded)
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>(false, null, result.Messages?.ToArray() ?? new[] { "Failed to load project state (compressed)" }));

            if (result.Data == null || result.Data.Length == 0)
                return NoContent();

            return File(result.Data, "application/gzip", $"project-{projectId}-state.json.gz");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load compressed state for project {ProjectId}", projectId);
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
    /// Update project metadata (name, device, file type, description)
    /// </summary>
    [HttpPut("{projectId:guid}")]
    public async Task<IActionResult> UpdateProject([FromRoute] Guid projectId, [FromBody] ProjectUpdateRequest req)
    {
        if (projectId == Guid.Empty)
            return BadRequest(new ApiResponse<object>(false, null, new[] { "projectId is required" }));

        try
        {
            var owner = HttpContext?.User?.Identity?.Name;
            var saveRes = await _persistence.SaveProjectAsync(projectId, req.ProjectName ?? string.Empty, owner, null, null, req.Device, req.FileType, req.Description);
            if (saveRes.Succeeded)
            {
                return Ok(new ApiResponse<object>(true, new { updated = true }, Array.Empty<string>()));
            }
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>(false, null, saveRes.Messages?.ToArray() ?? new[] { "Failed to update project" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update project {ProjectId}", projectId);
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
            var result = await _persistence.LoadProjectAsync(projectId, includeRawRows: false, includeLatestState: false);
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

    /// <summary>
    /// Backfill missing Device/FileType metadata by attempting to infer from project names.
    /// This is a one-off maintenance endpoint to populate historical projects that lack metadata.
    /// </summary>
    [HttpPost("backfill-metadata")]
    public async Task<IActionResult> BackfillMetadata()
    {
        try
        {
            // Load a large page of projects (caller can re-run if DB is large)
            var listRes = await _persistence.ListProjectsAsync(1, 10000);
            if (!listRes.Succeeded || listRes.Data == null)
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>(false, null, listRes.Messages?.ToArray() ?? new[] { "Failed to load projects for backfill" }));

            var updated = 0;
            foreach (var p in listRes.Data)
            {
                var needsDevice = string.IsNullOrWhiteSpace(p.Device);
                var needsFileType = string.IsNullOrWhiteSpace(p.FileType);
                if (!needsDevice && !needsFileType) continue;

                var (device, fileType) = InferDeviceAndFileTypeFromName(p.ProjectName ?? string.Empty);

                // Only save if we inferred at least one value
                if (string.IsNullOrWhiteSpace(device) && string.IsNullOrWhiteSpace(fileType))
                    continue;

                // Use existing owner when saving
                var owner = p.Owner;
                var saveRes = await _persistence.SaveProjectAsync(p.ProjectId, null, owner, null, null,
                    device == string.Empty ? null : device,
                    fileType == string.Empty ? null : fileType,
                    null);

                if (saveRes.Succeeded) updated++;
            }

            return Ok(new ApiResponse<object>(true, new { updated }, Array.Empty<string>()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backfill failed");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>(false, null, new[] { ex.Message }));
        }
    }

    // Simple inference helper used by the backfill endpoint
    private static (string device, string fileType) InferDeviceAndFileTypeFromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (string.Empty, string.Empty);
        var n = name.ToLowerInvariant();

        string device = string.Empty;
        string fileType = string.Empty;

        if (n.Contains("elan")) device = "Mass elan9000 1";
        else if (n.Contains("oes 735")) device = "OES 735 1";
        else if (n.Contains("oes 715")) device = "OES 715";
        else if (n.Contains("oes")) device = "OES";

        if (n.Contains("4cc")) fileType = "oes 4cc";
        else if (n.Contains("6cc")) fileType = "oes 6cc";
        else if (n.Contains(".txt") || n.Contains(" txt") || n.Contains("txt format")) fileType = "txt format";
        else if (n.Contains(".xls") || n.Contains(".xlsx") || n.Contains("xlsx format")) fileType = "xlsx format";

        return (device, fileType);
    }
}
