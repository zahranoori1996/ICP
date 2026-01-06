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

    /// <summary>
    /// Retrieves CRM method options for a project to mirror Python CRM selection behavior.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>CRM method options per CRM id.</returns>
    [HttpGet("{projectId:guid}/crm-options")]
    public async Task<ActionResult> GetCrmOptions(Guid projectId)
    {
        var result = await _optimizationService.GetCrmOptionsAsync(projectId);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Retrieves per-row CRM selection options (Python-style CRM dialog support).
    /// </summary>
    [HttpGet("{projectId:guid}/crm-selection-options")]
    public async Task<ActionResult> GetCrmSelectionOptions(Guid projectId)
    {
        var result = await _optimizationService.GetCrmSelectionOptionsAsync(projectId);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Returns a small pivot preview for debugging (row order + selected element values).
    /// </summary>
    [HttpGet("{projectId:guid}/pivot-preview")]
    public async Task<ActionResult> GetPivotPreview(
        Guid projectId,
        [FromQuery] int take = 10,
        [FromQuery] string[]? elements = null)
    {
        var result = await _optimizationService.GetPivotPreviewAsync(projectId, take, elements);
        return Ok(new { succeeded = true, data = result });
    }

    /// <summary>
    /// Returns CRM reference values for a CRM id + method (debug helper).
    /// </summary>
    [HttpGet("crm-preview")]
    public async Task<ActionResult> GetCrmPreview(
        [FromQuery] string crmId,
        [FromQuery] string? method = null,
        [FromQuery] string[]? elements = null)
    {
        var result = await _optimizationService.GetCrmPreviewAsync(crmId, method, elements);
        return Ok(new { succeeded = true, data = result });
    }

    /// <summary>
    /// Returns best-blank debug info (candidates + selected) for requested elements.
    /// </summary>
    [HttpGet("{projectId:guid}/blank-preview")]
    public async Task<ActionResult> GetBlankPreview(
        Guid projectId,
        [FromQuery] string[]? elements = null,
        [FromQuery] decimal rangeLow = 2m,
        [FromQuery] decimal rangeMid = 20m,
        [FromQuery] decimal rangeHigh1 = 10m,
        [FromQuery] decimal rangeHigh2 = 8m,
        [FromQuery] decimal rangeHigh3 = 5m,
        [FromQuery] decimal rangeHigh4 = 3m)
    {
        var result = await _optimizationService.GetBlankPreviewAsync(
            projectId,
            elements,
            rangeLow,
            rangeMid,
            rangeHigh1,
            rangeHigh2,
            rangeHigh3,
            rangeHigh4);
        return Ok(new { succeeded = true, data = result });
    }

    /// <summary>
    /// Saves per-row CRM selections for a project.
    /// </summary>
    [HttpPost("{projectId:guid}/crm-selections")]
    public async Task<ActionResult> SaveCrmSelections(Guid projectId, [FromBody] CrmSelectionSaveRequest request)
    {
        if (projectId != request.ProjectId)
        {
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId mismatch." } });
        }

        var user = User?.Identity?.Name;
        var result = await _optimizationService.SaveCrmSelectionsAsync(request, user);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }
}
