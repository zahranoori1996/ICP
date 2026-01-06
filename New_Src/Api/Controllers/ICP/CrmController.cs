using Application.DTOs;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Handles Certified Reference Material (CRM) operations including retrieval, updating, importing, and difference calculation.
/// </summary>
[ApiController]
[Route("api/crm")]
public class CrmController : ControllerBase
{
    private readonly ICrmService _crmService;
    private readonly ILogger<CrmController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CrmController"/> class.
    /// </summary>
    /// <param name="crmService">The CRM service instance.</param>
    /// <param name="logger">The logger instance.</param>
    public CrmController(ICrmService crmService, ILogger<CrmController> logger)
    {
        _crmService = crmService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves a paginated list of CRMs with optional filtering.
    /// </summary>
    /// <param name="analysisMethod">The analysis method to filter by (optional).</param>
    /// <param name="searchText">The search text for filtering CRMs (optional).</param>
    /// <param name="ourOreasOnly">Indicates whether to filter only OREAS CRMs (optional).</param>
    /// <param name="page">The page number for pagination (default: 1).</param>
    /// <param name="pageSize">The number of items per page (default: 0 to return all).</param>
    /// <returns>A paginated list of CRMs matching the criteria.</returns>
    [HttpGet]
    public async Task<ActionResult> GetCrmList(
        [FromQuery] string? analysisMethod = null,
        [FromQuery] string? searchText = null,
        [FromQuery] bool? ourOreasOnly = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 0)
    {
        var result = await _crmService.GetCrmListAsync(analysisMethod, searchText, ourOreasOnly, page, pageSize);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Retrieves a single CRM by its database ID.
    /// </summary>
    /// <param name="id">The unique identifier of the CRM.</param>
    /// <returns>The CRM details.</returns>
    [HttpGet("{id:int}")]
    public async Task<ActionResult> GetCrmById(int id)
    {
        var result = await _crmService.GetCrmByIdAsync(id);
        if (!result.Succeeded)
        {
            return NotFound(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Retrieves CRMs matching a specific CRM ID string (e.g., "OREAS 258").
    /// </summary>
    /// <param name="crmId">The CRM identifier string.</param>
    /// <param name="analysisMethod">The analysis method to filter by (optional).</param>
    /// <returns>A list of matching CRMs.</returns>
    [HttpGet("search/{crmId}")]
    public async Task<ActionResult> GetCrmByCrmId(string crmId, [FromQuery] string? analysisMethod = null)
    {
        var result = await _crmService.GetCrmByCrmIdAsync(crmId, analysisMethod);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Retrieves a list of all available analysis methods.
    /// </summary>
    /// <returns>A list of analysis method names.</returns>
    [HttpGet("methods")]
    public async Task<ActionResult> GetAnalysisMethods()
    {
        var result = await _crmService.GetAnalysisMethodsAsync();
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Calculates the differences between project data and CRM expected values.
    /// </summary>
    /// <param name="request">The request containing project ID and CRM comparison criteria.</param>
    /// <returns>The result of the difference calculation.</returns>
    [HttpPost("diff")]
    public async Task<ActionResult> CalculateDiff([FromBody] CrmDiffRequest request)
    {
        if (request.ProjectId == Guid.Empty)
        {
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });
        }

        var result = await _crmService.CalculateDiffAsync(request);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Adds a new CRM or updates an existing one.
    /// </summary>
    /// <param name="request">The CRM details to upsert.</param>
    /// <returns>The ID of the upserted CRM.</returns>
    [HttpPost]
    public async Task<ActionResult> UpsertCrm([FromBody] CrmUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CrmId))
        {
            return BadRequest(new { succeeded = false, messages = new[] { "CrmId is required" } });
        }

        var result = await _crmService.UpsertCrmAsync(request);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = new { id = result.Data } });
    }

    /// <summary>
    /// Deletes a CRM record by its ID.
    /// </summary>
    /// <param name="id">The unique identifier of the CRM to delete.</param>
    /// <returns>A confirmation of the deletion.</returns>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteCrm(int id)
    {
        var result = await _crmService.DeleteCrmAsync(id);
        if (!result.Succeeded)
        {
            return NotFound(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = new { deleted = true } });
    }

    /// <summary>
    /// Imports CRM records from a CSV file.
    /// </summary>
    /// <param name="file">The CSV file containing CRM data.</param>
    /// <returns>The count of imported records.</returns>
    [HttpPost("import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult> ImportCrms([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { succeeded = false, messages = new[] { "File is required" } });
        }

        using var stream = file.OpenReadStream();
        var result = await _crmService.ImportCrmsFromCsvAsync(stream);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, messages = result.Messages });
        }

        return Ok(new { succeeded = true, data = new { importedCount = result.Data } });
    }
}
