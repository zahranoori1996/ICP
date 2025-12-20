using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Provides generic undo endpoints for project-level snapshots.
/// </summary>
[ApiController]
[Route("api/undo")]
public class UndoController : ControllerBase
{
    private readonly IUndoService _undoService;
    private readonly ILogger<UndoController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UndoController"/> class.
    /// </summary>
    /// <param name="undoService">The undo service instance.</param>
    /// <param name="logger">The logger instance.</param>
    public UndoController(IUndoService undoService, ILogger<UndoController> logger)
    {
        _undoService = undoService;
        _logger = logger;
    }

    /// <summary>
    /// Restores the project data to the previous snapshot state (LIFO).
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>The result of the undo operation.</returns>
    [HttpPost("{projectId:guid}")]
    public async Task<ActionResult> Undo(Guid projectId)
    {
        var result = await _undoService.UndoLastAsync(projectId);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }
}
