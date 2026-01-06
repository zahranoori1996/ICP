using System.Globalization;
using System.Text.Json;
using Application.DTOs;
using Application.Services;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Shared.Wrapper;

namespace Infrastructure.Services;

/// <summary>
/// Service for importing project data from various file formats.
/// </summary>
public class ImportService : IImportService
{
    private const int DefaultChunkSize = 500;
    private readonly IProjectPersistenceService _persistence;
    private readonly ILogger<ImportService> _logger;
    private readonly AdvancedFileParser _parser;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportService"/> class.
    /// </summary>
    /// <param name="persistence">The project persistence service.</param>
    /// <param name="logger">The logger instance.</param>
    public ImportService(IProjectPersistenceService persistence, ILogger<ImportService> logger)
    {
        _persistence = persistence;
        _logger = logger;
        _parser = new AdvancedFileParser(logger);
    }

    #region Basic Import (existing)

    /// <inheritdoc/>
    public async Task<Result<ProjectSaveResult>> ImportCsvAsync(
        Stream stream,
        string projectName,
        string? owner,
        string? stateJson,
        IProgress<(int total, int processed)>? progress = null)
    {
        MemoryStream ms = new MemoryStream();
        try
        {
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            int totalRows = 0;
            using (var readerCount = new StreamReader(ms, leaveOpen: true))
            using (var csvCount = new CsvReader(readerCount, CultureInfo.InvariantCulture))
            {
                if (!csvCount.Read()) return Result<ProjectSaveResult>.Fail("CSV empty");
                csvCount.ReadHeader();
                while (csvCount.Read())
                {
                    totalRows++;
                }
            }

            ms.Position = 0;

            using var reader = new StreamReader(ms);
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                DetectDelimiter = true,
                BadDataFound = null,
                MissingFieldFound = null,
                TrimOptions = TrimOptions.Trim
            };

            using var csv = new CsvReader(reader, config);
            if (!csv.Read()) return Result<ProjectSaveResult>.Fail("CSV empty");
            csv.ReadHeader();
            var headers = csv.HeaderRecord;
            if (headers == null || headers.Length == 0) return Result<ProjectSaveResult>.Fail("CSV has no header");

            var processed = 0;
            Guid? knownProjectId = null;
            var batch = new List<RawDataDto>(DefaultChunkSize);

            while (csv.Read())
            {
                string? sampleId = null;
                var sampleIdHeader = headers.FirstOrDefault(h => string.Equals(h, "SampleId", StringComparison.OrdinalIgnoreCase));
                if (sampleIdHeader != null)
                {
                    sampleId = csv.GetField(sampleIdHeader);
                }

                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var h in headers)
                {
                    if (string.Equals(h, "SampleId", StringComparison.OrdinalIgnoreCase)) continue;
                    var value = csv.GetField(h);
                    if (value == null) { dict[h] = null; continue; }
                    if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) dict[h] = d;
                    else if (bool.TryParse(value, out var b)) dict[h] = b;
                    else dict[h] = value;
                }

                var columnDataJson = JsonSerializer.Serialize(dict);
                batch.Add(new RawDataDto(columnDataJson, string.IsNullOrWhiteSpace(sampleId) ? null : sampleId));
                processed++;

                progress?.Report((totalRows, processed));

                if (batch.Count >= DefaultChunkSize)
                {
                    var saveProjectId = knownProjectId ?? Guid.Empty;
                    var res = await _persistence.SaveProjectAsync(saveProjectId, projectName, owner, batch, stateJson);
                    if (!res.Succeeded)
                    {
                        var msg = (res.Messages ?? Array.Empty<string>()).FirstOrDefault();
                        return Result<ProjectSaveResult>.Fail($"Import failed during batch save: {msg ?? "unknown error"}");
                    }

                    knownProjectId = res.Data!.ProjectId;
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                var saveProjectId = knownProjectId ?? Guid.Empty;
                var res = await _persistence.SaveProjectAsync(saveProjectId, projectName, owner, batch, stateJson);
                if (!res.Succeeded)
                {
                    var msg = (res.Messages ?? Array.Empty<string>()).FirstOrDefault();
                    return Result<ProjectSaveResult>.Fail($"Import failed during final save: {msg ?? "unknown error"}");
                }

                knownProjectId = res.Data!.ProjectId;
                batch.Clear();
            }

