using Domain.Entities;

namespace Application.Services;

/// <summary>
/// Defines the contract for services responsible for logging, retrieving, and managing audit logs of project modifications.
/// </summary>
public interface IChangeLogService
{
    /// <summary>
    /// Records a single change event to the system's audit log.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project where the change occurred.</param>
    /// <param name="changeType">The category of the change (e.g., "WeightCorrection", "Import").</param>
    /// <param name="solutionLabel">The specific sample label associated with the change, if applicable.</param>
    /// <param name="element">The specific element symbol associated with the change, if applicable.</param>
    /// <param name="oldValue">The state of the value before the modification.</param>
    /// <param name="newValue">The state of the value after the modification.</param>
    /// <param name="changedBy">The username or identifier of the user who performed the action.</param>
    /// <param name="details">A descriptive text providing more context or reasons for the change.</param>
    /// <param name="batchId">An optional GUID to group multiple related changes into a single logical batch.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task LogChangeAsync(Guid projectId, string changeType, string? solutionLabel = null,
        string? element = null, string? oldValue = null, string? newValue = null,
        string? changedBy = null, string? details = null, Guid? batchId = null);

    /// <summary>
    /// Records a batch of changes efficiently in a single operation.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="changeType">The broad category shared by all changes in this batch.</param>
    /// <param name="changes">A collection of tuples detailing the individual changes (SolutionLabel, Element, OldValue, NewValue).</param>
    /// <param name="changedBy">The username or identifier of the user who performed the actions.</param>
    /// <param name="details">A common description or reason for the entire batch of changes.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task LogBatchChangesAsync(Guid projectId, string changeType,
        IEnumerable<(string? SolutionLabel, string? Element, string? OldValue, string? NewValue)> changes,
        string? changedBy = null, string? details = null);

    /// <summary>
    /// Retrieves the change log history for a specific project with pagination support.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="page">The current page number to retrieve.</param>
    /// <param name="pageSize">The number of log entries to include per page.</param>
    /// <returns>A task containing a list of <see cref="ChangeLog"/> entities.</returns>
    Task<List<ChangeLog>> GetChangeLogAsync(Guid projectId, int page = 1, int pageSize = 50);

    /// <summary>
    /// Retrieves a list of changes filtered by a specific change type.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="changeType">The specific change type string to filter by.</param>
    /// <returns>A task containing a list of matching <see cref="ChangeLog"/> entries.</returns>
    Task<List<ChangeLog>> GetChangesByTypeAsync(Guid projectId, string changeType);

    /// <summary>
    /// Retrieves a list of changes associated with a specific sample label.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="solutionLabel">The solution label to filter by.</param>
    /// <returns>A task containing a list of matching <see cref="ChangeLog"/> entries.</returns>
    Task<List<ChangeLog>> GetChangesBySampleAsync(Guid projectId, string solutionLabel);
}