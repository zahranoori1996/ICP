namespace Shared.Models;

/// <summary>
/// Represents the possible states of an import or processing job.
/// </summary>
public enum ImportJobState
{
    /// <summary>
    /// The job is pending execution.
    /// </summary>
    Pending,

    /// <summary>
    /// The job is currently running.
    /// </summary>
    Running,

    /// <summary>
    /// The job has completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The job has failed to complete.
    /// </summary>
    Failed
}
