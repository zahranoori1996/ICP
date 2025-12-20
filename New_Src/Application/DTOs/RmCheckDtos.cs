namespace Application.DTOs;

/// <summary>
/// Represents a request to perform a Reference Material (RM) check on project data.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to check.</param>
/// <param name="MinDiffPercent">The minimum acceptable percentage difference (tolerance). Defaults to -12%.</param>
/// <param name="MaxDiffPercent">The maximum acceptable percentage difference (tolerance). Defaults to 12%.</param>
/// <param name="RmPatterns">An optional list of patterns to identify RM samples.</param>
/// <param name="AnalysisMethod">An optional specific analysis method to filter RMs by.</param>
public record RmCheckRequest(
    Guid ProjectId,
    decimal MinDiffPercent = -12m,
    decimal MaxDiffPercent = 12m,
    List<string>? RmPatterns = null,
    string? AnalysisMethod = null
);

/// <summary>
/// Represents the result of an RM check for a single sample.
/// </summary>
/// <param name="SolutionLabel">The sample label identifying the specific RM instance.</param>
/// <param name="MatchedRmId">The identifier of the Reference Material definition it was matched against.</param>
/// <param name="AnalysisMethod">The analysis method associated with the matched RM.</param>
/// <param name="Status">The aggregated status of the check (e.g., Pass if all elements pass).</param>
/// <param name="ElementChecks">A list of individual checks performed for each element.</param>
/// <param name="PassCount">The number of elements that passed the check.</param>
/// <param name="FailCount">The number of elements that failed the check.</param>
/// <param name="TotalCount">The total number of elements evaluated.</param>
public record RmCheckResultDto(
    string SolutionLabel,
    string MatchedRmId,
    string AnalysisMethod,
    RmCheckStatus Status,
    List<RmElementCheckDto> ElementChecks,
    int PassCount,
    int FailCount,
    int TotalCount
);

/// <summary>
/// Represents the result of checking a specific element value against its reference value.
/// </summary>
/// <param name="Element">The chemical symbol of the element.</param>
/// <param name="SampleValue">The value measured in the sample.</param>
/// <param name="ReferenceValue">The expected reference value defined for the RM.</param>
/// <param name="DiffPercent">The calculated percentage difference between measured and reference values.</param>
/// <param name="Status">The status of this specific element check.</param>
/// <param name="Message">An optional message providing details (e.g., specific error reason).</param>
public record RmElementCheckDto(
    string Element,
    decimal? SampleValue,
    decimal? ReferenceValue,
    decimal? DiffPercent,
    RmCheckStatus Status,
    string? Message
);

/// <summary>
/// Enumerates the possible outcomes of an RM check.
/// </summary>
public enum RmCheckStatus
{
    /// <summary>
    /// The check passed successfully within tolerance.
    /// </summary>
    Pass,
    
    /// <summary>
    /// The check failed; the difference exceeded tolerance.
    /// </summary>
    Fail,
    
    /// <summary>
    /// The check produced a warning but is not a critical failure.
    /// </summary>
    Warning,
    
    /// <summary>
    /// No reference value was found for comparison.
    /// </summary>
    NoReference,
    
    /// <summary>
    /// The check was skipped.
    /// </summary>
    Skipped
}

/// <summary>
/// Summarizes the results of RM checks across an entire project.
/// </summary>
/// <param name="ProjectId">The identifier of the project.</param>
/// <param name="TotalRmSamples">The total number of RM samples identified and checked.</param>
/// <param name="PassedSamples">The count of RM samples where all checks passed.</param>
/// <param name="FailedSamples">The count of RM samples having at least one failure.</param>
/// <param name="WarningSamples">The count of RM samples having warnings but no failures.</param>
/// <param name="Results">The detailed list of results for every checked RM sample.</param>
/// <param name="ElementSummary">A dictionary summarizing performance by element across all samples.</param>
public record RmCheckSummaryDto(
    Guid ProjectId,
    int TotalRmSamples,
    int PassedSamples,
    int FailedSamples,
    int WarningSamples,
    List<RmCheckResultDto> Results,
    Dictionary<string, RmElementSummaryDto> ElementSummary
);

