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

public class CrmServiceTests
{
    private readonly Mock<ILogger<CrmService>> _loggerMock;

    public CrmServiceTests()
    {
        _loggerMock = new Mock<ILogger<CrmService>>();
    }

    [Fact(DisplayName = "GetAnalysisMethods returns unique methods")]
    public async Task GetAnalysisMethods_ReturnsUniqueMethods()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new CrmService(context, _loggerMock.Object);

        // Act
        var result = await service.GetAnalysisMethodsAsync();

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data.Should().Contain("4-Acid Digestion");
        result.Data.Should().Contain("Aqua Regia Digestion");
    }

    [Fact(DisplayName = "CalculateDiff returns diff results")]
    public async Task CalculateDiff_ReturnsDiffResults()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new CrmService(context, _loggerMock.Object);
        var request = new CrmDiffRequest(TestDbContextFactory.TestProjectId);

        // Act
        var result = await service.CalculateDiffAsync(request);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact(DisplayName = "CalculateDiff with thresholds")]
    public async Task CalculateDiff_WithThresholds()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new CrmService(context, _loggerMock.Object);
        var request = new CrmDiffRequest(
            TestDbContextFactory.TestProjectId,
            MinDiffPercent: -5m,
            MaxDiffPercent: 5m
        );

        // Act
        var result = await service.CalculateDiffAsync(request);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact(DisplayName = "CalculateDiff with invalid project fails")]
    public async Task CalculateDiff_InvalidProject_Fails()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new CrmService(context, _loggerMock.Object);
        var request = new CrmDiffRequest(Guid.NewGuid());

        // Act
        var result = await service.CalculateDiffAsync(request);

        // Assert
        result.Succeeded.Should().BeFalse();
    }
}