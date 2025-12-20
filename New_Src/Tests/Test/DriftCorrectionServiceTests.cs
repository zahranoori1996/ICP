using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Application.DTOs;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Tests;

public class DriftCorrectionServiceTests : IDisposable
{
    private readonly IsatisDbContext _db;
    private readonly DriftCorrectionService _driftService;
    private readonly Guid _testProjectId;

    public DriftCorrectionServiceTests()
    {
        var options = new DbContextOptionsBuilder<IsatisDbContext>()
            .UseInMemoryDatabase(databaseName: $"DriftTest_{Guid.NewGuid()}")
            .Options;
        _db = new IsatisDbContext(options);

        var logger = new Mock<ILogger<DriftCorrectionService>>();
        var changeLogService = new Mock<IChangeLogService>();  // ← Add

        _driftService = new DriftCorrectionService(_db, changeLogService.Object, logger.Object);  // ← Fix

        _testProjectId = Guid.NewGuid();
        SeedTestData();
    }

    private void SeedTestData()
    {
        var project = new Project
        {
            ProjectId = _testProjectId,
            ProjectName = "Drift Test Project",
            CreatedAt = DateTime.UtcNow
        };
        _db.Projects.Add(project);

        // Create a sequence with drift
        var samples = new (string Label, decimal Fe, decimal Cu)[]
        {
            ("OREAS 258", 100m, 10m),
            ("Sample1", 98m, 9.8m),
            ("Sample2", 97m, 9.7m),
            ("Sample3", 96m, 9.6m),
            ("OREAS 259", 95m, 9.5m),
            ("Sample4", 94m, 9.4m),
            ("Sample5", 93m, 9.3m),
            ("OREAS 260", 90m, 9.0m)
        };

        foreach (var sample in samples)
        {
            var data = new Dictionary<string, object>
            {
                ["Solution Label"] = sample.Label,
                ["Fe"] = sample.Fe,
                ["Cu"] = sample.Cu,
                ["Type"] = sample.Label.StartsWith("OREAS") ? "Std" : "Samp"
            };

            _db.RawDataRows.Add(new RawDataRow
            {
                ProjectId = _testProjectId,
                SampleId = sample.Label,
                ColumnData = JsonSerializer.Serialize(data)
            });
        }

        _db.SaveChanges();
    }

    [Fact]
    public async Task DetectSegmentsAsync_ShouldFindSegmentsBetweenStandards()
    {
        // Act
        var result = await _driftService.DetectSegmentsAsync(_testProjectId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Count);
    }

    [Fact]
    public async Task AnalyzeDriftAsync_ShouldCalculateDriftPercent()
    {
        // Arrange
        var request = new DriftCorrectionRequest(
            _testProjectId,
            null,
            DriftMethod.Linear,
            true,
            null,
            null
        );

        // Act
        var result = await _driftService.AnalyzeDriftAsync(request);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.SegmentsFound >= 1);
        Assert.True(result.Data.ElementDrifts.ContainsKey("Fe"));
    }

    [Fact]
    public async Task ApplyDriftCorrectionAsync_Linear_ShouldCorrectValues()
    {
        // Arrange
        var request = new DriftCorrectionRequest(
            _testProjectId,
            new List<string> { "Fe" },
            DriftMethod.Linear,
            true,
            null,
            null
        );

        // Act
        var result = await _driftService.ApplyDriftCorrectionAsync(request);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.CorrectedSamples > 0);
        Assert.NotEmpty(result.Data.CorrectedData);
    }

    [Fact]
    public async Task ApplyDriftCorrectionAsync_Stepwise_ShouldApplyCorrection()
    {
        // Arrange
        var request = new DriftCorrectionRequest(
            _testProjectId,
            new List<string> { "Fe" },
            DriftMethod.Stepwise,
            true,
            null,
            null
        );

        // Act
        var result = await _driftService.ApplyDriftCorrectionAsync(request);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task CalculateDriftRatiosAsync_ShouldReturnRatios()
    {
        // Act
        var result = await _driftService.CalculateDriftRatiosAsync(_testProjectId, new List<string> { "Fe" });

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("Fe"));
    }

    [Fact]
    public async Task ZeroSlopeAsync_ShouldFlattenTrend()
    {
        // Act
        var result = await _driftService.ZeroSlopeAsync(_testProjectId, "Fe");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(0m, result.Data.NewSlope);
    }

    [Fact]
    public async Task OptimizeSlopeAsync_RotateUp_ShouldWork()
    {
        // Arrange
        var request = new SlopeOptimizationRequest(
            _testProjectId,
            "Fe",
            SlopeAction.RotateUp,
            null
        );

        // Act
        var result = await _driftService.OptimizeSlopeAsync(request);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DetectSegmentsAsync_WithCustomPattern_ShouldWork()
    {
        // Act
        var result = await _driftService.DetectSegmentsAsync(
            _testProjectId,
            basePattern: @"^OREAS",
            conePattern: @"^CAL"
        );

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ApplyDriftCorrectionAsync_WithNoProject_ShouldFail()
    {
        // Arrange
        var request = new DriftCorrectionRequest(
            Guid.NewGuid(),
            null,
            DriftMethod.Linear,
            true,
            null,
            null
        );

        // Act
        var result = await _driftService.ApplyDriftCorrectionAsync(request);

        // Assert
        Assert.False(result.Succeeded);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }
}