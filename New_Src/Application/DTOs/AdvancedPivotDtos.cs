namespace Application.DTOs;

/// <summary>
/// Represents a request to generate an advanced pivot table, including options for specific data filtering, correction application, and repeat merging.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to query.</param>
/// <param name="SearchText">Optional text to filter the results by sample label or other searchable fields.</param>
/// <param name="SelectedSolutionLabels">A specific list of solution labels to include in the report. If null, all matching samples are included.</param>
/// <param name="SelectedElements">A specific list of chemical elements (e.g., "Au", "Ag") to include as columns.</param>
/// <param name="NumberFilters">A dictionary of filters to apply to numeric columns, keyed by column name.</param>
/// <param name="UseOxide">Values should be converted to their oxide forms where applicable.</param>
/// <param name="UseInt">Values should be displayed as raw intensity rather than standard concentration.</param>
/// <param name="DecimalPlaces">The number of decimal places to use when formatting numeric values.</param>
/// <param name="Page">The page number for pagination, starting from 1.</param>
/// <param name="PageSize">The number of records per page.</param>
/// <param name="Aggregation">The aggregation strategy to use when multiple values exist for a single cell.</param>
/// <param name="MergeRepeats">Multiple measurements of the same sample should be merged (e.g., averaged) into a single row.</param>
public record AdvancedPivotRequest(
    Guid ProjectId,
    string? SearchText = null,
    List<string>? SelectedSolutionLabels = null,
    List<string>? SelectedElements = null,
    Dictionary<string, NumberFilter>? NumberFilters = null,
    bool UseOxide = false,
    bool UseInt = false,
    int DecimalPlaces = 2,
    int Page = 1,
    int PageSize = 100,
    PivotAggregation Aggregation = PivotAggregation.First,
    bool MergeRepeats = false
);

/// <summary>
/// Specifies the aggregation method to use when multiple data points map to the same pivot cell.
/// </summary>
public enum PivotAggregation
{
    /// <summary>
    /// Uses the first encountered value.
    /// </summary>
    First,
    
    /// <summary>
    /// Uses the last encountered value.
    /// </summary>
    Last,
    
    /// <summary>
    /// Calculates the arithmetic mean of the values.
    /// </summary>
    Mean,
    
    /// <summary>
    /// Calculates the sum of the values.
    /// </summary>
    Sum,
    
    /// <summary>
    /// Uses the minimum value.
    /// </summary>
    Min,
    
    /// <summary>
    /// Uses the maximum value.
    /// </summary>
    Max,
    
    /// <summary>
    /// Counts the number of values.
    /// </summary>
    Count
}

/// <summary>
/// Represents the paginated result of an advanced pivot table query, including data rows and metadata.
/// </summary>
/// <param name="Columns">The list of column headers included in the pivot table.</param>
/// <param name="Rows">The list of data rows for the current page.</param>
/// <param name="TotalCount">The total number of rows matching the query execution.</param>
/// <param name="Page">The current page number.</param>
/// <param name="PageSize">The number of max rows per page.</param>
/// <param name="Metadata">Additional metadata about the pivot result, such as column statistics and repeat information.</param>
public record AdvancedPivotResultDto(
    List<string> Columns,
    List<AdvancedPivotRowDto> Rows,
    int TotalCount,
    int Page,
    int PageSize,
    AdvancedPivotMetadataDto Metadata
);

/// <summary>
/// Represents a single row in the advanced pivot table, corresponding to a specific sample or repeat set.
/// </summary>
/// <param name="SolutionLabel">The unique label identifying the sample.</param>
/// <param name="Values">A dictionary of cell values for this row, keyed by the column header.</param>
/// <param name="OriginalIndex">The index of the row in the original dataset, used for stable sorting.</param>
/// <param name="SetIndex">The index indicating the repeat set. 0 represents the main sample, while >0 indicates a repeat.</param>
/// <param name="SetSize">The total number of related samples in this repeat set.</param>
public record AdvancedPivotRowDto(
    string SolutionLabel,
    Dictionary<string, decimal?> Values,
    int OriginalIndex,
    int SetIndex,
    int SetSize
);

/// <summary>
/// Contains metadata related to the advanced pivot table generation, such as statistical summaries and repeat detection results.
/// </summary>
/// <param name="AllSolutionLabels">A complete list of all unique solution labels found in the result set.</param>
/// <param name="AllElements">A complete list of all chemical elements found in the result set.</param>
/// <param name="ColumnStats">Statistical summaries (min, max, average) for each numeric column.</param>
/// <param name="HasRepeats">Indicates whether any repeated samples were detected in the valid dataset.</param>
/// <param name="SetSizes">A mapping of solution labels to the number of repeats found for that label.</param>
/// <param name="RepeatedElements">A mapping of solution labels to the specific elements that were repeated.</param>
public record AdvancedPivotMetadataDto(
    List<string> AllSolutionLabels,
    List<string> AllElements,
    Dictionary<string, ColumnStatsDto> ColumnStats,
    bool HasRepeats,
    Dictionary<string, int> SetSizes,
    Dictionary<string, List<string>> RepeatedElements
);