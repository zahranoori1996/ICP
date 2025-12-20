using Application.DTOs;
using Shared.Wrapper;

namespace Application.Services;

/// <summary>
/// Defines the contract for services managing Certified Reference Materials (CRMs) and performing comparisons against them.
/// </summary>
public interface ICrmService
{
    /// <summary>
    /// Retrieves a paginated list of available CRM definitions, with optional filtering capabilities.
    /// </summary>
    /// <param name="analysisMethod">The analysis method to filter by (optional).</param>
    /// <param name="searchText">A text string to search within CRM IDs or types (optional).</param>
    /// <param name="ourOreasOnly">If true, restricts results to the "Our Oreas" internal subset.</param>
    /// <param name="page">The page number to retrieve.</param>
    /// <param name="pageSize">The number of records per page.</param>
    /// <returns>A result containing a paginated list of <see cref="CrmListItemDto"/>.</returns>
    Task<Result<PaginatedResult<CrmListItemDto>>> GetCrmListAsync(
        string? analysisMethod = null,
        string? searchText = null,
        bool? ourOreasOnly = null,
        int page = 1,
        int pageSize = 50);

    /// <summary>
    /// Retrieves the details of a specific CRM by its unique internal database identifier.
    /// </summary>
    /// <param name="id">The unique integer ID of the CRM.</param>
    /// <returns>A result containing the <see cref="CrmListItemDto"/> if found.</returns>
    Task<Result<CrmListItemDto>> GetCrmByIdAsync(int id);

    /// <summary>
    /// Searches for CRMs matching a specific official CRM identifier, optionally filtering by analysis method.
    /// </summary>
    /// <param name="crmId">The official identifier string of the CRM (e.g., "OREAS 24c").</param>
    /// <param name="analysisMethod">The specific analysis method to look for (optional).</param>
    /// <returns>A result containing a list of matching <see cref="CrmListItemDto"/> records.</returns>
    Task<Result<List<CrmListItemDto>>> GetCrmByCrmIdAsync(string crmId, string? analysisMethod = null);

    /// <summary>
    /// Computes the differences between the project's measured sample data and the certified values of matched CRMs.
    /// </summary>
    /// <param name="request">A request object specifying the project and tolerance parameters.</param>
    /// <returns>A result containing a list of <see cref="CrmDiffResultDto"/> detailing the differences.</returns>
    Task<Result<List<CrmDiffResultDto>>> CalculateDiffAsync(CrmDiffRequest request);

    /// <summary>
    /// Retrieves a distinct list of all analysis methods currently defined in the CRM library.
    /// </summary>
    /// <returns>A result containing a list of analysis method names.</returns>
    Task<Result<List<string>>> GetAnalysisMethodsAsync();

    /// <summary>
    /// Creates a new CRM record or updates an existing one based on the provided data.
    /// </summary>
    /// <param name="request">A request object containing the CRM details to save.</param>
    /// <returns>A result containing the unique ID of the upserted CRM record.</returns>
    Task<Result<int>> UpsertCrmAsync(CrmUpsertRequest request);

    /// <summary>
    /// Permanently deletes a CRM record from the database.
    /// </summary>
    /// <param name="id">The unique integer ID of the CRM to delete.</param>
    /// <returns>A result indicating true if the deletion was successful.</returns>
    Task<Result<bool>> DeleteCrmAsync(int id);

    /// <summary>
    /// Parses a CSV stream to bulk import multiple CRM records into the database.
    /// </summary>
    /// <param name="csvStream">A stream containing the CSV file data.</param>
    /// <returns>A result claiming the count of successfully imported records.</returns>
    Task<Result<int>> ImportCrmsFromCsvAsync(Stream csvStream);
}