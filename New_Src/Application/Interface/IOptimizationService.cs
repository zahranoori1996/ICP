using Application.DTOs;
using Shared.Wrapper;

namespace Application.Services;

/// <summary>
/// Defines the contract for services that utilize advanced algorithms to optimize correction parameters.
/// </summary>
public interface IOptimizationService
{
    /// <summary>
    /// Executes an evolutionary algorithm (e.g., Differential Evolution) to find the optimal Blank and Scale values.
    /// </summary>
    /// <param name="request">A request object containing optimization constraints and algorithm parameters.</param>
    /// <returns>A result containing the calculated optimal parameters for each element.</returns>
    Task<Result<BlankScaleOptimizationResult>> OptimizeBlankScaleAsync(BlankScaleOptimizationRequest request);

    /// <summary>
    /// Applies specific Blank and Scale values manually provided by the user to the project data.
    /// </summary>
    /// <param name="request">A request object specifying the element and the values to apply.</param>
    /// <returns>A result showing the outcome of applying the manual parameters.</returns>
    Task<Result<ManualBlankScaleResult>> ApplyManualBlankScaleAsync(ManualBlankScaleRequest request);

    /// <summary>
    /// Calculates the hypothetical outcome of applying Blank and Scale values without saving changes.
    /// </summary>
    /// <param name="request">A request object specifying the element and the values to preview.</param>
    /// <returns>A result showing the projected outcome of the parameters.</returns>
    Task<Result<ManualBlankScaleResult>> PreviewBlankScaleAsync(ManualBlankScaleRequest request);

    /// <summary>
    /// Retrieves the current statistical performance (pass/fail rates) of the project based on existing settings.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="minDiff">The lower bound of the acceptable difference percentage. Defaults to -10.</param>
    /// <param name="maxDiff">The upper bound of the acceptable difference percentage. Defaults to 10.</param>
    /// <returns>A result containing the current performance metrics.</returns>
    Task<Result<BlankScaleOptimizationResult>> GetCurrentStatisticsAsync(Guid projectId, decimal minDiff = -10m, decimal maxDiff = 10m);

    /// <summary>
    /// Retrieves a subset of project sample data formatted for debugging and analysis purposes.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>An object containing the debug dataset.</returns>
    Task<object> GetDebugSamplesAsync(Guid projectId);
}