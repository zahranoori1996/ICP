using Application.DTOs;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Handles optimization operations for blank and scale adjustments using differential evolution algorithms.
/// </summary>
[ApiController]
[Route("api/optimization")]
public class OptimizationController : ControllerBase
{
    private readonly IOptimizationService _optimizationService;
    private readonly ILogger<OptimizationController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OptimizationController"/> class.
    /// </summary>
    /// <param name="optimizationService">The optimization service instance.</param>
    /// <param name="logger">The logger instance.</param>
    public OptimizationController(IOptimizationService optimizationService, ILogger<OptimizationController> logger)
    {
        _optimizationService = optimizationService;
        _logger = logger;
    }

    /// <summary>
    /// Optimizes blank and scale parameters using differential evolution algorithm.
    /// </summary>
    /// <param name="request">The optimization request containing project ID and parameters.</param>
    /// <returns>The optimized blank and scale values.</returns>
    [HttpPost("blank-scale")]
    public async Task<ActionResult> OptimizeBlankScale([FromBody] BlankScaleOptimizationRequest request)
    {
        var result = await _optimizationService.OptimizeBlankScaleAsync(request);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Previews the effect of manual blank and scale adjustments without applying them.
    /// </summary>
    /// <param name="request">The manual adjustment request.</param>
    /// <returns>A preview of the adjusted values.</returns>
    [HttpPost("preview")]
    public async Task<ActionResult> PreviewBlankScale([FromBody] ManualBlankScaleRequest request)
    {
        var result = await _optimizationService.PreviewBlankScaleAsync(request);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Applies manual blank and scale adjustments to the project data.
    /// </summary>
    /// <param name="request">The manual adjustment request.</param>
    /// <returns>The result of applying the adjustments.</returns>
    [HttpPost("apply")]
    public async Task<ActionResult> ApplyBlankScale([FromBody] ManualBlankScaleRequest request)
    {
        var result = await _optimizationService.ApplyManualBlankScaleAsync(request);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Retrieves current CRM comparison statistics for the project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="minDiff">The minimum difference threshold (default: -10).</param>
    /// <param name="maxDiff">The maximum difference threshold (default: 10).</param>
    /// <returns>Statistical comparison data between project values and CRM references.</returns>
    [HttpGet("{projectId:guid}/statistics")]
    public async Task<ActionResult> GetStatistics(
        Guid projectId,
        [FromQuery] decimal minDiff = -10,
        [FromQuery] decimal maxDiff = 10)
    {
        var result = await _optimizationService.GetCurrentStatisticsAsync(projectId, minDiff, maxDiff);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Retrieves project sample labels for debugging CRM matching issues.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>A list of sample labels in the project.</returns>
    [HttpGet("{projectId:guid}/debug-samples")]
    public async Task<ActionResult> DebugSamples(Guid projectId)
    {
        var result = await _optimizationService.GetDebugSamplesAsync(projectId);
        return Ok(new { succeeded = true, data = result });
    }
}