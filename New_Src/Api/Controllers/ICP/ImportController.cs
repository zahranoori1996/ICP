using Application.DTOs;
using Application.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Wrapper;

namespace Api.Controllers;

/// <summary>
/// Handles data import operations including CSV file uploads, format detection, and background processing.
/// </summary>
[ApiController]
[Route("api/import")]
public class ImportController : ControllerBase
{
    private readonly IImportService _importService;
    private readonly IImportQueueService _importQueue;
    private readonly ILogger<ImportController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportController"/> class.
    /// </summary>
    /// <param name="importService">The import service instance.</param>
    /// <param name="importQueue">The import queue service for background processing.</param>
    /// <param name="logger">The logger instance.</param>
    public ImportController(
        IImportService importService,
        IImportQueueService importQueue,
        ILogger<ImportController> logger)
    {
        _importService = importService;
        _importQueue = importQueue;
        _logger = logger;
    }

    /// <summary>
    /// Imports a CSV file and creates a new project.
    /// </summary>
    /// <param name="file">The CSV file to import.</param>
    /// <param name="projectName">The name of the project to create.</param>
    /// <param name="owner">The owner of the project (optional).</param>
    /// <param name="stateJson">Additional state information in JSON format (optional).</param>
    /// <param name="background">Indicates whether to process the import in the background (optional).</param>
    /// <returns>The result of the import operation, including the project ID or job ID.</returns>
    [HttpPost("import")]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<ActionResult<Result<object>>> ImportCsv(
        [FromForm] IFormFile? file,
        [FromForm] string? projectName,
        [FromForm] string? owner,
        [FromForm] string? stateJson,
        [FromForm] string? background)
    {
        if (file == null)
        {
            return BadRequest(Result<object>.Fail("File is required"));
        }

        if (file.Length == 0)
        {
            return BadRequest(Result<object>.Fail("File is empty"));
        }

        projectName ??= "ImportedProject";

        if (!string.IsNullOrEmpty(background) && bool.TryParse(background, out var bkg) && bkg)
        {
            using var ms = new MemoryStream();
            await file.OpenReadStream().CopyToAsync(ms);
            ms.Position = 0;

            var jobId = await _importQueue.EnqueueImportAsync(ms, projectName, owner, stateJson);
            return Accepted(Result<object>.Success(new { JobId = jobId }));
        }

        using var stream = file.OpenReadStream();
        var res = await _importService.ImportCsvAsync(stream, projectName, owner, stateJson);
        if (res.Succeeded)
        {
            return Ok(Result<object>.Success(new { ProjectId = res.Data!.ProjectId }));
        }

        var firstMsg = (res.Messages ?? Array.Empty<string>()).FirstOrDefault();
        return BadRequest(Result<object>.Fail(firstMsg ?? "Import failed"));
    }

    /// <summary>
    /// Detects the file format of an uploaded file.
    /// </summary>
    /// <param name="file">The file to analyze.</param>
    /// <returns>The detected file format information.</returns>
    [HttpPost("detect-format")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult> DetectFormat([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { succeeded = false, messages = new[] { "File is required" } });
        }

        using var stream = file.OpenReadStream();
        var result = await _importService.DetectFormatAsync(stream, file.FileName);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Previews the contents of a file before importing.
    /// </summary>
    /// <param name="file">The file to preview.</param>
    /// <param name="previewRows">The number of rows to preview (default: 10).</param>
    /// <returns>A preview of the file contents.</returns>
    [HttpPost("preview")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult> PreviewFile(
        [FromForm] IFormFile file,
        [FromForm] int previewRows = 10)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { succeeded = false, messages = new[] { "File is required" } });
        }

        using var stream = file.OpenReadStream();
        var result = await _importService.PreviewFileAsync(stream, file.FileName, previewRows);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Imports a file with advanced configuration options.
    /// </summary>
    /// <param name="file">The file to import.</param>
    /// <param name="projectName">The name of the project to create.</param>
    /// <param name="owner">The owner of the project (optional).</param>
    /// <param name="forceFormat">The file format to force (optional).</param>
    /// <param name="delimiter">The delimiter character (optional).</param>
    /// <param name="headerRow">The row number containing headers (optional).</param>
    /// <param name="skipLastRow">Indicates whether to skip the last row (default: true).</param>
    /// <param name="autoDetectType">Indicates whether to auto-detect sample types (default: true).</param>
    /// <param name="defaultType">The default sample type (default: "Samp").</param>
    /// <returns>The result of the advanced import operation.</returns>
    [HttpPost("advanced")]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<ActionResult> ImportAdvanced(
        [FromForm] IFormFile file,
        [FromForm] string projectName,
        [FromForm] string? owner = null,
        [FromForm] string? forceFormat = null,
        [FromForm] string? delimiter = null,
        [FromForm] int? headerRow = null,
        [FromForm] bool skipLastRow = true,
        [FromForm] bool autoDetectType = true,
        [FromForm] string? defaultType = "Samp",
        [FromForm] string? device = null,
        [FromForm] string? fileType = null,
        [FromForm] string? description = null)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { succeeded = false, messages = new[] { "File is required" } });
        }

        if (string.IsNullOrWhiteSpace(projectName))
        {
            return BadRequest(new { succeeded = false, messages = new[] { "Project name is required" } });
        }

        FileFormat? format = null;
        if (!string.IsNullOrEmpty(forceFormat) && Enum.TryParse<FileFormat>(forceFormat, true, out var parsed))
        {
            format = parsed;
        }

        var request = new AdvancedImportRequest(
            projectName,
            owner,
            format,
            delimiter,
            headerRow,
            null,
            skipLastRow,
            autoDetectType,
            defaultType,
            device,
            fileType,
            description
        );

        using var stream = file.OpenReadStream();
        var result = await _importService.ImportAdvancedAsync(stream, file.FileName, request);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Imports an additional file into an existing project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="file">The file to import.</param>
    /// <returns>The result of the import operation.</returns>
    [HttpPost("{projectId:guid}/additional")]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<ActionResult> ImportAdditional(
        Guid projectId,
        [FromForm] IFormFile file)
    {
        if (projectId == Guid.Empty)
        {
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { succeeded = false, messages = new[] { "File is required" } });
        }

        using var stream = file.OpenReadStream();
        var result = await _importService.ImportAdditionalAsync(projectId, stream, file.FileName);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Retrieves the status of a background import job.
    /// </summary>
    /// <param name="jobId">The unique identifier of the import job.</param>
    /// <returns>The current status of the import job.</returns>
    [HttpGet("{jobId:guid}/status")]
    public async Task<ActionResult<Result<Shared.Models.ImportJobStatusDto>>> GetStatus(Guid jobId)
    {
        var st = await _importQueue.GetStatusAsync(jobId);
        if (st == null)
        {
            return NotFound(Result<Shared.Models.ImportJobStatusDto>.Fail("Job not found"));
        }

        return Ok(Result<Shared.Models.ImportJobStatusDto>.Success(st));
    }
}