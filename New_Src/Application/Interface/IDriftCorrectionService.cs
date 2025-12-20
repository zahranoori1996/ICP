using Application.DTOs;
using Shared.Wrapper;

namespace Application.Services;

/// <summary>
/// Defines the contract for services that calculate and apply instrument drift corrections.
/// </summary>
public interface IDriftCorrectionService
{
    /// <summary>
    /// Performs a preliminary analysis of drift in the project data without persisting corrections.
    /// </summary>
    /// <param name="request">A request object specifying the drift calculation method and parameters.</param>
    /// <returns>A result containing the calculated drift statistics and identified segments.</returns>
    Task<Result<DriftCorrectionResult>> AnalyzeDriftAsync(DriftCorrectionRequest request);

    /// <summary>
    /// Calculates drift correction factors and applies them to the project data.
    /// </summary>
    /// <param name="request">A request object specifying the drift correction method and parameters.</param>
    /// <returns>A result containing the correction details and the updated dataset.</returns>
    Task<Result<DriftCorrectionResult>> ApplyDriftCorrectionAsync(DriftCorrectionRequest request);

    /// <summary>
    /// Identifies contiguous segments of samples within the run bounded by calibration standards.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="basePattern">A regex pattern or string to identify base standards.</param>
    /// <param name="conePattern">A regex pattern or string to identify cone standards.</param>
    /// <returns>A result containing a list of <see cref="DriftSegment"/> objects.</returns>
    Task<Result<List<DriftSegment>>> DetectSegmentsAsync(Guid projectId, string? basePattern = null, string? conePattern = null);

    /// <summary>
    /// Computes the intensity ratios for specified elements across the sequence of standards in the run.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="elements">An optional list of element symbols to analyze.</param>
    /// <returns>A result containing a dictionary of drift ratios keyed by element symbol.</returns>
    Task<Result<Dictionary<string, List<decimal>>>> CalculateDriftRatiosAsync(Guid projectId, List<string>? elements = null);

    /// <summary>
    /// Optimizes or manually adjusts the drift slope for a specific element.
    /// </summary>
    /// <param name="request">A request object specifying the optimization action and target.</param>
    /// <returns>A result containing the outcome of the slope optimization.</returns>
    Task<Result<SlopeOptimizationResult>> OptimizeSlopeAsync(SlopeOptimizationRequest request);

    /// <summary>
    /// Resets the drift slope for a specific element to zero, effectively disabling drift correction for that element.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="element">The chemical symbol of the element to reset.</param>
    /// <returns>A result containing the zeroed slope details.</returns>
    Task<Result<SlopeOptimizationResult>> ZeroSlopeAsync(Guid projectId, string element);
}