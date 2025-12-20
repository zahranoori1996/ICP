namespace Application.DTOs;

/// <summary>
/// Represents a structured request to generate a project report.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to retrieve data from.</param>
/// <param name="ReportType">The specific category of report to generate (e.g., Full, Summary).</param>
/// <param name="Format">The desired output file format (e.g., Excel, CSV).</param>
/// <param name="Options">An optional configuration object specifying detailed report settings.</param>
public record ReportRequest(
    Guid ProjectId,
    ReportType ReportType,
    ReportFormat Format = ReportFormat.Excel,
    ReportOptions? Options = null
);

/// <summary>
/// Defines detailed configuration options for report generation customization.
/// </summary>
/// <param name="IncludeSummary">If true, a summary section is included in the report.</param>
/// <param name="IncludeRawData">If true, the original raw data is included.</param>
/// <param name="IncludeStatistics">If true, detailed statistical analysis is included.</param>
/// <param name="IncludeRmCheck">If true, Reference Material check results are included.</param>
/// <param name="IncludeDuplicates">If true, analysis of duplicate samples is included.</param>
/// <param name="UseOxide">If true, chemical values are converted to their oxide forms.</param>
/// <param name="DecimalPlaces">The number of decimal places to use for numeric values.</param>
/// <param name="SelectedElements">A specific list of elements to include; if null, all are included.</param>
/// <param name="Title">A custom title for the generated report.</param>
/// <param name="Author">The name of the report's author.</param>
public record ReportOptions(
    bool IncludeSummary = true,
    bool IncludeRawData = true,
    bool IncludeStatistics = true,
    bool IncludeRmCheck = true,
    bool IncludeDuplicates = true,
    bool UseOxide = false,
    int DecimalPlaces = 2,
    List<string>? SelectedElements = null,
    string? Title = null,
    string? Author = null
);

/// <summary>
/// Enumerates the various types of reports available for generation.
/// </summary>
public enum ReportType
{
    /// <summary>
    /// A comprehensive report containing all designated sections.
    /// </summary>
    Full,
    
    /// <summary>
    /// A concise report focusing on summary statistics.
    /// </summary>
    Summary,
    
    /// <summary>
    /// A report specifically detailing Reference Material performance.
    /// </summary>
    RmCheck,
    
    /// <summary>
    /// A report analyzing duplicate sample results.
    /// </summary>
    Duplicates,
    
    /// <summary>
    /// A report formatted as a pivot table for flexible data analysis.
    /// </summary>
    PivotTable,
    
    /// <summary>
    /// A report comparing sample results against Certified Reference Materials.
    /// </summary>
    CrmComparison
}

/// <summary>
/// Enumerates the supported output formats for generated reports.
/// </summary>
public enum ReportFormat
{
    /// <summary>
    /// Microsoft Excel format (.xlsx).
    /// </summary>
    Excel,
    
    /// <summary>
    /// Comma-Separated Values format (.csv).
    /// </summary>
    Csv,
    
    /// <summary>
    /// JavaScript Object Notation format (.json).
    /// </summary>
    Json,
    
    /// <summary>
    /// Hypertext Markup Language format (.html).
    /// </summary>
    Html
}

/// <summary>
/// Represents the completed output of a report generation process.
/// </summary>
/// <param name="FileName">The recommended filename for the downloaded report.</param>
/// <param name="ContentType">The MIME content type appropriate for the file format.</param>
/// <param name="Data">The raw binary content of the generated report file.</param>
/// <param name="GeneratedAt">The exact timestamp when the generation was completed.</param>
/// <param name="Metadata">Metadata describing the generated report's content and performance.</param>
public record ReportResultDto(
    string FileName,
    string ContentType,
    byte[] Data,
    DateTime GeneratedAt,
    ReportMetadataDto Metadata
);