            progress?.Report((totalRows, processed));

            return Result<ProjectSaveResult>.Success(new ProjectSaveResult(knownProjectId ?? Guid.Empty));
        }
        catch (Exception ex)
        {
            return Result<ProjectSaveResult>.Fail($"Import failed: {ex.Message}");
        }
        finally
        {
            try { ms.Dispose(); } catch { }
        }
    }

    #endregion

    #region Advanced Import

    /// <inheritdoc/>
    public async Task<Result<FileFormatDetectionResult>> DetectFormatAsync(Stream fileStream, string fileName)
    {
        try
        {
            var result = await _parser.DetectFormatAsync(fileStream, fileName);
            return Result<FileFormatDetectionResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect format for {FileName}", fileName);
            return Result<FileFormatDetectionResult>.Fail($"Format detection failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<FilePreviewResult>> PreviewFileAsync(Stream fileStream, string fileName, int previewRows = 10)
    {
        try
        {
            var detection = await _parser.DetectFormatAsync(fileStream, fileName);
            fileStream.Position = 0;

            var preview = new List<Dictionary<string, string>>();
            using var reader = new StreamReader(fileStream, leaveOpen: true);

            // Get columns - either from detection or parse from first line
            var columns = detection.DetectedColumns;
            var delimiter = detection.DetectedDelimiter ?? ",";

            int rowsRead = 0;
            string? line;

            // Read first line for headers if not detected
            if ((columns == null || columns.Count == 0) && (line = await reader.ReadLineAsync()) != null)
            {
                columns = line.Split(new[] { delimiter[0] }, StringSplitOptions.None)
                    .Select(h => h.Trim().Trim('"'))
                    .ToList();
                rowsRead++;
            }
            else if (detection.Format == FileFormat.TabularCsv)
            {
                // Skip header row for tabular format (already have columns from detection)
                await reader.ReadLineAsync();
                rowsRead++;
            }

            // Ensure we have columns
            if (columns == null || columns.Count == 0)
            {
                return Result<FilePreviewResult>.Fail("Could not detect columns in file");
            }

            // Read data rows
            while (rowsRead < previewRows + 1 && (line = await reader.ReadLineAsync()) != null)
            {
                var row = new Dictionary<string, string>();
                var parts = line.Split(new[] { delimiter[0] }, StringSplitOptions.None);
                for (int j = 0; j < parts.Length && j < columns.Count; j++)
                {
                    row[columns[j]] = parts[j].Trim().Trim('"');
                }
                preview.Add(row);
                rowsRead++;
            }

            return Result<FilePreviewResult>.Success(new FilePreviewResult(
                detection.Format,
                columns,
                preview,
                preview.Count,
                new List<string>(),
                detection.Message
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preview file {FileName}", fileName);
            return Result<FilePreviewResult>.Fail($"Preview failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<AdvancedImportResult>> ImportAdvancedAsync(
        Stream fileStream,
        string fileName,
        AdvancedImportRequest request,
        IProgress<(int total, int processed, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Detect format if not forced
            var format = request.ForceFormat ?? FileFormat.Unknown;
            if (format == FileFormat.Unknown)
            {
                var detection = await _parser.DetectFormatAsync(fileStream, fileName);
                format = detection.Format;
                fileStream.Position = 0;
            }

            if (format == FileFormat.Unknown)
            {
                return Result<AdvancedImportResult>.Fail("Could not determine file format");
            }

            progress?.Report((0, 0, "Parsing file... "));

            // Parse file
            var (parsedRows, warnings) = await _parser.ParseFileAsync(
                fileStream, fileName, format, request, progress, cancellationToken);

            // If no parsed rows, fallback to simple CSV import
            if (!parsedRows.Any())
            {
                _logger.LogWarning("Advanced parser returned no rows, falling back to simple import");
                fileStream.Position = 0;
                return await ImportSimpleCsvAsync(fileStream, request, progress, cancellationToken);
            }

            progress?.Report((parsedRows.Count, 0, "Saving to database..."));

            // Convert to RawDataDto and save
            var batch = new List<RawDataDto>(DefaultChunkSize);
            Guid? projectId = null;
            int saved = 0;

            foreach (var row in parsedRows)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var dict = new Dictionary<string, object?>
                {
                    ["Solution Label"] = row.SolutionLabel,
                    ["Element"] = row.Element,
                    ["Int"] = row.Intensity,
                    ["Corr Con"] = row.CorrCon,
                    ["Type"] = row.Type
                };

                if (row.ActWgt.HasValue) dict["Act Wgt"] = row.ActWgt;
                if (row.ActVol.HasValue) dict["Act Vol"] = row.ActVol;
                if (row.DF.HasValue) dict["DF"] = row.DF;

                foreach (var additional in row.AdditionalColumns)
                {
                    dict[additional.Key] = additional.Value;
                }

                var columnDataJson = JsonSerializer.Serialize(dict);
                batch.Add(new RawDataDto(columnDataJson, row.SolutionLabel));

                if (batch.Count >= DefaultChunkSize)
                {
                    var saveResult = await _persistence.SaveProjectAsync(
                        projectId ?? Guid.Empty,
                        request.ProjectName,
                        request.Owner,
                        batch,
                        null,
                        request.Device,
        request.FileType,
        request.Description);

                    if (!saveResult.Succeeded)
                    {
                        return Result<AdvancedImportResult>.Fail(
                            $"Failed to save batch: {saveResult.Messages?.FirstOrDefault()}");
                    }

                    projectId = saveResult.Data!.ProjectId;
                    saved += batch.Count;
                    batch.Clear();

                    progress?.Report((parsedRows.Count, saved, $"Saved {saved}/{parsedRows.Count} rows"));
                }
            }

            // Save remaining
            if (batch.Count > 0)
            {
                var saveResult = await _persistence.SaveProjectAsync(
                    projectId ?? Guid.Empty,
                    request.ProjectName,
                    request.Owner,
                    batch,
                    null,
                    request.Device,
        request.FileType,
        request.Description);

                if (!saveResult.Succeeded)
                {
                    return Result<AdvancedImportResult>.Fail(
                        $"Failed to save final batch: {saveResult.Messages?.FirstOrDefault()}");
                }

                projectId = saveResult.Data!.ProjectId;
                saved += batch.Count;
            }

            var solutionLabels = parsedRows.Select(r => r.SolutionLabel).Distinct().ToList();
            var elements = parsedRows.Select(r => r.Element).Distinct().ToList();

            return Result<AdvancedImportResult>.Success(new AdvancedImportResult(
                projectId ?? Guid.Empty,
                parsedRows.Count + warnings.Count(w => w.Level == ImportWarningLevel.Warning),
                saved,
                warnings.Count(w => w.Level == ImportWarningLevel.Warning),
                format,
                solutionLabels,
                elements,
                warnings
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Advanced import failed for {FileName}", fileName);
            return Result<AdvancedImportResult>.Fail($"Import failed: {ex.Message}");
        }
    }

    private async Task<Result<AdvancedImportResult>> ImportSimpleCsvAsync(
        Stream fileStream,
        AdvancedImportRequest request,
        IProgress<(int total, int processed, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                DetectDelimiter = true,
                BadDataFound = null,
                MissingFieldFound = null,
                TrimOptions = TrimOptions.Trim,
                HeaderValidated = null
            };

            using var reader = new StreamReader(fileStream, System.Text.Encoding.UTF8, leaveOpen: true);
            using var csv = new CsvReader(reader, config);

            // Read first line
            if (!csv.Read())
            {
                return Result<AdvancedImportResult>.Fail("File is empty or has no header");
            }

            // Check if this line is actually a header
            // Criteria: presence of keywords like "Solution Label" or "Sample"
            var rawRecord = csv.Parser.RawRecord;
            bool looksLikeHeader = !string.IsNullOrEmpty(rawRecord) &&
                                   (rawRecord.Contains("Solution Label", StringComparison.OrdinalIgnoreCase) ||
                                    rawRecord.Contains("Sample", StringComparison.OrdinalIgnoreCase) ||
                                    rawRecord.Contains("Element", StringComparison.OrdinalIgnoreCase));

            // If the first line is not a header (like OES files with metadata), read one more line
            if (!looksLikeHeader)
            {
                if (!csv.Read())
                {
                    return Result<AdvancedImportResult>.Fail("File has metadata but no header row found");
                }
            }

            // Read the header
            csv.ReadHeader();

            var headers = csv.HeaderRecord ?? Array.Empty<string>();

            if (headers.Length == 0)
            {
                return Result<AdvancedImportResult>.Fail("No headers found in file");
            }

            _logger.LogInformation("Simple import: Found {Count} columns: {Headers}", headers.Length, string.Join(", ", headers));

            var batch = new List<RawDataDto>(DefaultChunkSize);
            Guid? projectId = null;
            int rowNumber = 0;
            int saved = 0;
            var warnings = new List<ImportWarning>();

            // Find SampleID column (various possible names)
            var sampleIdColumn = headers.FirstOrDefault(h =>
                h.Equals("Solution Label", StringComparison.OrdinalIgnoreCase) || // Added Solution Label explicitly
                h.Equals("SampleID", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("Sample_ID", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("SampleId", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("Sample", StringComparison.OrdinalIgnoreCase) ||
                h.Contains("Sample", StringComparison.OrdinalIgnoreCase));

            while (csv.Read())
            {
                if (cancellationToken.IsCancellationRequested) break;
                rowNumber++;

                // Skip last row if requested
                if (request.SkipLastRow && csv.Parser.RawRow == csv.Parser.Count - 1)
                    continue;

                try
                {
                    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    string? sampleId = null;

                    foreach (var header in headers)
                    {
                        var value = csv.GetField(header);

                        if (sampleIdColumn != null && header.Equals(sampleIdColumn, StringComparison.OrdinalIgnoreCase))
                        {
                            sampleId = value;
                        }

                        // Try to parse as number, otherwise keep as string
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            dict[header] = null;
                        }
                        else if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                        {
                            dict[header] = d;
                        }
                        else if (bool.TryParse(value, out var b))
                        {
                            dict[header] = b;
                        }
                        else
                        {
                            dict[header] = value;
                        }
                    }

                    var columnDataJson = JsonSerializer.Serialize(dict);
                    batch.Add(new RawDataDto(columnDataJson, sampleId));
                }
                catch (Exception ex)
                {
                    warnings.Add(new ImportWarning(rowNumber, "", $"Row parse error: {ex.Message}", ImportWarningLevel.Warning));
                }

                if (batch.Count >= DefaultChunkSize)
                {
                    var saveResult = await _persistence.SaveProjectAsync(
                        projectId ?? Guid.Empty,
                        request.ProjectName,
                        request.Owner,
                        batch,
                        null,
                        request.Device,
        request.FileType,
        request.Description);

                    if (!saveResult.Succeeded)
                    {
                        return Result<AdvancedImportResult>.Fail(
                            $"Failed to save batch: {saveResult.Messages?.FirstOrDefault()}");
                    }

                    projectId = saveResult.Data!.ProjectId;
                    saved += batch.Count;
                    batch.Clear();

                    progress?.Report((rowNumber, saved, $"Saved {saved} rows"));
                }
            }

            // Save remaining batch
            if (batch.Count > 0)
            {
                var saveResult = await _persistence.SaveProjectAsync(
                    projectId ?? Guid.Empty,
                    request.ProjectName,
                    request.Owner,
                    batch,
                    null,
                    request.Device,
        request.FileType,
        request.Description);

                if (!saveResult.Succeeded)
                {
                    return Result<AdvancedImportResult>.Fail(
                        $"Failed to save final batch: {saveResult.Messages?.FirstOrDefault()}");
                }

                projectId = saveResult.Data!.ProjectId;
                saved += batch.Count;
            }

            if (projectId == null)
            {
                return Result<AdvancedImportResult>.Fail("No data was imported");
            }

            _logger.LogInformation("Simple import completed: {Rows} rows imported to project {ProjectId}", saved, projectId);

            return Result<AdvancedImportResult>.Success(new AdvancedImportResult(
                projectId.Value,
                rowNumber,
                saved,
                rowNumber - saved,
                FileFormat.TabularCsv,
                new List<string>(),
                headers.ToList(),
                warnings
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Simple CSV import failed");
            return Result<AdvancedImportResult>.Fail($"Import failed: {ex.Message}");
        }
    }
    /// <inheritdoc/>
    public async Task<Result<AdvancedImportResult>> ImportAdditionalAsync(
        Guid projectId,
        Stream fileStream,
        string fileName,
        AdvancedImportRequest? request = null,
        IProgress<(int total, int processed, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Similar to ImportAdvancedAsync but appends to existing project
        var importRequest = request ?? new AdvancedImportRequest("Additional", null);

        var result = await ImportAdvancedAsync(fileStream, fileName, importRequest, progress, cancellationToken);

        // Note: In a full implementation, we'd merge with existing project instead of creating new
        return result;
    }

    /// <inheritdoc/>
    public async Task<Result<AdvancedImportResult>> ImportExcelAsync(
        Stream fileStream,
        string fileName,
        AdvancedImportRequest request,
        IProgress<(int total, int processed, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Force Excel format
        var excelRequest = request with { ForceFormat = FileFormat.TabularExcel };
        return await ImportAdvancedAsync(fileStream, fileName, excelRequest, progress, cancellationToken);
    }

    #endregion



    /// <summary>
    /// Analyzes the file content to categorize rows into Contracts, CRMs, and Blanks
    /// similar to the desktop application logic.
    /// </summary>
// این متد را داخل کلاس ImportService قرار دهید

    public async Task<Result<AnalysisPreviewResult>> AnalyzeFileAsync(Stream fileStream, string fileName)
    {
        try
        {
            var result = new AnalysisPreviewResult();

            // اطمینان از اینکه استریم از ابتدا خوانده می‌شود
            if (fileStream.CanSeek)
            {
                fileStream.Position = 0;
            }

            using var reader = new StreamReader(fileStream, leaveOpen: true);

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var trimmedLine = line.Trim();

                // 1. تشخیص CRM
                if (trimmedLine.StartsWith("CRM", StringComparison.OrdinalIgnoreCase))
                {
                    result.CRMs.Add(trimmedLine);
                }
                // 2. تشخیص Blank
                else if (trimmedLine.Contains("Blank", StringComparison.OrdinalIgnoreCase) ||
                         trimmedLine.Contains("BLANK", StringComparison.OrdinalIgnoreCase))
                {
                    result.Blanks.Add(trimmedLine);
                }
                // 3. تشخیص قراردادها (مثلاً اگر با عدد شروع شود)
                else if (char.IsDigit(trimmedLine[0]))
                {
                    result.Contracts.Add(trimmedLine);
                }
            }

            // مقادیر نمونه برای دستگاه و نوع فایل
            result.Device = "Mass alam9000 1";
            result.FileType = Path.GetExtension(fileName).Trim('.');

            return Result<AnalysisPreviewResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis failed for {FileName}", fileName);
            return Result<AnalysisPreviewResult>.Fail($"Analysis failed: {ex.Message}");
        }
    }
}