namespace Application.Services;

/// <summary>
/// Defines the contract for services responsible for managing background import and processing queues.
/// </summary>
public interface IImportQueueService
{
    /// <summary>
    /// Adds a new CSV import job to the background processing queue.
    /// </summary>
    /// <param name="csvStream">The stream containing the CSV data to be imported.</param>
    /// <param name="projectName">The name to be assigned to the new project.</param>
    /// <param name="owner">The identifier of the user who owns the project.</param>
    /// <param name="stateJson">An optional JSON string representing the initial project state.</param>
    /// <returns>A task containing the unique identifier of the created background job.</returns>
    Task<Guid> EnqueueImportAsync(Stream csvStream, string projectName, string? owner = null, string? stateJson = null);

    /// <summary>
    /// Adds a new processing job for an existing project to the background queue.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project to be processed.</param>
    /// <returns>A task containing the unique identifier of the created background job.</returns>
    Task<Guid> EnqueueProcessJobAsync(Guid projectId);

    /// <summary>
    /// Retrieves the current execution status of a specific background job.
    /// </summary>
    /// <param name="jobId">The unique identifier of the job to check.</param>
    /// <returns>A task containing the job status, or null if the job is not found.</returns>
    Task<Shared.Models.ImportJobStatusDto?> GetStatusAsync(Guid jobId);
}