namespace Application.DTOs;

/// <summary>
/// Enumerates the file formats supported for data import operations.
/// </summary>
public enum FileFormat
{
    /// <summary>
    /// The file format could not be determined.
    /// </summary>
    Unknown,
    
    /// <summary>
    /// A standard CSV file structured in a tabular format.
    /// </summary>
    TabularCsv,
    
    /// <summary>
    /// An Excel file structured in a tabular format.
    /// </summary>
    TabularExcel,
    
    /// <summary>
    /// A CSV file where data is organized primarily by sample ID.
    /// </summary>
    SampleIdBasedCsv,
    
    /// <summary>
    /// An Excel file where data is organized primarily by sample ID.
    /// </summary>
    SampleIdBasedExcel,
    
    /// <summary>
    /// A data file exported from an ICP MassHunter system.
    /// </summary>
    IcpMassHunter,
    
    /// <summary>
    /// A data file exported from a PerkinElmer instrument system.
    /// </summary>
    PerkinElmer
}

/// <summary>
/// Represents the outcome of attempting to detect the format of an uploaded file.
/// </summary>
/// <param name="Format">The file format that was identified.</param>
/// <param name="DetectedDelimiter">The delimiter distinguishing columns, if applicable (e.g., comma, tab).</param>
/// <param name="HeaderRowIndex">The zero-based index of the row identified as containing column headers.</param>
/// <param name="DetectedColumns">A list of column names extracted from the header row.</param>
/// <param name="Message">Additional information, warnings, or error messages related to the detection process.</param>
public record FileFormatDetectionResult(
    FileFormat Format,
    string? DetectedDelimiter,
    int? HeaderRowIndex,
    List<string> DetectedColumns,
    string? Message
);

/// <summary>
/// Represents a complex request to import data into the system, specifying various parsing options.
/// </summary>
/// <param name="ProjectName">The proposed name for the new project to be created from this import.</param>
/// <param name="Owner">The identifier of the user who will own the new project.</param>
/// <param name="ForceFormat">Optionally enforces a specific file format, bypassing auto-detection.</param>
/// <param name="Delimiter">Optionally specifies the delimiter character to use for parsing.</param>
/// <param name="HeaderRow">Optionally specifies the exact row index to treat as the header.</param>
/// <param name="ColumnMappings">A dictionary mapping source file column names to system destination fields.</param>
/// <param name="SkipLastRow">Indicates whether the final row of the file (often a footer or summary) should be ignored.</param>
/// <param name="AutoDetectType">Indicates whether the system should attempt to infer sample types from their labels.</param>
/// <param name="DefaultType">The default sample type to assign if auto-detection fails or is disabled (e.g., "Samp").</param>
public record AdvancedImportRequest(
    string ProjectName,
    string? Owner = null,
    FileFormat? ForceFormat = null,
    string? Delimiter = null,
    int? HeaderRow = null,
    Dictionary<string, string>? ColumnMappings = null,
    bool SkipLastRow = true,
    bool AutoDetectType = true,
    string? DefaultType = "Samp",
    string? Device = null,
    string? FileType = null,
    string? Description = null
);

/// <summary>
/// Contains the comprehensive results of a completed data import process.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project that was created.</param>
/// <param name="TotalRowsRead">The total number of rows read from the input file.</param>
/// <param name="TotalRowsImported">The number of rows that were successfully created in the project.</param>
/// <param name="SkippedRows">The number of rows that were processed but purposefully not imported.</param>
/// <param name="DetectedFormat">The file format that was actually used for the import.</param>
/// <param name="ImportedSolutionLabels">A list of all solution labels that were successfully imported.</param>
/// <param name="ImportedElements">A list of all chemical elements that were identified and imported.</param>
/// <param name="Warnings">A collection of warnings or non-critical errors encountered during the import.</param>
public record AdvancedImportResult(
    Guid ProjectId,
    int TotalRowsRead,
    int TotalRowsImported,
    int SkippedRows,
    FileFormat DetectedFormat,
    List<string> ImportedSolutionLabels,
    List<string> ImportedElements,
    List<ImportWarning> Warnings
);

/// <summary>
/// Details a specific warning event that occurred during the data import process.
/// </summary>
/// <param name="RowNumber">The row number in the source file associated with the warning, if applicable.</param>
/// <param name="Column">The specific column name associated with the warning.</param>
/// <param name="Message">A descriptive message explaining the nature of the warning.</param>
/// <param name="Level">The severity level classification of the warning.</param>
public record ImportWarning(
    int? RowNumber,
    string Column,
    string Message,
    ImportWarningLevel Level
);

/// <summary>
/// Delineates the severity levels for warnings generated during import.
/// </summary>
public enum ImportWarningLevel
{
    /// <summary>
    /// Informational message, no significant issue.
    /// </summary>
    Info,
    
    /// <summary>
    /// Potential issue that should be noted but does not stop the process.
    /// </summary>
    Warning,
    
    /// <summary>
    /// Critical issue that prevents successful processing of a specific item.
    /// </summary>
    Error
}

/// <summary>
/// Represents a single row of data parsed from a raw input file, standardized for internal processing.
/// </summary>
/// <param name="SolutionLabel">The extracted label identifying the sample.</param>
/// <param name="Element">The element associated with this data point (if the file is in tall format).</param>
/// <param name="Intensity">The raw intensity value extracted from the row.</param>
/// <param name="CorrCon">The corrected concentration value extracted from the row.</param>
/// <param name="Type">The determined sample type.</param>
/// <param name="ActWgt">The actual weight recorded for the sample.</param>
/// <param name="ActVol">The actual volume recorded for the sample.</param>
/// <param name="DF">The dilution factor applied to the sample.</param>
/// <param name="AdditionalColumns">A dictionary of any other data columns not mapped to standard fields.</param>
public record ParsedFileRow(
    string SolutionLabel,
    string Element,
    decimal? Intensity,
    decimal? CorrCon,
    string Type,
    decimal? ActWgt,
    decimal? ActVol,
    decimal? DF,
    Dictionary<string, object?> AdditionalColumns
);

/// <summary>
/// Provides a preview of the file data and structure to allow user verification before commencing full import.
/// </summary>
/// <param name="DetectedFormat">The format that the system detected for the file.</param>
/// <param name="Headers">The list of headers detected in the file.</param>
/// <param name="PreviewRows">A small set of parsed rows to display as a preview.</param>
/// <param name="TotalRows">An estimation of the total number of rows in the file.</param>
/// <param name="SuggestedColumnMappings">A list of column pairs suggesting how file columns map to system fields.</param>
/// <param name="Message">A status message regarding the preview generation.</param>
public record FilePreviewResult(
    FileFormat DetectedFormat,
    List<string> Headers,
    List<Dictionary<string, string>> PreviewRows,
    int TotalRows,
    List<string> SuggestedColumnMappings,
    string? Message
);
public class AnalysisPreviewResult
{
    public List<string> Contracts { get; set; } = new();
    public List<string> CRMs { get; set; } = new();
    public List<string> Blanks { get; set; } = new();
    public string Device { get; set; } = "Unknown";
    public string FileType { get; set; } = "Unknown";
}