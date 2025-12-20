namespace Application.DTOs;

/// <summary>
/// Represents a request to create or retrieve a pivot table based on specific filtering and formatting criteria.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to query.</param>
/// <param name="SearchText">Optional text used to filter the rows, matching against solution labels or other text fields.</param>
/// <param name="SelectedSolutionLabels">An optional list of specific solution labels to restrict the pivot table to. If null, all matching samples are used.</param>
/// <param name="SelectedElements">An optional list of specific element symbols to include as columns in the pivot table.</param>
/// <param name="NumberFilters">A set of numeric value filters used to exclude rows based on column values.</param>
/// <param name="UseOxide">Indicates whether element values should be converted to their oxide equivalents.</param>
/// <param name="DecimalPlaces">The number of decimal places to use when formatting the output values.</param>
/// <param name="Page">The current page number for paginated results.</param>
/// <param name="PageSize">The maximum number of rows to return per page.</param>
public record PivotRequest(
    Guid ProjectId,
    string? SearchText = null,
    List<string>? SelectedSolutionLabels = null,
    List<string>? SelectedElements = null,
    Dictionary<string, NumberFilter>? NumberFilters = null,
    bool UseOxide = false,
    int DecimalPlaces = 2,
    int Page = 1,
    int PageSize = 100
);

/// <summary>
/// Defines a numeric range filter to be applied to a specific column in the pivot table.
/// </summary>
/// <param name="Min">The minimum acceptable value (inclusive). If null, no lower bound is applied.</param>
/// <param name="Max">The maximum acceptable value (inclusive). If null, no upper bound is applied.</param>
public record NumberFilter(
    decimal? Min = null,
    decimal? Max = null
);

/// <summary>
/// Represents the result of a pivot table generation operation, containing the data and metadata.
/// </summary>
/// <param name="Columns">The list of column headers included in the pivot table.</param>
/// <param name="Rows">The collection of data rows for the current page.</param>
/// <param name="TotalCount">The total number of rows matching the query criteria.</param>
/// <param name="Page">The current page number.</param>
/// <param name="PageSize">The page size used for this result.</param>
/// <param name="Metadata">Additional statistics and metadata describing the entire result set.</param>
public record PivotResultDto(
    List<string> Columns,
    List<PivotRowDto> Rows,
    int TotalCount,
    int Page,
    int PageSize,
    PivotMetadataDto Metadata
);

/// <summary>
/// Represents a single row of data within the pivot table.
/// </summary>
/// <param name="SolutionLabel">The comprehensive label identifying the sample for this row.</param>
/// <param name="Values">A dictionary where keys are column names and values are the corresponding measurements.</param>
/// <param name="OriginalIndex">The index of the row in the original source data, useful for sorting stability.</param>
public record PivotRowDto(
    string SolutionLabel,
    Dictionary<string, decimal?> Values,
    int OriginalIndex
);

/// <summary>
/// Contains descriptive statistics and metadata about the pivot table contents.
/// </summary>
/// <param name="AllSolutionLabels">A complete list of all unique solution labels present in the filtered result.</param>
/// <param name="AllElements">A complete list of all distinct elements available in the result data.</param>
/// <param name="ColumnStats">A dictionary of statistical summaries for each numeric column in the table.</param>
public record PivotMetadataDto(
    List<string> AllSolutionLabels,
    List<string> AllElements,
    Dictionary<string, ColumnStatsDto> ColumnStats
);

/// <summary>
/// Provides statistical information for a single column of data.
/// </summary>
/// <param name="Min">The minimum value found in this column.</param>
/// <param name="Max">The maximum value found in this column.</param>
/// <param name="Mean">The arithmetic mean of values in this column.</param>
/// <param name="StdDev">The standard deviation of values in this column.</param>
/// <param name="NonNullCount">The count of non-null entries in this column.</param>
public record ColumnStatsDto(
    decimal? Min,
    decimal? Max,
    decimal? Mean,
    decimal? StdDev,
    int NonNullCount
);

/// <summary>
/// Represents a request to identify duplicate data entries within a project.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to analyze.</param>
/// <param name="ThresholdPercent">The percentage threshold above which differences are considered significant. Defaults to 10%.</param>
/// <param name="DuplicatePatterns">An optional list of regex patterns or substrings used to identify duplicate pairs.</param>
public record DuplicateDetectionRequest(
    Guid ProjectId,
    decimal ThresholdPercent = 10m,
    List<string>? DuplicatePatterns = null
);

/// <summary>
/// Represents the result of a comparison between two samples identified as duplicates.
/// </summary>
/// <param name="MainSolutionLabel">The label of the primary sample.</param>
/// <param name="DuplicateSolutionLabel">The label of the secondary (duplicate) sample.</param>
/// <param name="Differences">A list of element-wise comparisons detailing the differences found.</param>
/// <param name="HasOutOfRangeDiff">Indicates whether any element comparison exceeded the defined tolerance threshold.</param>
public record DuplicateResultDto(
    string MainSolutionLabel,
    string DuplicateSolutionLabel,
    List<ElementDiffDto> Differences,
    bool HasOutOfRangeDiff
);

