using Application.DTOs;
using Shared.Wrapper;

namespace Application.Services;

/// <summary>
/// Defines the contract for services that transform flat project data into pivot table structures for reporting.
/// </summary>
public interface IPivotService
{
    /// <summary>
    /// Generates a standard pivot table based on the provided filtering and formatting options.
    /// </summary>
    /// <param name="request">A request object defining the pivot configuration.</param>
    /// <returns>A result containing the generated pivot table data.</returns>
    Task<Result<PivotResultDto>> GetPivotTableAsync(PivotRequest request);

    /// <summary>
    /// Generates a complex pivot table that supports Geochemical Data (GCD) logic and repeat sample handling.
    /// </summary>
    /// <param name="request">A request object defining the advanced pivot configuration.</param>
    /// <returns>A result containing the advanced pivot table data and associated metadata.</returns>
    Task<Result<AdvancedPivotResultDto>> GetAdvancedPivotTableAsync(AdvancedPivotRequest request);

    /// <summary>
    /// Retrieves a complete list of unique solution labels currently existing within the project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>A result containing the list of solution label strings.</returns>
    Task<Result<List<string>>> GetSolutionLabelsAsync(Guid projectId);

    /// <summary>
    /// Retrieves a complete list of unique chemical element symbols associated with the project data.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>A result containing the list of element symbol strings.</returns>
    Task<Result<List<string>>> GetElementsAsync(Guid projectId);

    /// <summary>
    /// Scans the project data to identify potential duplicate samples based on configuration patterns.
    /// </summary>
    /// <param name="request">A request object specifying the detection criteria and thresholds.</param>
    /// <returns>A result containing a list of identified duplicate pairs and their differences.</returns>
    Task<Result<List<DuplicateResultDto>>> DetectDuplicatesAsync(DuplicateDetectionRequest request);

    /// <summary>
    /// Computes statistical summaries (e.g., Min, Max, Mean, Standard Deviation) for all numeric columns in the project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>A result containing a dictionary of statistics for each column.</returns>
    Task<Result<Dictionary<string, ColumnStatsDto>>> GetColumnStatsAsync(Guid projectId);

    /// <summary>
    /// Exports the generated pivot table data to a CSV file format.
    /// </summary>
    /// <param name="request">A request object defining the pivot configuration to export.</param>
    /// <returns>A result containing the CSV file content as a byte array.</returns>
    Task<Result<byte[]>> ExportToCsvAsync(PivotRequest request);

    /// <summary>
    /// Analyzes the project data structure to identify and report on repeating sample patterns.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>A result containing detailed analysis of any repeated structures found.</returns>
    Task<Result<RepeatAnalysisDto>> AnalyzeRepeatsAsync(Guid projectId);
}

/// <summary>
/// Encapsulates the results of an analysis of repeated sample patterns within the project.
/// </summary>
/// <param name="HasRepeats">True if the analysis detected any repeating sample patterns.</param>
/// <param name="TotalSamples">The total number of samples processed during analysis.</param>
/// <param name="SetSizes">A dictionary mapping solution labels to the count of samples in their repeat set.</param>
/// <param name="RepeatedElements">A dictionary mapping solution labels to the specific elements that repeat.</param>
/// <param name="ElementCounts">A dictionary aggregating the total occurrence count of each element.</param>
public record RepeatAnalysisDto(
    bool HasRepeats,
    int TotalSamples,
    Dictionary<string, int> SetSizes,
    Dictionary<string, List<string>> RepeatedElements,
    Dictionary<string, int> ElementCounts
);