/// <summary>
/// Aggregates check statistics for a single element across multiple RM samples.
/// </summary>
/// <param name="Element">The element identifier.</param>
/// <param name="TotalChecks">The total number of times this element was checked.</param>
/// <param name="PassCount">The number of times this element passed.</param>
/// <param name="FailCount">The number of times this element failed.</param>
/// <param name="AverageDiff">The average percentage difference observed for this element.</param>
/// <param name="MaxDiff">The maximum percentage difference observed.</param>
/// <param name="MinDiff">The minimum percentage difference observed.</param>
public record RmElementSummaryDto(
    string Element,
    int TotalChecks,
    int PassCount,
    int FailCount,
    decimal? AverageDiff,
    decimal? MaxDiff,
    decimal? MinDiff
);

/// <summary>
/// Represents a request to validate sample weights and volumes against expected constraints.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to check.</param>
/// <param name="MinWeight">The absolute minimum acceptable weight.</param>
/// <param name="MaxWeight">The absolute maximum acceptable weight.</param>
/// <param name="MinVolume">The absolute minimum acceptable volume.</param>
/// <param name="MaxVolume">The absolute maximum acceptable volume.</param>
/// <param name="ExpectedWeight">The target expected weight. Defaults to 0.25.</param>
/// <param name="ExpectedVolume">The target expected volume. Defaults to 50.</param>
/// <param name="TolerancePercent">The allowed percentage deviation from expected values. Defaults to 10%.</param>
public record WeightVolumeCheckRequest(
    Guid ProjectId,
    decimal? MinWeight = null,
    decimal? MaxWeight = null,
    decimal? MinVolume = null,
    decimal? MaxVolume = null,
    decimal? ExpectedWeight = 0.25m,
    decimal? ExpectedVolume = 50m,
    decimal TolerancePercent = 10m
);

/// <summary>
/// Represents the result of validating a specific sample's weight and volume.
/// </summary>
/// <param name="SolutionLabel">The sample's solution label.</param>
/// <param name="Weight">The measured weight.</param>
/// <param name="Volume">The measured volume.</param>
/// <param name="WeightStatus">The validation status of the weight.</param>
/// <param name="VolumeStatus">The validation status of the volume.</param>
/// <param name="WeightMessage">A message describing the weight status.</param>
/// <param name="VolumeMessage">A message describing the volume status.</param>
public record WeightVolumeCheckResultDto(
    string SolutionLabel,
    decimal? Weight,
    decimal? Volume,
    WeightVolumeStatus WeightStatus,
    WeightVolumeStatus VolumeStatus,
    string? WeightMessage,
    string? VolumeMessage
);

/// <summary>
/// Enumerates the possible outcomes of a weight or volume check.
/// </summary>
public enum WeightVolumeStatus
{
    /// <summary>
    /// The value is within acceptable limits.
    /// </summary>
    Ok,
    
    /// <summary>
    /// The value is below the minimum limit.
    /// </summary>
    TooLow,
    
    /// <summary>
    /// The value is above the maximum limit.
    /// </summary>
    TooHigh,
    
    /// <summary>
    /// The value is missing.
    /// </summary>
    Missing,
    
    /// <summary>
    /// The check was not performed.
    /// </summary>
    Skipped
}

/// <summary>
/// Summarizes weight and volume validation results for the entire project.
/// </summary>
/// <param name="ProjectId">The project identifier.</param>
/// <param name="TotalSamples">Total number of samples checked.</param>
/// <param name="WeightOkCount">Count of samples with valid weights.</param>
/// <param name="WeightErrorCount">Count of samples with invalid weights.</param>
/// <param name="VolumeOkCount">Count of samples with valid volumes.</param>
/// <param name="VolumeErrorCount">Count of samples with invalid volumes.</param>
/// <param name="Results">Detailed list of results for each sample.</param>
public record WeightVolumeCheckSummaryDto(
    Guid ProjectId,
    int TotalSamples,
    int WeightOkCount,
    int WeightErrorCount,
    int VolumeOkCount,
    int VolumeErrorCount,
    List<WeightVolumeCheckResultDto> Results
);