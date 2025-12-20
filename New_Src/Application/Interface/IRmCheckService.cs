using Application.DTOs;
using Shared.Wrapper;

namespace Application.Services;

/// <summary>
/// Defines the contract for services that validate project data against known Reference Materials (RMs).
/// </summary>
public interface IRmCheckService
{
    /// <summary>
    /// Executes a comprehensive validation check of all RM samples within a project against their certified values.
    /// </summary>
    /// <param name="request">A request object specifying the tolerance thresholds and filtering options.</param>
    /// <returns>A result containing a detailed summary of the check results.</returns>
    Task<Result<RmCheckSummaryDto>> CheckRmAsync(RmCheckRequest request);

    /// <summary>
    /// Scans the project to identify samples that match known Reference Material naming patterns or definitions.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="patterns">Optional list of regex patterns to override default detection logic.</param>
    /// <returns>A result containing a list of solution labels identified as RMs.</returns>
    Task<Result<List<string>>> GetRmSamplesAsync(Guid projectId, List<string>? patterns = null);

    /// <summary>
    /// Verifies that sample weights and volumes fall within expected operational limits.
    /// </summary>
    /// <param name="request">A request object defining the acceptable ranges for weight and volume.</param>
    /// <returns>A result containing a summary of the validation checks.</returns>
    Task<Result<WeightVolumeCheckSummaryDto>> CheckWeightVolumeAsync(WeightVolumeCheckRequest request);

    /// <summary>
    /// Retrieves a list of specific samples that failed the weight or volume validation checks.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>A result containing a list of details for the failing samples.</returns>
    Task<Result<List<WeightVolumeCheckResultDto>>> GetWeightVolumeIssuesAsync(Guid projectId);
}