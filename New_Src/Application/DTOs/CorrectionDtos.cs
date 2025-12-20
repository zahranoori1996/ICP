using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Application.DTOs;

/// <summary>
/// Represents a request to identify samples where the measured weight falls outside the specified acceptable range.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to inspect.</param>
/// <param name="WeightMin">The minimum acceptable weight value (inclusive). Defaults to 0.09.</param>
/// <param name="WeightMax">The maximum acceptable weight value (inclusive). Defaults to 0.11.</param>
public record FindBadWeightsRequest(
    Guid ProjectId,
    decimal WeightMin = 0.09m,
    decimal WeightMax = 0.11m
);

/// <summary>
/// Represents a request to identify samples where the volume deviates from the expected standard.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to inspect.</param>
/// <param name="ExpectedVolume">The expected volume value for valid samples. Defaults to 10.</param>
public record FindBadVolumesRequest(
    Guid ProjectId,
    decimal ExpectedVolume = 10m
);

/// <summary>
/// Represents a request to find rows that appear to be empty or contain outlier data based on element analysis.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to inspect.</param>
/// <param name="ThresholdPercent">The percentage of the average value below which a reading is considered low. Defaults to 70%.</param>
/// <param name="ElementsToCheck">An optional list of specific elements to analyze. If null, all available elements are checked.</param>
/// <param name="RequireAllElements">If true, a row is flagged only if ALL checked elements are below the threshold. If false, ANY element below threshold triggers a flag.</param>
public record FindEmptyRowsRequest(
    Guid ProjectId,
    decimal ThresholdPercent = 70m,
    List<string>? ElementsToCheck = null,
    bool RequireAllElements = true
);

/// <summary>
/// Represents a request to apply a new weight value to a specific set of samples.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project containing the samples.</param>
/// <param name="SolutionLabels">The list of solution labels identifying the samples to update.</param>
/// <param name="NewWeight">The new weight value to apply to the specified samples.</param>
/// <param name="ChangedBy">The username or identifier of the user performing this correction.</param>
public record WeightCorrectionRequest(
    Guid ProjectId,
    List<string> SolutionLabels,
    decimal NewWeight,
    string? ChangedBy = null
);

/// <summary>
/// Represents a request to apply a new volume value to a specific set of samples.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project containing the samples.</param>
/// <param name="SolutionLabels">The list of solution labels identifying the samples to update.</param>
/// <param name="NewVolume">The new volume value to apply to the specified samples.</param>
/// <param name="ChangedBy">The username or identifier of the user performing this correction.</param>
public record VolumeCorrectionRequest(
    Guid ProjectId,
    List<string> SolutionLabels,
    decimal NewVolume,
    string? ChangedBy = null
);

/// <summary>
/// Represents a request to update the dilution factor (DF) for a specific set of samples.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project containing the samples.</param>
/// <param name="SolutionLabels">The list of solution labels identifying the samples to update.</param>
/// <param name="NewDf">The new dilution factor to apply.</param>
/// <param name="ChangedBy">The username or identifier of the user performing this correction.</param>
public record DfCorrectionRequest(
    Guid ProjectId,
    List<string> SolutionLabels,
    decimal NewDf,
    string? ChangedBy = null
);

/// <summary>
/// Represents a request to permanently remove specific sample rows from the project.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project containing the rows.</param>
/// <param name="SolutionLabels">The list of solution labels identifying the rows to delete.</param>
/// <param name="ChangedBy">The username or identifier of the user performing the deletion.</param>
public record DeleteRowsRequest(
    Guid ProjectId,
    List<string> SolutionLabels,
    string? ChangedBy = null
);

/// <summary>
/// Represents a request to apply optimization settings, such as blank subtraction and scaling, to project data.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to optimize.</param>
/// <param name="ElementSettings">A dictionary mapping element names to their specific blank and scale settings.</param>
/// <param name="ChangedBy">The username or identifier of the user applying the settings.</param>
public record ApplyOptimizationRequest(
    Guid ProjectId,
    Dictionary<string, ElementSettings> ElementSettings,
    string? ChangedBy = null
);

