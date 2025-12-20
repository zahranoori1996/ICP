using System;
using System.Threading.Tasks;
using Application.DTOs;
using FluentAssertions;
using Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Tests.Helpers;
using Xunit;

namespace Tests.UnitTests;

public class PivotServiceTests
{
    private readonly Mock<ILogger<PivotService>> _loggerMock;

    public PivotServiceTests()
    {
        _loggerMock = new Mock<ILogger<PivotService>>();
    }

    [Fact(DisplayName = "GetPivotTable returns correct data")]
    public async Task GetPivotTable_ReturnsCorrectData()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new PivotService(context, _loggerMock.Object);
        var request = new PivotRequest(TestDbContextFactory.TestProjectId);

        // Act
        var result = await service.GetPivotTableAsync(request);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Rows.Should().HaveCountGreaterThan(0);
        result.Data.Columns.Should().Contain("Fe");
        result.Data.Columns.Should().Contain("Cu");
    }

    [Fact(DisplayName = "GetPivotTable with search filters results")]
    public async Task GetPivotTable_WithSearch_FiltersResults()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new PivotService(context, _loggerMock.Object);
        var request = new PivotRequest(TestDbContextFactory.TestProjectId, SearchText: "OREAS");

        // Act
        var result = await service.GetPivotTableAsync(request);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data!.Rows.Should().HaveCount(1);
        result.Data.Rows[0].SolutionLabel.Should().Contain("OREAS");
    }

    [Fact(DisplayName = "GetPivotTable with pagination works")]
    public async Task GetPivotTable_WithPagination_Works()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new PivotService(context, _loggerMock.Object);
        var request = new PivotRequest(TestDbContextFactory.TestProjectId, Page: 1, PageSize: 2);

        // Act
        var result = await service.GetPivotTableAsync(request);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data!.Rows.Should().HaveCount(2);
        result.Data.Page.Should().Be(1);
        result.Data.PageSize.Should().Be(2);
    }

    [Fact(DisplayName = "GetSolutionLabels returns all labels")]
    public async Task GetSolutionLabels_ReturnsAllLabels()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new PivotService(context, _loggerMock.Object);

        // Act
        var result = await service.GetSolutionLabelsAsync(TestDbContextFactory.TestProjectId);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data.Should().Contain("S1");
        result.Data.Should().Contain("S2");
        result.Data.Should().Contain("OREAS 258");
    }

    [Fact(DisplayName = "GetElements returns element list")]
    public async Task GetElements_ReturnsElementList()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new PivotService(context, _loggerMock.Object);

        // Act
        var result = await service.GetElementsAsync(TestDbContextFactory.TestProjectId);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data.Should().Contain("Fe");
        result.Data.Should().Contain("Cu");
    }

    [Fact(DisplayName = "GetColumnStats returns statistics")]
    public async Task GetColumnStats_ReturnsStatistics()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new PivotService(context, _loggerMock.Object);

        // Act
        var result = await service.GetColumnStatsAsync(TestDbContextFactory.TestProjectId);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data.Should().ContainKey("Fe");
        result.Data!["Fe"].Min.Should().BeGreaterThan(0);
        result.Data["Fe"].Max.Should().BeGreaterThan(result.Data["Fe"].Min ?? 0);
    }

    [Fact(DisplayName = "DetectDuplicates works")]
    public async Task DetectDuplicates_Works()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new PivotService(context, _loggerMock.Object);
        var request = new DuplicateDetectionRequest(TestDbContextFactory.TestProjectId);

        // Act
        var result = await service.DetectDuplicatesAsync(request);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact(DisplayName = "GetPivotTable with invalid projectId fails")]
    public async Task GetPivotTable_InvalidProjectId_Fails()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new PivotService(context, _loggerMock.Object);
        var request = new PivotRequest(Guid.NewGuid());

        // Act
        var result = await service.GetPivotTableAsync(request);

        // Assert
        result.Succeeded.Should().BeFalse();
    }
}