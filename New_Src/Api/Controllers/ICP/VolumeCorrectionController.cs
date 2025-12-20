using Application.DTOs;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/correction")] // Shared Route
public class VolumeCorrectionController : ControllerBase
{
    private readonly IVolumeCorrectionService _service;

    public VolumeCorrectionController(IVolumeCorrectionService service)
    {
        _service = service;
    }

    [HttpPost("bad-volumes")]
    public async Task<ActionResult> FindBadVolumes([FromBody] FindBadVolumesRequest request)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        var result = await _service.FindBadVolumesAsync(request);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }

    [HttpPost("volume")]
    public async Task<ActionResult> ApplyVolumeCorrection([FromBody] VolumeCorrectionRequest request)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        if (request.SolutionLabels == null || !request.SolutionLabels.Any())
            return BadRequest(new { succeeded = false, messages = new[] { "At least one solution label is required" } });

        var result = await _service.ApplyVolumeCorrectionAsync(request);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }
}