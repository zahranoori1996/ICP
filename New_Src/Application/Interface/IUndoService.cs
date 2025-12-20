using Shared.Wrapper;

namespace Application.Services;

/// <summary>
/// Provides generic undo operations for project-level snapshots.
/// </summary>
public interface IUndoService
{
    /// <summary>
    /// Restores the project data to the previous snapshot state (LIFO).
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>The result of the undo operation.</returns>
    Task<Result<string>> UndoLastAsync(Guid projectId);
}
