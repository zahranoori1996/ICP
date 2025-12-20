using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Application.DTOs;
using Application.Services;
using ClosedXML.Excel;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Wrapper;

namespace Infrastructure.Services;

/// <summary>
/// Implementation of IReportService. 
/// Handles report generation and data exports.
/// </summary>
public class ReportService : IReportService
{
    private readonly IsatisDbContext _db;
    private readonly IPivotService _pivotService;
    private readonly IRmCheckService _rmCheckService;
    private readonly ICrmService _crmService;
    private readonly ILogger<ReportService> _logger;

    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public ReportService(
        IsatisDbContext db,
        IPivotService pivotService,
        IRmCheckService rmCheckService,
        ICrmService crmService,
        ILogger<ReportService> logger)
    {
        _db = db;
        _pivotService = pivotService;
        _rmCheckService = rmCheckService;
        _crmService = crmService;
        _logger = logger;
    }

    public async Task<Result<ReportResultDto>> GenerateReportAsync(ReportRequest request)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var project = await _db.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == request.ProjectId);

            if (project == null)
                return Result<ReportResultDto>.Fail("Project not found");

            byte[] data;
            string fileName;
            string contentType;

            switch (request.Format)
            {
                case ReportFormat.Excel:
                    var excelResult = await GenerateExcelReportAsync(request);
                    if (!excelResult.Succeeded)
                        return Result<ReportResultDto>.Fail(excelResult.Messages.FirstOrDefault() ?? "Failed");
                    data = excelResult.Data!;
                    fileName = $"{project.ProjectName}_{request.ReportType}_{DateTime.Now:yyyyMMdd}.xlsx";
                    contentType = ExcelContentType;
                    break;

                case ReportFormat.Csv:
                    var csvResult = await ExportToCsvAsync(request.ProjectId, request.Options?.UseOxide ?? false);
                    if (!csvResult.Succeeded)
                        return Result<ReportResultDto>.Fail(csvResult.Messages.FirstOrDefault() ?? "Failed");
                    data = csvResult.Data!;
                    fileName = $"{project.ProjectName}_{DateTime.Now:yyyyMMdd}.csv";
                    contentType = "text/csv";
                    break;

                case ReportFormat.Json:
                    var jsonResult = await ExportToJsonAsync(request.ProjectId);
                    if (!jsonResult.Succeeded)
                        return Result<ReportResultDto>.Fail(jsonResult.Messages.FirstOrDefault() ?? "Failed");
                    data = jsonResult.Data!;
                    fileName = $"{project.ProjectName}_{DateTime.Now:yyyyMMdd}.json";
                    contentType = "application/json";
                    break;

                case ReportFormat.Html:
                    var htmlResult = await GenerateHtmlReportAsync(request.ProjectId, request.Options);
                    if (!htmlResult.Succeeded)
                        return Result<ReportResultDto>.Fail(htmlResult.Messages.FirstOrDefault() ?? "Failed");
                    data = Encoding.UTF8.GetBytes(htmlResult.Data!);
                    fileName = $"{project.ProjectName}_{DateTime.Now:yyyyMMdd}.html";
                    contentType = "text/html";
                    break;

                default:
                    return Result<ReportResultDto>.Fail("Unsupported format");
            }

            stopwatch.Stop();

            // Get row/column counts
            var pivotResult = await _pivotService.GetPivotTableAsync(new PivotRequest(request.ProjectId, PageSize: 1));
            var totalRows = pivotResult.Succeeded ? pivotResult.Data!.TotalCount : 0;
            var totalColumns = pivotResult.Succeeded ? pivotResult.Data!.Columns.Count : 0;

            var metadata = new ReportMetadataDto(
                request.ProjectId,
                project.ProjectName,
                request.ReportType,
                request.Format,
                totalRows,
                totalColumns,
                stopwatch.Elapsed
            );

            return Result<ReportResultDto>.Success(new ReportResultDto(
                fileName,
                contentType,
                data,
                DateTime.UtcNow,
                metadata
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate report for project {ProjectId}", request.ProjectId);
            return Result<ReportResultDto>.Fail($"Failed to generate report: {ex.Message}");
        }
    }

    public async Task<Result<byte[]>> ExportDataAsync(ExportRequest request)
    {
        return request.Format switch
        {
            ReportFormat.Excel => await ExportToExcelAsync(request.ProjectId, new ReportOptions(
                UseOxide: request.UseOxide,
                DecimalPlaces: request.DecimalPlaces,
                SelectedElements: request.SelectedElements
            )),
            ReportFormat.Csv => await ExportToCsvAsync(request.ProjectId, request.UseOxide),
            ReportFormat.Json => await ExportToJsonAsync(request.ProjectId),
            _ => Result<byte[]>.Fail("Unsupported format")
        };
    }

    public async Task<Result<byte[]>> ExportToExcelAsync(Guid projectId, ReportOptions? options = null)
    {
        try
        {
            options ??= new ReportOptions();

            var project = await _db.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);

            if (project == null)
                return Result<byte[]>.Fail("Project not found");

            using var workbook = new XLWorkbook();

            // 1. Summary Sheet
            if (options.IncludeSummary)
            {
                await AddSummarySheet(workbook, project, projectId);
            }

            // 2. Raw Data Sheet
            if (options.IncludeRawData)
            {
                await AddPivotDataSheet(workbook, projectId, options);
            }

            // 3. Statistics Sheet
            if (options.IncludeStatistics)
            {
                await AddStatisticsSheet(workbook, projectId);
            }

            // 4. RM Check Sheet
            if (options.IncludeRmCheck)
            {
                await AddRmCheckSheet(workbook, projectId);
            }

            // 5.  Duplicates Sheet
            if (options.IncludeDuplicates)
            {
                await AddDuplicatesSheet(workbook, projectId);
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return Result<byte[]>.Success(stream.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export to Excel for project {ProjectId}", projectId);
            return Result<byte[]>.Fail($"Failed to export to Excel: {ex.Message}");
        }
    }

    public async Task<Result<byte[]>> ExportToCsvAsync(Guid projectId, bool useOxide = false)
    {
        try
        {
            var pivotResult = await _pivotService.GetPivotTableAsync(new PivotRequest(
                projectId,
                UseOxide: useOxide,
                PageSize: int.MaxValue
            ));

            if (!pivotResult.Succeeded)
                return Result<byte[]>.Fail(pivotResult.Messages.FirstOrDefault() ?? "Failed to get data");

            var pivot = pivotResult.Data!;
            var sb = new StringBuilder();

            // Header
            sb.AppendLine("Solution Label," + string.Join(",", pivot.Columns));

            // Rows
            foreach (var row in pivot.Rows)
            {
                var values = pivot.Columns.Select(c =>
                    row.Values.TryGetValue(c, out var v) && v.HasValue
                        ? v.Value.ToString()
                        : "");
                sb.AppendLine($"\"{row.SolutionLabel}\",{string.Join(",", values)}");
            }

            return Result<byte[]>.Success(Encoding.UTF8.GetBytes(sb.ToString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export to CSV for project {ProjectId}", projectId);
            return Result<byte[]>.Fail($"Failed to export to CSV: {ex.Message}");
        }
    }

    public async Task<Result<byte[]>> ExportToJsonAsync(Guid projectId)
    {
        try
        {
            var pivotResult = await _pivotService.GetPivotTableAsync(new PivotRequest(
                projectId,
                PageSize: int.MaxValue
            ));

            if (!pivotResult.Succeeded)
                return Result<byte[]>.Fail(pivotResult.Messages.FirstOrDefault() ?? "Failed to get data");

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(pivotResult.Data, jsonOptions);
            return Result<byte[]>.Success(Encoding.UTF8.GetBytes(json));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export to JSON for project {ProjectId}", projectId);
            return Result<byte[]>.Fail($"Failed to export to JSON: {ex.Message}");
        }
    }

    public async Task<Result<string>> GenerateHtmlReportAsync(Guid projectId, ReportOptions? options = null)
    {
        try
        {
            options ??= new ReportOptions();

            var project = await _db.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);

            if (project == null)
                return Result<string>.Fail("Project not found");

            var pivotResult = await _pivotService.GetPivotTableAsync(new PivotRequest(
                projectId,
                UseOxide: options.UseOxide,
                DecimalPlaces: options.DecimalPlaces,
                PageSize: int.MaxValue
            ));

            if (!pivotResult.Succeeded)
                return Result<string>.Fail(pivotResult.Messages.FirstOrDefault() ?? "Failed");

            var pivot = pivotResult.Data!;

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset=\"UTF-8\">");
            html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.AppendLine($"    <title>{options.Title ?? project.ProjectName} - Report</title>");
            html.AppendLine("    <style>");
            html.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; }");
            html.AppendLine("        h1 { color: #333; }");
            html.AppendLine("        table { border-collapse: collapse; width: 100%; margin-top: 20px; }");
            html.AppendLine("        th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
            html.AppendLine("        th { background-color: #4CAF50; color: white; }");
            html.AppendLine("        tr:nth-child(even) { background-color: #f2f2f2; }");
            html.AppendLine("        tr:hover { background-color: #ddd; }");
            html.AppendLine("        .summary { background-color: #f9f9f9; padding: 15px; border-radius: 5px; margin-bottom: 20px; }");
            html.AppendLine("        .stats { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; }");
            html.AppendLine("        .stat-box { background: #e3f2fd; padding: 10px; border-radius: 5px; text-align: center; }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");

            // Header
            html.AppendLine($"<h1>{options.Title ?? project.ProjectName}</h1>");
            html.AppendLine($"<p>Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
            if (!string.IsNullOrWhiteSpace(options.Author))
                html.AppendLine($"<p>Author: {options.Author}</p>");

            // Summary
            if (options.IncludeSummary)
            {
                html.AppendLine("<div class=\"summary\">");
                html.AppendLine("<h2>Summary</h2>");
                html.AppendLine("<div class=\"stats\">");
                html.AppendLine($"<div class=\"stat-box\"><strong>Total Rows</strong><br>{pivot.TotalCount}</div>");
                html.AppendLine($"<div class=\"stat-box\"><strong>Total Columns</strong><br>{pivot.Columns.Count}</div>");
                html.AppendLine($"<div class=\"stat-box\"><strong>Solution Labels</strong><br>{pivot.Metadata.AllSolutionLabels.Count}</div>");
                html.AppendLine($"<div class=\"stat-box\"><strong>Elements</strong><br>{pivot.Metadata.AllElements.Count}</div>");
                html.AppendLine("</div>");
                html.AppendLine("</div>");
            }

            // Statistics
            if (options.IncludeStatistics && pivot.Metadata.ColumnStats.Any())
            {
                html.AppendLine("<h2>Statistics</h2>");
                html.AppendLine("<table>");
                html.AppendLine("<tr><th>Element</th><th>Min</th><th>Max</th><th>Mean</th><th>Std Dev</th><th>Count</th></tr>");
                foreach (var stat in pivot.Metadata.ColumnStats)
                {
                    html.AppendLine($"<tr><td>{stat.Key}</td><td>{stat.Value.Min}</td><td>{stat.Value.Max}</td><td>{stat.Value.Mean}</td><td>{stat.Value.StdDev}</td><td>{stat.Value.NonNullCount}</td></tr>");
                }
                html.AppendLine("</table>");
            }

            // Data Table
            if (options.IncludeRawData)
            {
                html.AppendLine("<h2>Data</h2>");
                html.AppendLine("<table>");
                html.AppendLine("<tr><th>Solution Label</th>");
                foreach (var col in pivot.Columns)
                {
                    html.AppendLine($"<th>{col}</th>");
                }
                html.AppendLine("</tr>");

                foreach (var row in pivot.Rows)
                {
                    html.AppendLine($"<tr><td>{row.SolutionLabel}</td>");
                    foreach (var col in pivot.Columns)
                    {
                        var value = row.Values.TryGetValue(col, out var v) && v.HasValue ? v.Value.ToString() : "";
                        html.AppendLine($"<td>{value}</td>");
                    }
                    html.AppendLine("</tr>");
                }
                html.AppendLine("</table>");
            }

            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return Result<string>.Success(html.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate HTML report for project {ProjectId}", projectId);
            return Result<string>.Fail($"Failed to generate HTML report: {ex.Message}");
        }
    }

    #region Private Excel Sheet Helpers

    private async Task AddSummarySheet(XLWorkbook workbook, Domain.Entities.Project project, Guid projectId)
    {
        var ws = workbook.Worksheets.Add("Summary");

        ws.Cell(1, 1).Value = "Project Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;

        ws.Cell(3, 1).Value = "Project Name:";
        ws.Cell(3, 2).Value = project.ProjectName;

        ws.Cell(4, 1).Value = "Generated:";
        ws.Cell(4, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        ws.Cell(5, 1).Value = "Owner:";
        ws.Cell(5, 2).Value = project.Owner ?? "N/A";

        // Get stats
        var pivotResult = await _pivotService.GetPivotTableAsync(new PivotRequest(projectId, PageSize: 1));
        if (pivotResult.Succeeded)
        {
            ws.Cell(7, 1).Value = "Total Rows:";
            ws.Cell(7, 2).Value = pivotResult.Data!.TotalCount;

            ws.Cell(8, 1).Value = "Total Columns:";
            ws.Cell(8, 2).Value = pivotResult.Data!.Columns.Count;

            ws.Cell(9, 1).Value = "Solution Labels:";
            ws.Cell(9, 2).Value = pivotResult.Data!.Metadata.AllSolutionLabels.Count;
        }

        ws.Columns().AdjustToContents();
    }

    private async Task AddPivotDataSheet(XLWorkbook workbook, Guid projectId, ReportOptions options)
    {
        var pivotResult = await _pivotService.GetPivotTableAsync(new PivotRequest(
            projectId,
            UseOxide: options.UseOxide,
            DecimalPlaces: options.DecimalPlaces,
            SelectedElements: options.SelectedElements,
            PageSize: int.MaxValue
        ));

        if (!pivotResult.Succeeded) return;

        var pivot = pivotResult.Data!;
        var ws = workbook.Worksheets.Add("Data");

        // Header
        ws.Cell(1, 1).Value = "Solution Label";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.LightGreen;

        for (int i = 0; i < pivot.Columns.Count; i++)
        {
            ws.Cell(1, i + 2).Value = pivot.Columns[i];
            ws.Cell(1, i + 2).Style.Font.Bold = true;
            ws.Cell(1, i + 2).Style.Fill.BackgroundColor = XLColor.LightGreen;
        }

        // Data
        for (int row = 0; row < pivot.Rows.Count; row++)
        {
            ws.Cell(row + 2, 1).Value = pivot.Rows[row].SolutionLabel;

            for (int col = 0; col < pivot.Columns.Count; col++)
            {
                if (pivot.Rows[row].Values.TryGetValue(pivot.Columns[col], out var value) && value.HasValue)
                {
                    ws.Cell(row + 2, col + 2).Value = (double)value.Value;
                }
            }
        }

        ws.Columns().AdjustToContents();
    }

    private async Task AddStatisticsSheet(XLWorkbook workbook, Guid projectId)
    {
        var statsResult = await _pivotService.GetColumnStatsAsync(projectId);
        if (!statsResult.Succeeded) return;

        var ws = workbook.Worksheets.Add("Statistics");

        // Header
        var headers = new[] { "Element", "Min", "Max", "Mean", "Std Dev", "Count" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
        }

        // Data
        int row = 2;
        foreach (var stat in statsResult.Data!)
        {
            ws.Cell(row, 1).Value = stat.Key;
            ws.Cell(row, 2).Value = stat.Value.Min.HasValue ? (double)stat.Value.Min.Value : 0;
            ws.Cell(row, 3).Value = stat.Value.Max.HasValue ? (double)stat.Value.Max.Value : 0;
            ws.Cell(row, 4).Value = stat.Value.Mean.HasValue ? (double)stat.Value.Mean.Value : 0;
            ws.Cell(row, 5).Value = stat.Value.StdDev.HasValue ? (double)stat.Value.StdDev.Value : 0;
            ws.Cell(row, 6).Value = stat.Value.NonNullCount;
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private async Task AddRmCheckSheet(XLWorkbook workbook, Guid projectId)
    {
        var rmResult = await _rmCheckService.CheckRmAsync(new RmCheckRequest(projectId));
        if (!rmResult.Succeeded || !rmResult.Data!.Results.Any()) return;

        var ws = workbook.Worksheets.Add("RM Check");

        // Header
        var headers = new[] { "Solution Label", "Matched RM", "Analysis Method", "Status", "Pass", "Fail", "Total" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightYellow;
        }

        // Data
        int row = 2;
        foreach (var result in rmResult.Data!.Results)
        {
            ws.Cell(row, 1).Value = result.SolutionLabel;
            ws.Cell(row, 2).Value = result.MatchedRmId;
            ws.Cell(row, 3).Value = result.AnalysisMethod;
            ws.Cell(row, 4).Value = result.Status.ToString();
            ws.Cell(row, 5).Value = result.PassCount;
            ws.Cell(row, 6).Value = result.FailCount;
            ws.Cell(row, 7).Value = result.TotalCount;

            // Color coding
            if (result.Status == RmCheckStatus.Fail)
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.LightPink;
            else if (result.Status == RmCheckStatus.Pass)
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.LightGreen;

            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private async Task AddDuplicatesSheet(XLWorkbook workbook, Guid projectId)
    {
        var dupResult = await _pivotService.DetectDuplicatesAsync(new DuplicateDetectionRequest(projectId));
        if (!dupResult.Succeeded || !dupResult.Data!.Any()) return;

        var ws = workbook.Worksheets.Add("Duplicates");

        // Header
        var headers = new[] { "Main Label", "Duplicate Label", "Has Out of Range", "Elements Checked" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.Gold;
        }

        // Data
        int row = 2;
        foreach (var dup in dupResult.Data!)
        {
            ws.Cell(row, 1).Value = dup.MainSolutionLabel;
            ws.Cell(row, 2).Value = dup.DuplicateSolutionLabel;
            ws.Cell(row, 3).Value = dup.HasOutOfRangeDiff ? "Yes" : "No";
            ws.Cell(row, 4).Value = dup.Differences.Count;

            if (dup.HasOutOfRangeDiff)
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.LightPink;

            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private async Task<Result<byte[]>> GenerateExcelReportAsync(ReportRequest request)
    {
        return await ExportToExcelAsync(request.ProjectId, request.Options);
    }

    #endregion

    #region Best Wavelength Selection (Python-compatible)

    /// <summary>
    /// Calculate calibration ranges for each element/wavelength
    /// Based on Python report.py logic (lines 310-323):
    /// std_data = original_df[(original_df['Type'] == 'Std') & (original_df['Element'] == element_name)][concentration_column]
    /// Uses STD (Standard) type rows, NOT Blk (Blank)!
    /// </summary>
    public async Task<Result<Dictionary<string, CalibrationRange>>> GetCalibrationRangesAsync(Guid projectId)
    {
        try
        {
            var rawRows = await _db.RawDataRows
                .AsNoTracking()
                .Where(r => r.ProjectId == projectId)
                .ToListAsync();

            if (!rawRows.Any())
                return Result<Dictionary<string, CalibrationRange>>.Fail("No data found for project");

            var calibrationRanges = new Dictionary<string, CalibrationRange>();
            var elementValues = new Dictionary<string, List<decimal>>();

            // Python equivalent (report.py lines 310-312):
            // std_data = original_df[(original_df['Type'] == 'Std') & (original_df['Element'] == element_name)][concentration_column]
            // Uses STD type rows for calibration ranges
            foreach (var row in rawRows)
            {
                try
                {
                    using var doc = JsonDocument.Parse(row.ColumnData);
                    var root = doc.RootElement;

                    // Check if this is a STD (Standard) row - NOT Blk!
                    // Python: original_df['Type'] == 'Std'
                    if (!root.TryGetProperty("Type", out var typeElement))
                        continue;
                    
                    var type = typeElement.GetString();
                    if (type != "Std" && type != "Standard")
                        continue;

                    // Get Element name
                    if (!root.TryGetProperty("Element", out var elementElement))
                        continue;
                    
                    var element = elementElement.GetString();
                    if (string.IsNullOrEmpty(element))
                        continue;

                    // Get concentration value (Soln Conc or Corr Con)
                    // Python: concentration_column = self.get_concentration_column(original_df)
                    decimal? concValue = null;
                    
                    // Try Soln Conc first, then Corr Con as fallback
                    if (root.TryGetProperty("Soln Conc", out var solnConcElement))
                    {
                        if (solnConcElement.ValueKind == JsonValueKind.Number)
                            concValue = solnConcElement.GetDecimal();
                        else if (solnConcElement.ValueKind == JsonValueKind.String &&
                                 decimal.TryParse(solnConcElement.GetString(), out var parsed))
                            concValue = parsed;
                    }
                    
                    if (!concValue.HasValue && root.TryGetProperty("Corr Con", out var corrConElement))
                    {
                        if (corrConElement.ValueKind == JsonValueKind.Number)
                            concValue = corrConElement.GetDecimal();
                        else if (corrConElement.ValueKind == JsonValueKind.String &&
                                 decimal.TryParse(corrConElement.GetString(), out var parsed))
                            concValue = parsed;
                    }

                    // Python filters: str(x).replace('.', '', 1).isdigit() - positive numbers only
                    if (concValue.HasValue && concValue.Value > 0)
                    {
                        if (!elementValues.ContainsKey(element))
                            elementValues[element] = new List<decimal>();
                        elementValues[element].Add(concValue.Value);
                    }
                }
                catch (JsonException)
                {
                    continue;
                }
            }

            // Calculate min/max for each element
            foreach (var (element, values) in elementValues)
            {
                if (!values.Any()) continue;

                var min = values.Min();
                var max = values.Max();
                calibrationRanges[element] = new CalibrationRange(
                    element,
                    min,
                    max,
                    $"[{min:F2} to {max:F2}]"
                );
            }

            _logger.LogInformation("Calculated calibration ranges for {Count} elements in project {ProjectId}",
                calibrationRanges.Count, projectId);

            return Result<Dictionary<string, CalibrationRange>>.Success(calibrationRanges);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate calibration ranges for project {ProjectId}", projectId);
            return Result<Dictionary<string, CalibrationRange>>.Fail($"Failed to calculate calibration ranges: {ex.Message}");
        }
    }

    /// <summary>
    /// Select best wavelength for each base element per row
    /// Based on Python report.py select_best_wavelength_for_row()
    /// 
    /// Algorithm:
    /// 1. Group elements by base name (e.g., "Fe 239.562", "Fe 238.204" → base = "Fe")
    /// 2. For each row and base element, select the wavelength where:
    ///    a. Concentration is within calibration range (preferred)
    ///    b. If none in range, select the one closest to the range
    /// </summary>
    public async Task<Result<BestWavelengthResult>> SelectBestWavelengthsAsync(BestWavelengthRequest request)
    {
        try
        {
            // Get calibration ranges first
            var calRangesResult = await GetCalibrationRangesAsync(request.ProjectId);
            if (!calRangesResult.Succeeded)
                return Result<BestWavelengthResult>.Fail(calRangesResult.Messages.FirstOrDefault() ?? "Failed to get calibration ranges");

            var calibrationRanges = calRangesResult.Data!;

            // Get pivot data
            var pivotRequest = new AdvancedPivotRequest(
                request.ProjectId,
                SelectedSolutionLabels: request.SelectedSolutionLabels,
                PageSize: 10000
            );
            var pivotResult = await _pivotService.GetAdvancedPivotTableAsync(pivotRequest);
            if (!pivotResult.Succeeded)
                return Result<BestWavelengthResult>.Fail(pivotResult.Messages.FirstOrDefault() ?? "Failed to get pivot data");

            var pivotData = pivotResult.Data!;

            // Group elements by base name (extract base element from column names)
            var baseElements = GroupElementsByBase(pivotData.Columns);

            // Get concentration data from raw rows
            var concentrations = await GetConcentrationData(request.ProjectId, request.UseConcentration);

            // Select best wavelength for each row and base element
            var bestWavelengthsPerRow = new Dictionary<int, Dictionary<string, string>>();
            var selectedColumns = new List<string> { "Solution Label" };

            for (int rowIndex = 0; rowIndex < pivotData.Rows.Count; rowIndex++)
            {
                var row = pivotData.Rows[rowIndex];
                bestWavelengthsPerRow[rowIndex] = new Dictionary<string, string>();

                foreach (var (baseElement, wavelengths) in baseElements)
                {
                    var bestWavelength = SelectBestWavelengthForRow(
                        row.SolutionLabel,
                        baseElement,
                        wavelengths,
                        calibrationRanges,
                        concentrations
                    );

                    if (bestWavelength != null)
                    {
                        bestWavelengthsPerRow[rowIndex][baseElement] = bestWavelength;
                        if (!selectedColumns.Contains(bestWavelength))
                            selectedColumns.Add(bestWavelength);
                    }
                }
            }

            var result = new BestWavelengthResult(
                calibrationRanges,
                bestWavelengthsPerRow,
                baseElements,
                selectedColumns
            );

            _logger.LogInformation("Selected best wavelengths for {RowCount} rows, {BaseCount} base elements",
                pivotData.Rows.Count, baseElements.Count);

            return Result<BestWavelengthResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to select best wavelengths for project {ProjectId}", request.ProjectId);
            return Result<BestWavelengthResult>.Fail($"Failed to select best wavelengths: {ex.Message}");
        }
    }

    /// <summary>
    /// Group element columns by base element name
    /// E.g., "Fe 239.562", "Fe 238.204" → "Fe" → ["Fe 239.562", "Fe 238.204"]
    /// </summary>
    private Dictionary<string, List<string>> GroupElementsByBase(List<string> columns)
    {
        var baseElements = new Dictionary<string, List<string>>();

        foreach (var col in columns)
        {
            if (col == "Solution Label") continue;

            // Extract base element (first word before space or number)
            var parts = col.Split(' ', '_');
            var baseElement = parts[0];

            // Remove trailing _1, _2 if present (from repeats)
            if (baseElement.Contains("_"))
            {
                var underscoreIndex = baseElement.LastIndexOf('_');
                if (underscoreIndex > 0 && int.TryParse(baseElement[(underscoreIndex + 1)..], out _))
                {
                    baseElement = baseElement[..underscoreIndex];
                }
            }

            if (!baseElements.ContainsKey(baseElement))
                baseElements[baseElement] = new List<string>();

            baseElements[baseElement].Add(col);
        }

        return baseElements;
    }

    /// <summary>
    /// Get concentration data (Soln Conc or Corr Con) for all samples
    /// </summary>
    private async Task<Dictionary<(string SolutionLabel, string Element), decimal>> GetConcentrationData(
        Guid projectId, bool useSolnConc)
    {
        var result = new Dictionary<(string SolutionLabel, string Element), decimal>();
        var columnName = useSolnConc ? "Soln Conc" : "Corr Con";

        var rawRows = await _db.RawDataRows
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .ToListAsync();

        foreach (var row in rawRows)
        {
            try
            {
                using var doc = JsonDocument.Parse(row.ColumnData);
                var root = doc.RootElement;

                // Only process Samp type
                if (root.TryGetProperty("Type", out var typeElement) &&
                    typeElement.GetString() != "Samp" && typeElement.GetString() != "Sample")
                    continue;

                if (!root.TryGetProperty("Solution Label", out var labelElement))
                    continue;
                var solutionLabel = labelElement.GetString();

                if (!root.TryGetProperty("Element", out var elementElement))
                    continue;
                var element = elementElement.GetString();

                if (string.IsNullOrEmpty(solutionLabel) || string.IsNullOrEmpty(element))
                    continue;

                if (!root.TryGetProperty(columnName, out var concElement))
                    continue;

                decimal? conc = null;
                if (concElement.ValueKind == JsonValueKind.Number)
                    conc = concElement.GetDecimal();
                else if (concElement.ValueKind == JsonValueKind.String &&
                         decimal.TryParse(concElement.GetString(), out var parsed))
                    conc = parsed;

                if (conc.HasValue)
                {
                    result[(solutionLabel, element)] = conc.Value;
                }
            }
            catch (JsonException)
            {
                continue;
            }
        }

        return result;
    }

    /// <summary>
    /// Select the best wavelength for a base element in a specific row
    /// Based on Python report.py select_best_wavelength_for_row()
    /// </summary>
    private string? SelectBestWavelengthForRow(
        string solutionLabel,
        string baseElement,
        List<string> wavelengths,
        Dictionary<string, CalibrationRange> calibrationRanges,
        Dictionary<(string SolutionLabel, string Element), decimal> concentrations)
    {
        if (!wavelengths.Any())
            return null;

        var validWavelengths = new List<(string Wavelength, decimal Conc, decimal Distance)>();
        var outOfRangeWavelengths = new List<(string Wavelength, decimal Conc, decimal Distance)>();

        foreach (var wl in wavelengths)
        {
            // Get element name (remove _1, _2 suffixes)
            var elementName = wl;
            var underscoreIndex = wl.LastIndexOf('_');
            if (underscoreIndex > 0 && int.TryParse(wl[(underscoreIndex + 1)..], out _))
            {
                elementName = wl[..underscoreIndex];
            }

            // Get concentration for this solution label and element
            if (!concentrations.TryGetValue((solutionLabel, elementName), out var conc))
                continue;

            // Get calibration range
            if (!calibrationRanges.TryGetValue(elementName, out var calRange))
            {
                // No calibration range, consider it valid
                validWavelengths.Add((wl, conc, 0));
                continue;
            }

            // Check if concentration is within calibration range
            if (calRange.Min <= conc && conc <= calRange.Max)
            {
                validWavelengths.Add((wl, conc, 0));
            }
            else
            {
                // Calculate distance from range
                var distance = Math.Min(Math.Abs(conc - calRange.Min), Math.Abs(conc - calRange.Max));
                outOfRangeWavelengths.Add((wl, conc, distance));
            }
        }

        // Select best wavelength
        if (validWavelengths.Count == 1)
        {
            return validWavelengths[0].Wavelength;
        }
        else if (validWavelengths.Any() || outOfRangeWavelengths.Any())
        {
            // Combine and select the one with minimum distance
            var allCandidates = validWavelengths.Concat(outOfRangeWavelengths);
            return allCandidates.OrderBy(x => x.Distance).First().Wavelength;
        }

        return wavelengths.FirstOrDefault();
    }

    #endregion
}