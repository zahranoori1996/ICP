using Application.DTOs;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/correction")] // Shared Route
public class WeightCorrectionController : ControllerBase
{
    private readonly IWeightCorrectionService _service;

    public WeightCorrectionController(IWeightCorrectionService service)
    {
        _service = service;
    }

    [HttpPost("bad-weights")]
    public async Task<ActionResult> FindBadWeights([FromBody] FindBadWeightsRequest request)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        var result = await _service.FindBadWeightsAsync(request);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }

    [HttpPost("weight")]
    public async Task<ActionResult> ApplyWeightCorrection([FromBody] WeightCorrectionRequest request)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        if (request.SolutionLabels == null || !request.SolutionLabels.Any())
            return BadRequest(new { succeeded = false, messages = new[] { "At least one solution label is required" } });

        var result = await _service.ApplyWeightCorrectionAsync(request);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }
}