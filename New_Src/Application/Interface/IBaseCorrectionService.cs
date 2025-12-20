using Shared.Wrapper;

namespace Application.Services;

public interface IBaseCorrectionService
{
    /// <summary>
    /// Reverts the most recently applied correction or bulk modification action for the specified project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>A result indicating true if the undo operation was successful.</returns>
    Task<Result<bool>> UndoLastCorrectionAsync(Guid projectId);
}