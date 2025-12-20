using Domain.Entities;
using Shared.Wrapper;

namespace Application.Services;

/// <summary>
/// Represents a hierarchical node in the project version history tree.
/// </summary>
/// <param name="StateId">The unique identifier of the version state.</param>
/// <param name="ParentStateId">The identifier of the parent version state, if any.</param>
/// <param name="VersionNumber">The sequential version number.</param>
/// <param name="ProcessingType">The type of processing action that generated this version.</param>
/// <param name="Description">A user-friendly description of the version.</param>
/// <param name="Timestamp">The date and time when the version was created.</param>
/// <param name="IsActive">True if this is the currently active version of the project.</param>
/// <param name="Children">A list of child version nodes branching from this version.</param>
public record VersionNodeDto(
    int StateId,
    int? ParentStateId,
    int VersionNumber,
    string ProcessingType,
    string? Description,
    DateTime Timestamp,
    bool IsActive,
    List<VersionNodeDto> Children
);

/// <summary>
/// Represents a request to create a new version checkpoint for a project.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project.</param>
/// <param name="ParentStateId">The identifier of the state from which this version is derived.</param>
/// <param name="ProcessingType">The type of processing action triggering this version creation.</param>
/// <param name="Data">The serialized state data associated with this version.</param>
/// <param name="Description">An optional description for the version.</param>
public record CreateVersionDto(
    Guid ProjectId,
    int? ParentStateId,
    string ProcessingType,
    string Data,
    string? Description
);

/// <summary>
/// Defines the contract for services managing project versioning, history, and branching capabilities.
/// </summary>
public interface IVersionService
{
    /// <summary>
    /// Creates a new version state entry in the project's history.
    /// </summary>
    /// <param name="dto">An object containing the details for the new version.</param>
    /// <param name="ct">A token to observe while waiting for the task to complete.</param>
    /// <returns>A result containing the created <see cref="ProjectState"/> entity.</returns>
    Task<Result<ProjectState>> CreateVersionAsync(CreateVersionDto dto, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the complete hierarchical tree of versions for a specific project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="ct">A token to observe while waiting for the task to complete.</param>
    /// <returns>A result containing the root nodes of the version tree.</returns>
    Task<Result<List<VersionNodeDto>>> GetVersionTreeAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a flat list of all version states associated with a project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="ct">A token to observe while waiting for the task to complete.</param>
    /// <returns>A result containing a list of <see cref="ProjectState"/> entities.</returns>
    Task<Result<List<ProjectState>>> GetAllVersionsAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the currently active version state for a project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="ct">A token to observe while waiting for the task to complete.</param>
    /// <returns>A result containing the active <see cref="ProjectState"/>, or null if none is active.</returns>
    Task<Result<ProjectState?>> GetActiveVersionAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>
    /// Sets a specific version as the active current state for the project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="stateId">The unique identifier of the state to activate.</param>
    /// <param name="ct">A token to observe while waiting for the task to complete.</param>
    /// <returns>A result indicating true if the switch was successful.</returns>
    Task<Result<bool>> SwitchToVersionAsync(Guid projectId, int stateId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the details of a specific version state by its ID.
    /// </summary>
    /// <param name="stateId">The unique identifier of the version state.</param>
    /// <param name="ct">A token to observe while waiting for the task to complete.</param>
    /// <returns>A result containing the <see cref="ProjectState"/> if found.</returns>
    Task<Result<ProjectState?>> GetVersionAsync(int stateId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the sequence of states leading from the root to the specified version (lineage).
    /// </summary>
    /// <param name="stateId">The unique identifier of the target version state.</param>
    /// <param name="ct">A token to observe while waiting for the task to complete.</param>
    /// <returns>A result containing a list of <see cref="ProjectState"/> entities representing the path.</returns>
    Task<Result<List<ProjectState>>> GetVersionPathAsync(int stateId, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes a specific version state and optionally its entire descendant lineage.
    /// </summary>
    /// <param name="stateId">The unique identifier of the version state to delete.</param>
    /// <param name="deleteChildren">If true, recursively deletes all child versions derived from this state.</param>
    /// <param name="ct">A token to observe while waiting for the task to complete.</param>
    /// <returns>A result indicating true if the deletion was successful.</returns>
    Task<Result<bool>> DeleteVersionAsync(int stateId, bool deleteChildren = false, CancellationToken ct = default);

    /// <summary>
    /// Creates a new divergent branch (fork) from an existing version state with modified data.
    /// </summary>
    /// <param name="parentStateId">The identifier of the parent state to fork from.</param>
    /// <param name="processingType">The type of processing action for the new fork.</param>
    /// <param name="data">The new state data to initialize the fork with.</param>
    /// <param name="description">An optional description for the new fork.</param>
    /// <param name="ct">A token to observe while waiting for the task to complete.</param>
    /// <returns>A result containing the newly created branched <see cref="ProjectState"/>.</returns>
    Task<Result<ProjectState>> ForkVersionAsync(int parentStateId, string processingType, string data, string? description = null, CancellationToken ct = default);
}
