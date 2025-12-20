using Application.DTOs;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Handles drift correction analysis and application for ICP data.
/// </summary>
[ApiController]
[Route("api/drift")]
public class DriftController : ControllerBase
{
    private readonly IDriftCorrectionService _driftService;
    private readonly ILogger<DriftController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DriftController"/> class.
    /// </summary>
    /// <param name="driftService">The drift correction service instance.</param>
    /// <param name="logger">The logger instance.</param>
    public DriftController(IDriftCorrectionService driftService, ILogger<DriftController> logger)
    {
        _driftService = driftService;
        _logger = logger;
    }

    /// <summary>
    /// Analyzes drift patterns in the data without applying corrections.
    /// </summary>
    /// <param name="request">The drift analysis request containing project ID and parameters.</param>
    /// <returns>The drift analysis results.</returns>
    [HttpPost("analyze")]
    public async Task<ActionResult> AnalyzeDrift([FromBody] DriftCorrectionRequest request)
    {
        var result = await _driftService.AnalyzeDriftAsync(request);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Applies drift correction to the project data.
    /// </summary>
    /// <param name="request">The drift correction request containing project ID and correction parameters.</param>
    /// <returns>The result of the drift correction operation.</returns>
    [HttpPost("correct")]
    public async Task<ActionResult> ApplyDriftCorrection([FromBody] DriftCorrectionRequest request)
    {
        var result = await _driftService.ApplyDriftCorrectionAsync(request);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Detects data segments based on pattern matching.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="basePattern">The pattern for base samples (optional).</param>
    /// <param name="conePattern">The pattern for cone samples (optional).</param>
    /// <returns>A list of detected segments.</returns>
    [HttpGet("{projectId:guid}/segments")]
    public async Task<ActionResult> DetectSegments(
        Guid projectId,
        [FromQuery] string? basePattern = null,
        [FromQuery] string? conePattern = null)
    {
        var result = await _driftService.DetectSegmentsAsync(projectId, basePattern, conePattern);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Calculates drift ratios between standard measurements.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>The calculated drift ratios.</returns>
    [HttpGet("{projectId:guid}/ratios")]
    public async Task<ActionResult> CalculateDriftRatios(Guid projectId)
    {
        var result = await _driftService.CalculateDriftRatiosAsync(projectId);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Optimizes the slope parameter for a specific element.
    /// </summary>
    /// <param name="request">The slope optimization request containing element and parameters.</param>
    /// <returns>The optimized slope value.</returns>
    [HttpPost("slope")]
    public async Task<ActionResult> OptimizeSlope([FromBody] SlopeOptimizationRequest request)
    {
        var result = await _driftService.OptimizeSlopeAsync(request);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Resets the slope to zero for a specific element.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="element">The element name.</param>
    /// <returns>Confirmation of the slope reset.</returns>
    [HttpPost("{projectId:guid}/zero-slope/{element}")]
    public async Task<ActionResult> ZeroSlope(Guid projectId, string element)
    {
        var result = await _driftService.ZeroSlopeAsync(projectId, element);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }
}