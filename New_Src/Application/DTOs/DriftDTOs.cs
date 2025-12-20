using System.Text.Json.Serialization;

namespace Application.DTOs;

/// <summary>
/// Represents a request to perform drift correction analysis on a project's data.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to analyze.</param>
/// <param name="SelectedElements">An optional list of specific element symbols (e.g., "Au", "Ag") to correct. If null, all elements are processed.</param>
/// <param name="Method">The mathematical method used to calculate drift (e.g., Linear, Polynomial). Defaults to Linear.</param>
/// <param name="UseSegmentation">Indicates whether to split the data into segments bounded by standards (true) or treat it as a continuous stream (false).</param>
/// <param name="BasePattern">A regex pattern or string used to identify the base calibration standard in the dataset.</param>
/// <param name="ConePattern">A regex pattern or string used to identify the cone comparison standard in the dataset.</param>
/// <param name="ChangedBy">The username or identifier of the user initiating the correction.</param>
public record DriftCorrectionRequest(
    Guid ProjectId,
    List<string>? SelectedElements = null,
    DriftMethod Method = DriftMethod.Linear,
    bool UseSegmentation = true,
    string? BasePattern = null,
    string? ConePattern = null,
    string? ChangedBy = null
);

/// <summary>
/// Defines the available mathematical algorithms for drift correction.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DriftMethod
{
    /// <summary>
    /// No drift correction is applied.
    /// </summary>
    None = 0,
    
    /// <summary>
    /// Applies a linear interpolation drift correction.
    /// </summary>
    Linear = 1,
    
    /// <summary>
    /// Applies a stepwise correction based on discrete segments.
    /// </summary>
    Stepwise = 2,
    
    /// <summary>
    /// Applies a polynomial curve fitting for drift correction.
    /// </summary>
    Polynomial = 3
}

/// <summary>
/// Contains the comprehensive results of a drift correction operation.
/// </summary>
/// <param name="TotalSamples">The total number of sample rows processed.</param>
/// <param name="CorrectedSamples">The number of samples that were successfully corrected.</param>
/// <param name="SegmentsFound">The number of distinct drift segments identified in the data.</param>
/// <param name="Segments">A list of the identified drift segments, detailing their boundaries.</param>
/// <param name="ElementDrifts">A dictionary containing drift statistics for each element.</param>
/// <param name="CorrectedData">The list of sample data with drift corrections applied.</param>
public record DriftCorrectionResult(
    int TotalSamples,
    int CorrectedSamples,
    int SegmentsFound,
    List<DriftSegment> Segments,
    Dictionary<string, ElementDriftInfo> ElementDrifts,
    List<CorrectedSampleDto> CorrectedData
);

/// <summary>
/// Represents a specific segment of data samples bracketed by calibration standards.
/// </summary>
/// <param name="SegmentIndex">The sequential index of this segment within the dataset.</param>
/// <param name="StartIndex">The row index of the first sample in this segment.</param>
/// <param name="EndIndex">The row index of the last sample in this segment.</param>
/// <param name="StartStandard">The label of the standard defining the start of the segment.</param>
/// <param name="EndStandard">The label of the standard defining the end of the segment.</param>
/// <param name="SampleCount">The count of samples contained within this segment.</param>
public record DriftSegment(
    int SegmentIndex,
    int StartIndex,
    int EndIndex,
    string? StartStandard,
    string? EndStandard,
    int SampleCount
);

/// <summary>
/// Contains statistical information about the drift calculated for a single chemical element.
/// </summary>
/// <param name="Element">The chemical symbol of the element.</param>
/// <param name="InitialRatio">The intensity ratio observed at the start of the drift period.</param>
/// <param name="FinalRatio">The intensity ratio observed at the end of the drift period.</param>
/// <param name="DriftPercent">The calculated total drift expressed as a percentage.</param>
/// <param name="Slope">The slope of the drift correction line.</param>
/// <param name="Intercept">The y-intercept of the drift correction line.</param>
public record ElementDriftInfo(
    string Element,
    decimal InitialRatio,
    decimal FinalRatio,
    decimal DriftPercent,
    decimal Slope,
    decimal Intercept
);

/// <summary>
/// Represents a sample row with both its original measured values and the drift-corrected values.
/// </summary>
/// <param name="SolutionLabel">The unique label of the sample.</param>
/// <param name="GroupId">The identifier of the group this sample belongs to.</param>
/// <param name="OriginalIndex">The original row index of the sample.</param>
/// <param name="SegmentIndex">The index of the drift segment containing this sample.</param>
/// <param name="OriginalValues">The raw measured values before correction.</param>
/// <param name="CorrectedValues">The values after drift correction has been applied.</param>
/// <param name="CorrectionFactors">The specific multiplication factors applied to each element.</param>
public record CorrectedSampleDto(
    string SolutionLabel,
    int GroupId,
    int OriginalIndex,
    int SegmentIndex,
    Dictionary<string, decimal?> OriginalValues,
    Dictionary<string, decimal?> CorrectedValues,
    Dictionary<string, decimal> CorrectionFactors
);

/// <summary>
/// Represents a request to manually optimize or manipulate the drift slope for a specific element.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project.</param>
/// <param name="Element">The chemical symbol of the element to optimize.</param>
/// <param name="Action">The type of action to perform on the slope (e.g., RotateUp, ZeroSlope).</param>
/// <param name="TargetSlope">An optional specific slope value to apply if the action is SetCustom.</param>
public record SlopeOptimizationRequest(
    Guid ProjectId,
    string Element,
    SlopeAction Action,
    decimal? TargetSlope = null
);

/// <summary>
/// Defines the specific actions that can be performed to optimize a drift slope.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SlopeAction
{
    /// <summary>
    /// Forces the slope to zero, effectively removing the drift trend.
    /// </summary>
    ZeroSlope = 0,
    
    /// <summary>
    /// Increases the slope angle.
    /// </summary>
    RotateUp = 1,
    
    /// <summary>
    /// Decreases the slope angle.
    /// </summary>
    RotateDown = 2,
    
    /// <summary>
    /// Sets the slope to a specific custom value.
    /// </summary>
    SetCustom = 3
}

/// <summary>
/// Contains the results of a slope optimization operation for a specific element.
/// </summary>
/// <param name="Element">The chemical symbol of the element that was optimized.</param>
/// <param name="OriginalSlope">The slope value before the optimization action.</param>
/// <param name="NewSlope">The new slope value after optimization.</param>
/// <param name="OriginalIntercept">The intercept value before the optimization action.</param>
/// <param name="NewIntercept">The new intercept value after optimization.</param>
/// <param name="CorrectedData">The sample data re-calculated using the new slope parameters.</param>
public record SlopeOptimizationResult(
    string Element,
    decimal OriginalSlope,
    decimal NewSlope,
    decimal OriginalIntercept,
    decimal NewIntercept,
    List<CorrectedSampleDto> CorrectedData
);