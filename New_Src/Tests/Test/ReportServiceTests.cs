using System;
using System.Threading.Tasks;
using Application.DTOs;
using FluentAssertions;
using Infrastructure.Services;
using Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Moq;
using Tests.Helpers;
using Xunit;

namespace Tests.UnitTests;

public class ReportServiceTests
{
    private readonly Mock<ILogger<ReportService>> _loggerMock;
    private readonly Mock<ILogger<PivotService>> _pivotLoggerMock;
    private readonly Mock<ILogger<RmCheckService>> _rmLoggerMock;
    private readonly Mock<ILogger<CrmService>> _crmLoggerMock;

    public ReportServiceTests()
    {
        _loggerMock = new Mock<ILogger<ReportService>>();
        _pivotLoggerMock = new Mock<ILogger<PivotService>>();
        _rmLoggerMock = new Mock<ILogger<RmCheckService>>();
        _crmLoggerMock = new Mock<ILogger<CrmService>>();
    }

    private (ReportService service, IsatisDbContext context) CreateService()
    {
        var context = TestDbContextFactory.CreateWithData();
        var pivotService = new PivotService(context, _pivotLoggerMock.Object);
        var rmCheckService = new RmCheckService(context, _rmLoggerMock.Object);
        var crmService = new CrmService(context, _crmLoggerMock.Object);

        var service = new ReportService(context, pivotService, rmCheckService, crmService, _loggerMock.Object);
        return (service, context);
    }

    [Fact(DisplayName = "ExportToCsv generates valid CSV")]
    public async Task ExportToCsv_GeneratesValidCsv()
    {
        var (service, context) = CreateService();
        using (context)
        {
            var result = await service.ExportToCsvAsync(TestDbContextFactory.TestProjectId);

            result.Succeeded.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Length.Should().BeGreaterThan(0);

            var csv = System.Text.Encoding.UTF8.GetString(result.Data);
            csv.Should().Contain("Solution Label");
            csv.Should().Contain("Fe");
        }
    }

    [Fact(DisplayName = "ExportToJson generates valid JSON")]
    public async Task ExportToJson_GeneratesValidJson()
    {
        var (service, context) = CreateService();
        using (context)
        {
            var result = await service.ExportToJsonAsync(TestDbContextFactory.TestProjectId);

            result.Succeeded.Should().BeTrue();
            result.Data.Should().NotBeNull();

            var json = System.Text.Encoding.UTF8.GetString(result.Data!);
            json.Should().Contain("columns");
            json.Should().Contain("rows");
        }
    }

    [Fact(DisplayName = "GenerateHtmlReport generates valid HTML")]
    public async Task GenerateHtmlReport_GeneratesValidHtml()
    {
        var (service, context) = CreateService();
        using (context)
        {
            var result = await service.GenerateHtmlReportAsync(TestDbContextFactory.TestProjectId);

            result.Succeeded.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data.Should().Contain("<!DOCTYPE html>");
            result.Data.Should().Contain("<table>");
            result.Data.Should().Contain("Fe");
        }
    }

    [Fact(DisplayName = "ExportToExcel generates Excel file")]
    public async Task ExportToExcel_GeneratesExcelFile()
    {
        var (service, context) = CreateService();
        using (context)
        {
            var result = await service.ExportToExcelAsync(TestDbContextFactory.TestProjectId);

            result.Succeeded.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Length.Should().BeGreaterThan(0);

            // Excel files start with PK (ZIP format)
            result.Data[0].Should().Be(0x50); // 'P'
            result.Data[1].Should().Be(0x4B); // 'K'
        }
    }

    [Fact(DisplayName = "GenerateReport with HTML format works")]
    public async Task GenerateReport_HtmlFormat_Works()
    {
        var (service, context) = CreateService();
        using (context)
        {
            var request = new ReportRequest(
                TestDbContextFactory.TestProjectId,
                ReportType.Full,
                ReportFormat.Html,
                new ReportOptions(
                    IncludeSummary: true,
                    IncludeRawData: true,
                    IncludeStatistics: true,
                    Title: "Custom Report",
                    Author: "Test Author"
                )
            );

            var result = await service.GenerateReportAsync(request);

            result.Succeeded.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.FileName.Should().EndWith(".html");
        }
    }

    [Fact(DisplayName = "GenerateReport with Excel format works")]
    public async Task GenerateReport_ExcelFormat_Works()
    {
        var (service, context) = CreateService();
        using (context)
        {
            var request = new ReportRequest(
                TestDbContextFactory.TestProjectId,
                ReportType.Full,
                ReportFormat.Excel
            );

            var result = await service.GenerateReportAsync(request);

            result.Succeeded.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.FileName.Should().EndWith(".xlsx");
            result.Data.ContentType.Should().Contain("spreadsheet");
        }
    }

    [Fact(DisplayName = "GenerateReport with CSV format works")]
    public async Task GenerateReport_CsvFormat_Works()
    {
        var (service, context) = CreateService();
        using (context)
        {
            var request = new ReportRequest(
                TestDbContextFactory.TestProjectId,
                ReportType.Full,
                ReportFormat.Csv
            );

            var result = await service.GenerateReportAsync(request);

            result.Succeeded.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.FileName.Should().EndWith(".csv");
        }
    }

    [Fact(DisplayName = "Export with invalid project fails")]
    public async Task Export_InvalidProject_Fails()
    {
        var (service, context) = CreateService();
        using (context)
        {
            var result = await service.ExportToCsvAsync(Guid.NewGuid());

            result.Succeeded.Should().BeFalse();
        }
    }

    [Fact(DisplayName = "GenerateReport with invalid project fails")]
    public async Task GenerateReport_InvalidProject_Fails()
    {
        var (service, context) = CreateService();
        using (context)
        {
            var request = new ReportRequest(
                Guid.NewGuid(),
                ReportType.Full,
                ReportFormat.Html
            );

            var result = await service.GenerateReportAsync(request);

            result.Succeeded.Should().BeFalse();
        }
    }
}