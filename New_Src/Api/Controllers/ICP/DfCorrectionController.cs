using Application.DTOs;
using Application.Interface;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/correction")] // Shared Route
public class DfCorrectionController : ControllerBase
{
    private readonly IDfCorrectionService _service;

    public DfCorrectionController(IDfCorrectionService service)
    {
        _service = service;
    }

    [HttpPost("df")]
    public async Task<ActionResult> ApplyDfCorrection([FromBody] DfCorrectionRequest request)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        if (request.SolutionLabels == null || !request.SolutionLabels.Any())
            return BadRequest(new { succeeded = false, messages = new[] { "At least one solution label is required" } });

        var result = await _service.ApplyDfCorrectionAsync(request);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }

    [HttpGet("{projectId:guid}/df-samples")]
    public async Task<ActionResult> GetDfSamples([FromRoute] Guid projectId)
    {
        if (projectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        var result = await _service.GetDfSamplesAsync(projectId);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }
}