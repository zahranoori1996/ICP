using Application.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Wrapper;

namespace Api.Controllers;

/// <summary>
/// Handles project processing operations including synchronous and background processing.
/// </summary>
[ApiController]
[Route("api/projects")]
public class ProcessingController : ControllerBase
{
    private readonly IProcessingService _processingService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessingController"/> class.
    /// </summary>
    /// <param name="processingService">The processing service instance.</param>
    public ProcessingController(IProcessingService processingService)
    {
        _processingService = processingService;
    }

    /// <summary>
    /// Processes a project either synchronously or in the background.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project to process.</param>
    /// <param name="background">Indicates whether to process in the background (default: false).</param>
    /// <returns>The processing result or job ID if background processing is requested.</returns>
    [HttpPost("{projectId:guid}/process")]
    public async Task<ActionResult<Result<object>>> ProcessProject(Guid projectId, [FromQuery] bool background = false)
    {
        if (background)
        {
            var res = await _processingService.EnqueueProcessProjectAsync(projectId);
            if (res.Succeeded)
            {
                return Accepted(Result<object>.Success(new { JobId = res.Data }));
            }

            var firstMsg = (res.Messages ?? Array.Empty<string>()).FirstOrDefault();
            return BadRequest(Result<object>.Fail(firstMsg ?? "Enqueue failed"));
        }

        var resSync = await _processingService.ProcessProjectAsync(projectId);
        if (resSync.Succeeded)
        {
            return Ok(Result<object>.Success(new { ProjectStateId = resSync.Data }));
        }

        var firstMsgSync = (resSync.Messages ?? Array.Empty<string>()).FirstOrDefault();
        return BadRequest(Result<object>.Fail(firstMsgSync ?? "Processing failed"));
    }
}