using Application.DTOs;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/correction")] // Shared Route
public class RowCorrectionController : ControllerBase
{
    private readonly IRowCorrectionService _service;

    public RowCorrectionController(IRowCorrectionService service)
    {
        _service = service;
    }

    [HttpPost("apply-optimization")]
    public async Task<ActionResult> ApplyOptimization([FromBody] ApplyOptimizationRequest request)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        if (request.ElementSettings == null || !request.ElementSettings.Any())
            return BadRequest(new { succeeded = false, messages = new[] { "At least one element setting is required" } });

        var result = await _service.ApplyOptimizationAsync(request);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }

    [HttpPost("empty-rows")]
    public async Task<ActionResult> FindEmptyRows([FromBody] FindEmptyRowsRequest request)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        var result = await _service.FindEmptyRowsAsync(request);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }

    [HttpPost("delete-rows")]
    public async Task<ActionResult> DeleteRows([FromBody] DeleteRowsRequest request)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        if (request.SolutionLabels == null || !request.SolutionLabels.Any())
            return BadRequest(new { succeeded = false, messages = new[] { "At least one solution label is required" } });

        var result = await _service.DeleteRowsAsync(request);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }

    [HttpPost("{projectId:guid}/undo")]
    public async Task<ActionResult> UndoLastCorrection([FromRoute] Guid projectId)
    {
        if (projectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        var result = await _service.UndoLastCorrectionAsync(projectId);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = new { undone = result.Data } });
    }
}