using Application.DTOs;
using Shared.Wrapper;

namespace Application.Services;

/// <summary>
/// Defines the contract for services that generate reports and export data in various formats.
/// </summary>
public interface IReportService
{
    /// <summary>
    /// Generates a comprehensive report based on the specified configuration parameters.
    /// </summary>
    /// <param name="request">An object defining the report type, content, and formatting options.</param>
    /// <returns>A result containing the generated report file metadata and binary content.</returns>
    Task<Result<ReportResultDto>> GenerateReportAsync(ReportRequest request);

    /// <summary>
    /// Exports a specific subset of data to a requested file format (e.g., CSV, Excel) as a byte array.
    /// </summary>
    /// <param name="request">An object defining the data selection and export format.</param>
    /// <returns>A result containing the binary content of the exported file.</returns>
    Task<Result<byte[]>> ExportDataAsync(ExportRequest request);

    /// <summary>
    /// Exports the full project dataset to a multi-sheet Excel workbook.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project to export.</param>
    /// <param name="options">Optional parameters to customize the Excel output.</param>
    /// <returns>A result containing the binary content of the Excel file.</returns>
    Task<Result<byte[]>> ExportToExcelAsync(Guid projectId, ReportOptions? options = null);

    /// <summary>
    /// Exports the project dataset to a simple CSV format.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project to export.</param>
    /// <param name="useOxide">If true, converts elemental values to their oxide equivalents.</param>
    /// <returns>A result containing the binary content of the CSV file.</returns>
    Task<Result<byte[]>> ExportToCsvAsync(Guid projectId, bool useOxide = false);

    /// <summary>
    /// Exports the project dataset to a JSON format.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project to export.</param>
    /// <returns>A result containing the binary content of the JSON file.</returns>
    Task<Result<byte[]>> ExportToJsonAsync(Guid projectId);

    /// <summary>
    /// Generates an HTML representation of the project report for web viewing.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="options">Optional parameters to customize the report content.</param>
    /// <returns>A result containing the raw HTML string.</returns>
    Task<Result<string>> GenerateHtmlReportAsync(Guid projectId, ReportOptions? options = null);

    /// <summary>
    /// Calculates the valid calibration range for each element based on the project's blank samples.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <returns>A result containing a dictionary of calibration ranges keyed by element symbol.</returns>
    Task<Result<Dictionary<string, CalibrationRange>>> GetCalibrationRangesAsync(Guid projectId);

    /// <summary>
    /// Determines the optimal wavelength for each element in each sample row based on calibration data.
    /// </summary>
    /// <param name="request">An object specifying criteria for wavelength selection.</param>
    /// <returns>A result containing the mapping of best wavelengths for each data point.</returns>
    Task<Result<BestWavelengthResult>> SelectBestWavelengthsAsync(BestWavelengthRequest request);
    /// <summary>
    /// Exports the raw (unprocessed) data to an Excel file.
    /// </summary>
    Task<Result<byte[]>> ExportRawExcelAsync(Guid projectId);
}