/// <summary>
/// Contains metadata describing the properties and metrics of a generated report.
/// </summary>
/// <param name="ProjectId">The identifier of the project source.</param>
/// <param name="ProjectName">The display name of the project.</param>
/// <param name="ReportType">The type of report that was generated.</param>
/// <param name="Format">The file format of the report.</param>
/// <param name="TotalRows">The total number of data rows included in the report.</param>
/// <param name="TotalColumns">The total number of columns included in the report.</param>
/// <param name="GenerationTime">The time duration occupied by the generation process.</param>
public record ReportMetadataDto(
    Guid ProjectId,
    string ProjectName,
    ReportType ReportType,
    ReportFormat Format,
    int TotalRows,
    int TotalColumns,
    TimeSpan GenerationTime
);

/// <summary>
/// Represents a simple request to export project data without complex formatting.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to export.</param>
/// <param name="Format">The desired export format. Defaults to Excel.</param>
/// <param name="UseOxide">Indicates whether to convert values to oxides.</param>
/// <param name="DecimalPlaces">The numeric precision to use.</param>
/// <param name="SelectedElements">Optional list of elements to include.</param>
/// <param name="SelectedSolutionLabels">Optional list of specific samples to include.</param>
public record ExportRequest(
    Guid ProjectId,
    ReportFormat Format = ReportFormat.Excel,
    bool UseOxide = false,
    int DecimalPlaces = 2,
    List<string>? SelectedElements = null,
    List<string>? SelectedSolutionLabels = null
);

/// <summary>
/// Represents the valid calibration range for a specific element.
/// </summary>
/// <param name="Element">The element identifier.</param>
/// <param name="Min">The minimum valid calibration value.</param>
/// <param name="Max">The maximum valid calibration value.</param>
/// <param name="DisplayRange">A string representation of the range for display purposes.</param>
public record CalibrationRange(
    string Element,
    decimal Min,
    decimal Max,
    string DisplayRange
);

/// <summary>
/// Represents a request to determine the optimal wavelength for analysis.
/// </summary>
/// <param name="ProjectId">The project identifier.</param>
/// <param name="SelectedSolutionLabels">An optional list of samples to consider.</param>
/// <param name="UseConcentration">If true, uses Solution Concentration; if false, uses Corrected Concentration.</param>
public record BestWavelengthRequest(
    Guid ProjectId,
    List<string>? SelectedSolutionLabels = null,
    bool UseConcentration = true
);

/// <summary>
/// Contains the comprehensive results of the best wavelength selection algorithm.
/// </summary>
/// <param name="CalibrationRanges">A dictionary mapping elements to their calibration ranges.</param>
/// <param name="BestWavelengthsPerRow">A nested dictionary mapping row index and element to the selected wavelength.</param>
/// <param name="BaseElements">A dictionary mapping base elements to available wavelength options.</param>
/// <param name="SelectedColumns">The final list of column headers selected for the view.</param>
public record BestWavelengthResult(
    Dictionary<string, CalibrationRange> CalibrationRanges,
    Dictionary<int, Dictionary<string, string>> BestWavelengthsPerRow,
    Dictionary<string, List<string>> BaseElements,
    List<string> SelectedColumns
);

/// <summary>
/// Details the wavelength selection decision for a single sample row and element.
/// </summary>
/// <param name="SolutionLabel">The sample label.</param>
/// <param name="BaseElement">The base chemical element (e.g., "Fe").</param>
/// <param name="SelectedWavelength">The specific wavelength variant selected (e.g., "Fe 238.204").</param>
/// <param name="Concentration">The concentration value associated with the selected wavelength.</param>
/// <param name="IsInCalibrationRange">True if the value falls within the calibration range.</param>
/// <param name="DistanceFromRange">The absolute difference from the nearest range boundary, if out of range.</param>
public record WavelengthSelectionInfo(
    string SolutionLabel,
    string BaseElement,
    string SelectedWavelength,
    decimal? Concentration,
    bool IsInCalibrationRange,
    decimal? DistanceFromRange
);