/// <summary>
/// Defines the blank correction and scaling factor for a specific element.
/// </summary>
/// <param name="Blank">The value to be subtracted from the raw reading as a blank correction.</param>
/// <param name="Scale">The factor by which the result should be scaled.</param>
public record ElementSettings(
    decimal Blank,
    decimal Scale
);

/// <summary>
/// Contains information about the current dilution factor of a specific sample row.
/// </summary>
/// <param name="RowNumber">The sequential row number of the sample.</param>
/// <param name="SolutionLabel">The label identifying the sample.</param>
/// <param name="CurrentDf">The currently applied dilution factor.</param>
/// <param name="SampleType">The classification of the sample (e.g., standard, unknown).</param>
public record DfSampleDto(
    int RowNumber,
    string SolutionLabel,
    decimal CurrentDf,
    string? SampleType
);

/// <summary>
/// Provides details about a sample that has been flagged for having an incorrect weight or volume.
/// </summary>
/// <param name="SolutionLabel">The label identifying the flagged sample.</param>
/// <param name="ActualValue">The value (weight or volume) that was actually recorded.</param>
/// <param name="CorrCon">The corrected concentration calculated using the actual value.</param>
/// <param name="ExpectedValue">The value that was expected for this sample.</param>
/// <param name="Deviation">The numerical difference between the actual and expected values.</param>
public record BadSampleDto(
    string SolutionLabel,
    decimal ActualValue,
    decimal CorrCon,
    decimal ExpectedValue,
    decimal Deviation
);

/// <summary>
/// Represents a row that has been flagged as potentially empty or an outlier based on statistical analysis.
/// </summary>
/// <param name="SolutionLabel">The label identifying the sample row.</param>
/// <param name="ElementValues">A dictionary of raw element values measured for this row.</param>
/// <param name="ElementAverages">A dictionary of average values for the corresponding elements across the dataset.</param>
/// <param name="PercentOfAverage">A dictionary showing each element's value as a percentage of the average.</param>
/// <param name="ElementsBelowThreshold">The count of assessed elements that fell below the specified threshold.</param>
/// <param name="TotalElementsChecked">The total number of elements that were included in the check.</param>
/// <param name="OverallScore">A calculated score indicating the severity of the outlier status.</param>
public record EmptyRowDto(
    string SolutionLabel,
    Dictionary<string, decimal?> ElementValues,
    Dictionary<string, decimal> ElementAverages,
    Dictionary<string, decimal> PercentOfAverage,
    int ElementsBelowThreshold,
    int TotalElementsChecked,
    decimal OverallScore
)
{
    /// <summary>
    /// Gets a sanitized version of the solution label safe for use as an HTML element identifier.
    /// </summary>
    [JsonIgnore]
    public string SafeId => Regex.Replace(SolutionLabel ?? Guid.NewGuid().ToString(), @"[^a-zA-Z0-9-_]", "_");
}

/// <summary>
/// Summarizes the result of a bulk correction operation.
/// </summary>
/// <param name="TotalRows">The total number of rows considered for the operation.</param>
/// <param name="CorrectedRows">The number of rows that were successfully modified.</param>
/// <param name="CorrectedSamples">A detailed list of the specific samples that were corrected.</param>
public record CorrectionResultDto(
    int TotalRows,
    int CorrectedRows,
    List<CorrectedSampleInfo> CorrectedSamples
);

/// <summary>
/// Details the changes applied to a single sample during a correction operation.
/// </summary>
/// <param name="SolutionLabel">The label of the corrected sample.</param>
/// <param name="OldValue">The primary value (e.g., weight, DF) before the correction.</param>
/// <param name="NewValue">The primary value after the correction.</param>
/// <param name="OldCorrCon">The corrected concentration value before the change.</param>
/// <param name="NewCorrCon">The corrected concentration value resulting from the change.</param>
public record CorrectedSampleInfo(
    string SolutionLabel,
    decimal OldValue,
    decimal NewValue,
    decimal OldCorrCon,
    decimal NewCorrCon
);