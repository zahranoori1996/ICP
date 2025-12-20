namespace Application.DTOs;

/// <summary>
/// Represents a CRM (Certified Reference Material) list item for display and selection purposes.
/// </summary>
/// <param name="Id">The internal database identifier for this CRM record.</param>
/// <param name="CrmId">The official identifier or code of the CRM (e.g., "OREAS 24c").</param>
/// <param name="AnalysisMethod">The specific analytical method used for certification (e.g., "4-Acid").</param>
/// <param name="Type">The general category or type of the CRM.</param>
/// <param name="IsOurOreas">Indicates whether this CRM is part of the frequently used or internal standard set (previously "IsOurOreas").</param>
/// <param name="Elements">A dictionary of element concentrations defined for this CRM.</param>
public record CrmListItemDto(
    int Id,
    string CrmId,
    string? AnalysisMethod,
    string? Type,
    bool IsOurOreas,
    Dictionary<string, decimal> Elements
);

/// <summary>
/// Contains the results of comparing project data against a specific CRM standard.
/// </summary>
/// <param name="SolutionLabel">The solution label from the project data.</param>
/// <param name="CrmId">The identifier of the CRM standard used for comparison.</param>
/// <param name="AnalysisMethod">The analysis method associated with the CRM.</param>
/// <param name="Differences">A list of differences for each element comparing the measured value to the CRM certified value.</param>
public record CrmDiffResultDto(
    string SolutionLabel,
    string CrmId,
    string AnalysisMethod,
    List<ElementDiffDto> Differences
);

/// <summary>
/// Represents the calculated difference for a single chemical element between a measured value and a CRM standard.
/// </summary>
/// <param name="Element">The chemical symbol of the element.</param>
/// <param name="ProjectValue">The value measured in the project sample.</param>
/// <param name="CrmValue">The certified reference value from the CRM.</param>
/// <param name="DiffPercent">The calculated percentage difference between the project value and the CRM value.</param>
/// <param name="IsInRange">Indicates whether the calculated difference falls within the specified acceptance tolerance.</param>
public record ElementDiffDto(
    string Element,
    decimal? ProjectValue,
    decimal? CrmValue,
    decimal? DiffPercent,
    bool IsInRange
);

/// <summary>
/// Represents a request to calculate the differences between project samples and known CRM standards.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to analyze.</param>
/// <param name="MinDiffPercent">The minimum percentage difference to consider acceptable (negative value for lower bound). Defaults to -12%.</param>
/// <param name="MaxDiffPercent">The maximum percentage difference to consider acceptable. Defaults to 12%.</param>
/// <param name="CrmPatterns">An optional list of string patterns used to identify which samples are CRMs (e.g., "OREAS").</param>
public record CrmDiffRequest(
    Guid ProjectId,
    decimal MinDiffPercent = -12m,
    decimal MaxDiffPercent = 12m,
    List<string>? CrmPatterns = null
);

/// <summary>
/// Represents a request to create a new CRM record or update an existing one.
/// </summary>
/// <param name="CrmId">The official identifier code for the CRM.</param>
/// <param name="AnalysisMethod">The analytical method applicable to this CRM.</param>
/// <param name="Type">The category or classification of the CRM.</param>
/// <param name="Elements">A dictionary mapping element symbols to their certified concentration values.</param>
/// <param name="IsOurOreas">Indicates if this is a commonly used internal standard.</param>
public record CrmUpsertRequest(
    string CrmId,
    string? AnalysisMethod,
    string? Type,
    Dictionary<string, decimal> Elements,
    bool IsOurOreas = false
);

/// <summary>
/// Represents a generic container for paginated query results.
/// </summary>
/// <typeparam name="T">The type of item contained in the result list.</typeparam>
/// <param name="Items">The collection of items for the current page.</param>
/// <param name="TotalCount">The total number of items available across all pages.</param>
/// <param name="Page">The current page number.</param>
/// <param name="PageSize">The number of items per page.</param>
public record PaginatedResult<T>(
    List<T> Items,
    int TotalCount,
    int Page,
    int PageSize
);