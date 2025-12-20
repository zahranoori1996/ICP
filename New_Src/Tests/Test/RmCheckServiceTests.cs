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

public class RmCheckServiceTests
{
    private readonly Mock<ILogger<RmCheckService>> _loggerMock;

    public RmCheckServiceTests()
    {
        _loggerMock = new Mock<ILogger<RmCheckService>>();
    }

    [Fact(DisplayName = "GetRmSamples finds RM samples")]
    public async Task GetRmSamples_FindsRmSamples()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new RmCheckService(context, _loggerMock.Object);

        // Act
        var result = await service.GetRmSamplesAsync(TestDbContextFactory.TestProjectId);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data.Should().Contain("OREAS 258");
    }

    [Fact(DisplayName = "CheckRm validates RM against CRM")]
    public async Task CheckRm_ValidatesRmAgainstCrm()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new RmCheckService(context, _loggerMock.Object);
        var request = new RmCheckRequest(TestDbContextFactory.TestProjectId);

        // Act
        var result = await service.CheckRmAsync(request);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.TotalRmSamples.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact(DisplayName = "CheckRm with custom threshold")]
    public async Task CheckRm_WithCustomThreshold()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new RmCheckService(context, _loggerMock.Object);
        var request = new RmCheckRequest(
            TestDbContextFactory.TestProjectId,
            MinDiffPercent: -5m,
            MaxDiffPercent: 5m
        );

        // Act
        var result = await service.CheckRmAsync(request);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact(DisplayName = "CheckWeightVolume validates weight and volume")]
    public async Task CheckWeightVolume_ValidatesWeightAndVolume()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new RmCheckService(context, _loggerMock.Object);
        var request = new WeightVolumeCheckRequest(TestDbContextFactory.TestProjectId);

        // Act
        var result = await service.CheckWeightVolumeAsync(request);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data.Should().NotBeNull();
    }

    [Fact(DisplayName = "GetRmSamples with invalid project fails")]
    public async Task GetRmSamples_InvalidProject_Fails()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateWithData();
        var service = new RmCheckService(context, _loggerMock.Object);

        // Act
        var result = await service.GetRmSamplesAsync(Guid.NewGuid());

        // Assert
        result.Succeeded.Should().BeFalse();
    }
}