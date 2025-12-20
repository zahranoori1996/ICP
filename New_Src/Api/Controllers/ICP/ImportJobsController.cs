using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Wrapper;

namespace Api.Controllers;

/// <summary>
/// Handles background import job management and status tracking.
/// </summary>
[ApiController]
[Route("api/projects/import")]
public class ImportJobsController : ControllerBase
{
    private readonly IsatisDbContext _db;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportJobsController"/> class.
    /// </summary>
    /// <param name="db">The database context.</param>
    public ImportJobsController(IsatisDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Retrieves a paginated list of import jobs.
    /// </summary>
    /// <param name="page">The page number (default: 1).</param>
    /// <param name="pageSize">The number of items per page (default: 20).</param>
    /// <returns>A paginated list of import jobs with their status.</returns>
    [HttpGet("jobs")]
    public async Task<ActionResult<Result<object>>> ListJobs([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.ProjectImportJobs.OrderByDescending(j => j.CreatedAt);
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(j => new
            {
                j.JobId,
                j.ProjectName,
                j.State,
                j.TotalRows,
                j.ProcessedRows,
                j.Percent,
                j.Message,
                j.ResultProjectId,
                j.CreatedAt,
                j.UpdatedAt
            }).ToListAsync();

        return Ok(Result<object>.Success(new { total, page, pageSize, items }));
    }

    /// <summary>
    /// Retrieves detailed information about a specific import job.
    /// </summary>
    /// <param name="jobId">The unique identifier of the import job.</param>
    /// <returns>The import job details.</returns>
    [HttpGet("{jobId:guid}")]
    public async Task<ActionResult<Result<object>>> GetJob(Guid jobId)
    {
        var job = await _db.ProjectImportJobs.FindAsync(jobId);
        if (job == null) return NotFound(Result<object>.Fail("Job not found"));

        return Ok(Result<object>.Success(new
        {
            job.JobId,
            job.ProjectName,
            job.State,
            job.TotalRows,
            job.ProcessedRows,
            job.Percent,
            job.Message,
            job.ResultProjectId,
            job.TempFilePath,
            job.CreatedAt,
            job.UpdatedAt
        }));
    }

    /// <summary>
    /// Cancels an import job by marking it as failed.
    /// </summary>
    /// <param name="jobId">The unique identifier of the import job to cancel.</param>
    /// <returns>Confirmation of the cancellation.</returns>
    [HttpPost("{jobId:guid}/cancel")]
    public async Task<ActionResult<Result<object>>> CancelJob(Guid jobId)
    {
        var job = await _db.ProjectImportJobs.FindAsync(jobId);
        if (job == null)
        {
            return NotFound(Result<object>.Fail("Job not found"));
        }

        job.State = (int)ImportJobState.Failed;
        job.Message = "Cancelled by user";
        job.UpdatedAt = DateTime.UtcNow;
        _db.ProjectImportJobs.Update(job);
        await _db.SaveChangesAsync();

        return Ok(Result<object>.Success(new { jobId }));
    }
}