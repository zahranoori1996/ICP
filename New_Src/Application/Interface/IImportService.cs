using Application.DTOs;
using Shared.Wrapper;

namespace Application.Services;

/// <summary>
/// Defines the contract for services handling the import of external data files into the system.
/// </summary>
public interface IImportService
{
    /// <summary>
    /// Imports data from a standard CSV stream into a new project structure.
    /// </summary>
    /// <param name="csvStream">The input stream containing CSV data.</param>
    /// <param name="projectName">The name to assign to the newly created project.</param>
    /// <param name="owner">The identifier of the user creating the project.</param>
    /// <param name="stateJson">Optional JSON string defining initial state or configuration.</param>
    /// <param name="progress">An optional provider for reporting progress updates.</param>
    /// <returns>A result containing details of the saved project.</returns>
    Task<Result<ProjectSaveResult>> ImportCsvAsync(
        Stream csvStream,
        string projectName,
        string? owner = null,
        string? stateJson = null,
        IProgress<(int total, int processed)>? progress = null);

    /// <summary>
    /// Performs a sophisticated import capable of handling various formats and configuration options.
    /// </summary>
    /// <param name="fileStream">The input stream of the file to import.</param>
    /// <param name="fileName">The specific name of the file being imported.</param>
    /// <param name="request">An object defining detailed import settings and mappings.</param>
    /// <param name="progress">An optional provider for reporting detailed progress.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A result containing comprehensive statistics and details of the import execution.</returns>
    Task<Result<AdvancedImportResult>> ImportAdvancedAsync(
        Stream fileStream,
        string fileName,
        AdvancedImportRequest request,
        IProgress<(int total, int processed, string message)>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inspects a file stream to determine its format and structure without performing a full import.
    /// </summary>
    /// <param name="fileStream">The input stream of the file to analyze.</param>
    /// <param name="fileName">The name of the file being analyzed.</param>
    /// <returns>A result describing the detected file format and properties.</returns>
    Task<Result<FileFormatDetectionResult>> DetectFormatAsync(
        Stream fileStream,
        string fileName);

    /// <summary>
    /// Generates a localized preview of the file content to assist with mapping and verification.
    /// </summary>
    /// <param name="fileStream">The input stream of the file to preview.</param>
    /// <param name="fileName">The name of the file being previewed.</param>
    /// <param name="previewRows">The number of rows to include in the preview. Defaults to 10.</param>
    /// <returns>A result containing the preview data and headers.</returns>
    Task<Result<FilePreviewResult>> PreviewFileAsync(
        Stream fileStream,
        string fileName,
        int previewRows = 10);

    /// <summary>
    /// Appends data from an additional file to an existing project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the target project.</param>
    /// <param name="fileStream">The input stream of the file to append.</param>
    /// <param name="fileName">The name of the file being appended.</param>
    /// <param name="request">Optional settings to control the import behavior.</param>
    /// <param name="progress">An optional provider for reporting detailed progress.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A result containing statistics about the appended data.</returns>
    Task<Result<AdvancedImportResult>> ImportAdditionalAsync(
        Guid projectId,
        Stream fileStream,
        string fileName,
        AdvancedImportRequest? request = null,
        IProgress<(int total, int processed, string message)>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports data from an Excel workbook stream.
    /// </summary>
    /// <param name="fileStream">The input stream of the Excel file.</param>
    /// <param name="fileName">The name of the Excel file.</param>
    /// <param name="request">An object defining import settings.</param>
    /// <param name="progress">An optional provider for reporting detailed progress.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A result containing comprehensive statistics and details of the import execution.</returns>
    Task<Result<AdvancedImportResult>> ImportExcelAsync(
        Stream fileStream,
        string fileName,
        AdvancedImportRequest request,
        IProgress<(int total, int processed, string message)>? progress = null,
        CancellationToken cancellationToken = default);


    Task<Result<AnalysisPreviewResult>> AnalyzeFileAsync(Stream fileStream, string fileName);
}