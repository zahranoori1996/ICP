using Application.DTOs;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Handles Reference Material (RM) checking operations against CRM values.
/// </summary>
[ApiController]
[Route("api/rmcheck")]
public class RmCheckController : ControllerBase
{
    private readonly IRmCheckService _rmCheckService;
    private readonly ILogger<RmCheckController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RmCheckController"/> class.
    /// </summary>
    /// <param name="rmCheckService">The RM check service instance.</param>
    /// <param name="logger">The logger instance.</param>
    public RmCheckController(IRmCheckService rmCheckService, ILogger<RmCheckController> logger)
    {
        _rmCheckService = rmCheckService;
        _logger = logger;
    }

    /// <summary>
    /// Checks RM samples against CRM reference values.
    /// </summary>
    /// <param name="request">The RM check request containing project ID and comparison parameters.</param>
    /// <returns>The comparison results between RM samples and CRM references.</returns>
    [HttpPost]
    public async Task<ActionResult> CheckRm([FromBody] RmCheckRequest request)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        var result = await _rmCheckService.CheckRmAsync(request);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Retrieves all RM samples in the project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>A list of RM samples.</returns>
    [HttpGet("{projectId:guid}/samples")]
    public async Task<ActionResult> GetRmSamples(Guid projectId)
    {
        var result = await _rmCheckService.GetRmSamplesAsync(projectId);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Checks weight and volume values for validity.
    /// </summary>
    /// <param name="request">The weight and volume check request.</param>
    /// <returns>The validation results for weight and volume values.</returns>
    [HttpPost("weight-volume")]
    public async Task<ActionResult> CheckWeightVolume([FromBody] WeightVolumeCheckRequest request)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        var result = await _rmCheckService.CheckWeightVolumeAsync(request);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Retrieves samples with weight or volume issues.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>A list of samples with weight or volume validation issues.</returns>
    [HttpGet("{projectId:guid}/weight-volume-issues")]
    public async Task<ActionResult> GetWeightVolumeIssues(Guid projectId)
    {
        var result = await _rmCheckService.GetWeightVolumeIssuesAsync(projectId);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }
}