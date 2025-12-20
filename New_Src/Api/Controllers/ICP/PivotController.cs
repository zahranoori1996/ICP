using Application.DTOs;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Handles pivot table operations including data aggregation, filtering, and export functionality.
/// </summary>
[ApiController]
[Route("api/pivot")]
public class PivotController : ControllerBase
{
    private readonly IPivotService _pivotService;
    private readonly ILogger<PivotController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PivotController"/> class.
    /// </summary>
    /// <param name="pivotService">The pivot service instance.</param>
    /// <param name="logger">The logger instance.</param>
    public PivotController(IPivotService pivotService, ILogger<PivotController> logger)
    {
        _pivotService = pivotService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves a basic pivot table for the specified project.
    /// </summary>
    /// <param name="request">The pivot request containing project ID and filtering options.</param>
    /// <returns>The pivot table data.</returns>
    [HttpPost]
    public async Task<ActionResult> GetPivotTable([FromBody] PivotRequest request)
    {
        if (request.ProjectId == Guid.Empty)
        {
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });
        }

        var result = await _pivotService.GetPivotTableAsync(request);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Retrieves an advanced pivot table with GCD (Greatest Common Divisor) and repeat pattern support.
    /// </summary>
    /// <param name="request">The advanced pivot request.</param>
    /// <returns>The advanced pivot table data.</returns>
    [HttpPost("advanced")]
    public async Task<ActionResult> GetAdvancedPivotTable([FromBody] AdvancedPivotRequest request)
    {
        if (request.ProjectId == Guid.Empty)
        {
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });
        }

        var result = await _pivotService.GetAdvancedPivotTableAsync(request);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Analyzes repeat patterns in the project data.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>Analysis results showing repeat patterns.</returns>
    [HttpGet("{projectId:guid}/analyze-repeats")]
    public async Task<ActionResult> AnalyzeRepeats(Guid projectId)
    {
        var result = await _pivotService.AnalyzeRepeatsAsync(projectId);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Retrieves a pivot table using simple query parameters.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="searchText">Optional search text for filtering.</param>
    /// <param name="useOxide">Indicates whether to use oxide values.</param>
    /// <param name="decimalPlaces">The number of decimal places for rounding (default: 2).</param>
    /// <param name="page">The page number (default: 1).</param>
    /// <param name="pageSize">The number of items per page (default: 100).</param>
    /// <returns>The pivot table data.</returns>
    [HttpGet("{projectId:guid}")]
    public async Task<ActionResult> GetPivotTableSimple(
        Guid projectId,
        [FromQuery] string? searchText = null,
        [FromQuery] bool useOxide = false,
        [FromQuery] int decimalPlaces = 2,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        var request = new PivotRequest(
            projectId,
            searchText,
            null, null, null,
            useOxide,
            decimalPlaces,
            page,
            pageSize
        );

        var result = await _pivotService.GetPivotTableAsync(request);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Retrieves all solution labels in the project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>A list of solution labels.</returns>
    [HttpGet("{projectId:guid}/labels")]
    public async Task<ActionResult> GetSolutionLabels(Guid projectId)
    {
        var result = await _pivotService.GetSolutionLabelsAsync(projectId);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Retrieves all elements present in the project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>A list of element names.</returns>
    [HttpGet("{projectId:guid}/elements")]
    public async Task<ActionResult> GetElements(Guid projectId)
    {
        var result = await _pivotService.GetElementsAsync(projectId);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Retrieves statistical information for all columns in the project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>Column statistics including min, max, mean, and standard deviation.</returns>
    [HttpGet("{projectId:guid}/stats")]
    public async Task<ActionResult> GetColumnStats(Guid projectId)
    {
        var result = await _pivotService.GetColumnStatsAsync(projectId);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Detects duplicate rows in the project data.
    /// </summary>
    /// <param name="request">The duplicate detection request.</param>
    /// <returns>A list of detected duplicate rows.</returns>
    [HttpPost("duplicates")]
    public async Task<ActionResult> DetectDuplicates([FromBody] DuplicateDetectionRequest request)
    {
        if (request.ProjectId == Guid.Empty)
        {
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });
        }

        var result = await _pivotService.DetectDuplicatesAsync(request);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Exports the pivot table to a CSV file.
    /// </summary>
    /// <param name="request">The pivot request containing export options.</param>
    /// <returns>A CSV file containing the pivot table data.</returns>
    [HttpPost("export")]
    public async Task<ActionResult> ExportToCsv([FromBody] PivotRequest request)
    {
        if (request.ProjectId == Guid.Empty)
        {
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });
        }

        var result = await _pivotService.ExportToCsvAsync(request);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return File(result.Data!, "text/csv", $"pivot_{request.ProjectId}.csv");
    }
}