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

    /// <summary>
    /// Retrieves CRM method options detected in a project to mirror Python CRM selection behavior.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>A result containing CRM method options per CRM id.</returns>
    Task<Result<CrmOptionsResult>> GetCrmOptionsAsync(Guid projectId);

    /// <summary>
    /// Retrieves per-row CRM selection options for a project (Python-compatible).
    /// </summary>
    Task<Result<CrmSelectionOptionsResult>> GetCrmSelectionOptionsAsync(Guid projectId);

    /// <summary>
    /// Returns a small pivot preview for debugging (row order + selected element values).
    /// </summary>
    Task<object> GetPivotPreviewAsync(Guid projectId, int take, IEnumerable<string>? elements);

    /// <summary>
    /// Returns CRM reference values for a CRM id + method, limited to requested elements.
    /// </summary>
    Task<object> GetCrmPreviewAsync(string crmId, string? method, IEnumerable<string>? elements);

    /// <summary>
    /// Returns best-blank debug info (candidates + selected) for requested elements.
    /// </summary>
    Task<object> GetBlankPreviewAsync(Guid projectId, IEnumerable<string>? elements, decimal rangeLow, decimal rangeMid, decimal rangeHigh1, decimal rangeHigh2, decimal rangeHigh3, decimal rangeHigh4);

    /// <summary>
    /// Saves per-row CRM selections for a project.
    /// </summary>
    Task<Result<bool>> SaveCrmSelectionsAsync(CrmSelectionSaveRequest request, string? selectedBy);
}