/// <summary>
/// Provides a comprehensive dictionary of oxide conversion factors for chemical elements.
/// </summary>
public static class OxideFactors
{
    /// <summary>
    /// A dictionary mapping element symbols to a tuple containing their oxide formula and numeric conversion factor.
    /// </summary>
    public static readonly Dictionary<string, (string Formula, decimal Factor)> Factors = new()
    {
        { "Ag", ("Ag2O", 1.0741m) },
        { "Al", ("Al2O3", 1.8895m) },
        { "As", ("As2O5", 1.5339m) },
        { "Au", ("Au2O3", 1.1218m) },
        { "B",  ("B2O3", 3.2199m) },
        { "Ba", ("BaO", 1.1165m) },
        { "Be", ("BeO", 2.7753m) },
        { "Bi", ("Bi2O3", 1.1148m) },
        { "Br", ("Br", 1.0m) },
        { "C",  ("CO2", 3.6641m) },
        { "Ca", ("CaO", 1.3992m) },
        { "Cd", ("CdO", 1.1423m) },
        { "Ce", ("CeO2", 1.2284m) },
        { "Cl", ("Cl", 1.0m) },
        { "Co", ("CoO", 1.2715m) },
        { "Cr", ("Cr2O3", 1.4616m) },
        { "Cs", ("Cs2O", 1.0602m) },
        { "Cu", ("CuO", 1.2518m) },
        { "Dy", ("Dy2O3", 1.1477m) },
        { "Er", ("Er2O3", 1.1435m) },
        { "Eu", ("Eu2O3", 1.1579m) },
        { "F",  ("F", 1.0m) },
        { "Fe", ("Fe2O3", 1.4297m) },
        { "Ga", ("Ga2O3", 1.3442m) },
        { "Gd", ("Gd2O3", 1.1526m) },
        { "Ge", ("GeO2", 1.4408m) },
        { "Hf", ("HfO2", 1.1793m) },
        { "Hg", ("HgO", 1.0798m) },
        { "Ho", ("Ho2O3", 1.1455m) },
        { "I",  ("I", 1.0m) },
        { "In", ("In2O3", 1.2091m) },
        { "Ir", ("IrO2", 1.1665m) },
        { "K",  ("K2O", 1.2046m) },
        { "La", ("La2O3", 1.1728m) },
        { "Li", ("Li2O", 2.1527m) },
        { "Lu", ("Lu2O3", 1.1371m) },
        { "Mg", ("MgO", 1.6583m) },
        { "Mn", ("MnO", 1.2912m) },
        { "Mo", ("MoO3", 1.5003m) },
        { "N",  ("N", 1.0m) },
        { "Na", ("Na2O", 1.3480m) },
        { "Nb", ("Nb2O5", 1.4305m) },
        { "Nd", ("Nd2O3", 1.1664m) },
        { "Ni", ("NiO", 1.2725m) },
        { "Os", ("OsO4", 1.3365m) },
        { "P",  ("P2O5", 2.2914m) },
        { "Pb", ("PbO", 1.0772m) },
        { "Pd", ("PdO", 1.1503m) },
        { "Pr", ("Pr6O11", 1.2082m) },
        { "Pt", ("PtO2", 1.1639m) },
        { "Rb", ("Rb2O", 1.0936m) },
        { "Re", ("Re2O7", 1.3009m) },
        { "Rh", ("Rh2O3", 1.2332m) },
        { "Ru", ("RuO2", 1.3165m) },
        { "S",  ("SO3", 2.4972m) },
        { "Sb", ("Sb2O3", 1.1971m) },
        { "Sc", ("Sc2O3", 1.5338m) },
        { "Se", ("SeO2", 1.4053m) },
        { "Si", ("SiO2", 2.1393m) },
        { "Sm", ("Sm2O3", 1.1596m) },
        { "Sn", ("SnO2", 1.2696m) },
        { "Sr", ("SrO", 1.1826m) },
        { "Ta", ("Ta2O5", 1.2211m) },
        { "Tb", ("Tb4O7", 1.1762m) },
        { "Tc", ("Tc2O7", 1.5657m) },
        { "Te", ("TeO2", 1.2508m) },
        { "Th", ("ThO2", 1.1379m) },
        { "Ti", ("TiO2", 1.6681m) },
        { "Tl", ("Tl2O3", 1.1158m) },
        { "Tm", ("Tm2O3", 1.1421m) },
        { "U",  ("U3O8", 1.1792m) },
        { "V",  ("V2O5", 1.7852m) },
        { "W",  ("WO3", 1.2611m) },
        { "Y",  ("Y2O3", 1.2699m) },
        { "Yb", ("Yb2O3", 1.1387m) },
        { "Zn", ("ZnO", 1.2447m) },
        { "Zr", ("ZrO2", 1.3508m) }
    };
}