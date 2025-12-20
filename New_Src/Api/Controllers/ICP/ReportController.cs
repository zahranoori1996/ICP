using Application.DTOs;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Handles report generation and data export operations in various formats.
/// </summary>
[ApiController]
[Route("api/reports")]
public class ReportController : ControllerBase
{
    private readonly IReportService _reportService;
    private readonly ILogger<ReportController> _logger;

    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvContentType = "text/csv";
    private const string JsonContentType = "application/json";

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportController"/> class.
    /// </summary>
    /// <param name="reportService">The report service instance.</param>
    /// <param name="logger">The logger instance.</param>
    public ReportController(IReportService reportService, ILogger<ReportController> logger)
    {
        _reportService = reportService;
        _logger = logger;
    }

    /// <summary>
    /// Generates a report based on the specified request parameters.
    /// </summary>
    /// <param name="request">The report request containing project ID and format options.</param>
    /// <returns>The generated report file.</returns>
    [HttpPost]
    public async Task<ActionResult> GenerateReport([FromBody] ReportRequest request)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        var result = await _reportService.GenerateReportAsync(request);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return File(result.Data!.Data, result.Data.ContentType, result.Data.FileName);
    }

    /// <summary>
    /// Exports project data to an Excel file.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="useOxide">Indicates whether to use oxide values.</param>
    /// <param name="decimalPlaces">The number of decimal places for rounding (default: 2).</param>
    /// <returns>An Excel file containing the project data.</returns>
    [HttpGet("{projectId:guid}/excel")]
    public async Task<ActionResult> ExportToExcel(
        Guid projectId,
        [FromQuery] bool useOxide = false,
        [FromQuery] int decimalPlaces = 2)
    {
        var options = new ReportOptions(UseOxide: useOxide, DecimalPlaces: decimalPlaces);
        var result = await _reportService.ExportToExcelAsync(projectId, options);

        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return File(result.Data!, ExcelContentType, $"export_{projectId}_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    /// <summary>
    /// Exports project data to a CSV file.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="useOxide">Indicates whether to use oxide values.</param>
    /// <returns>A CSV file containing the project data.</returns>
    [HttpGet("{projectId:guid}/csv")]
    public async Task<ActionResult> ExportToCsv(Guid projectId, [FromQuery] bool useOxide = false)
    {
        var result = await _reportService.ExportToCsvAsync(projectId, useOxide);

        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return File(result.Data!, CsvContentType, $"export_{projectId}_{DateTime.Now:yyyyMMdd}.csv");
    }

    /// <summary>
    /// Exports project data to a JSON file.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>A JSON file containing the project data.</returns>
    [HttpGet("{projectId:guid}/json")]
    public async Task<ActionResult> ExportToJson(Guid projectId)
    {
        var result = await _reportService.ExportToJsonAsync(projectId);

        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return File(result.Data!, JsonContentType, $"export_{projectId}_{DateTime.Now:yyyyMMdd}.json");
    }

    /// <summary>
    /// Generates an HTML report for the project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>An HTML report.</returns>
    [HttpGet("{projectId:guid}/html")]
    public async Task<ActionResult> GenerateHtmlReport(Guid projectId)
    {
        var result = await _reportService.GenerateHtmlReportAsync(projectId);

        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Content(result.Data!, "text/html");
    }

    /// <summary>
    /// Exports project data in the specified format.
    /// </summary>
    /// <param name="request">The export request containing project ID and format options.</param>
    /// <returns>The exported data file.</returns>
    [HttpPost("export")]
    public async Task<ActionResult> ExportData([FromBody] ExportRequest request)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        var result = await _reportService.ExportDataAsync(request);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        var (contentType, extension) = request.Format switch
        {
            ReportFormat.Excel => (ExcelContentType, "xlsx"),
            ReportFormat.Csv => (CsvContentType, "csv"),
            ReportFormat.Json => (JsonContentType, "json"),
            _ => ("application/octet-stream", "bin")
        };

        return File(result.Data!, contentType, $"export_{request.ProjectId}_{DateTime.Now:yyyyMMdd}.{extension}");
    }

    /// <summary>
    /// Retrieves calibration ranges for all elements in the project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>Calibration range data for each element.</returns>
    [HttpGet("{projectId:guid}/calibration-ranges")]
    public async Task<ActionResult> GetCalibrationRanges(Guid projectId)
    {
        var result = await _reportService.GetCalibrationRangesAsync(projectId);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }

    /// <summary>
    /// Selects the best wavelength for each base element per row.
    /// </summary>
    /// <param name="request">The wavelength selection request.</param>
    /// <returns>The selected best wavelengths for each element.</returns>
    [HttpPost("best-wavelengths")]
    public async Task<ActionResult> SelectBestWavelengths([FromBody] BestWavelengthRequest request)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest(new { succeeded = false, messages = new[] { "ProjectId is required" } });

        var result = await _reportService.SelectBestWavelengthsAsync(request);
        if (!result.Succeeded)
            return BadRequest(new { succeeded = false, messages = result.Messages });

        return Ok(new { succeeded = true, data = result.Data });
    }
}