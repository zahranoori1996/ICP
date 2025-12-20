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

public class OptimizationServiceTests : IDisposable
{
    private readonly IsatisDbContext _db;
    private readonly OptimizationService _optimizationService;
    private readonly Guid _testProjectId;

    public OptimizationServiceTests()
    {
        var options = new DbContextOptionsBuilder<IsatisDbContext>()
            .UseInMemoryDatabase(databaseName: $"OptimizationTest_{Guid.NewGuid()}")
            .Options;

        _db = new IsatisDbContext(options);
        var logger = new Mock<ILogger<OptimizationService>>();

        _optimizationService = new OptimizationService(_db, logger.Object);
        _testProjectId = Guid.NewGuid();

        SeedTestData();
    }

    private void SeedTestData()
    {
        var project = new Project
        {
            ProjectId = _testProjectId,
            ProjectName = "Optimization Test Project",
            CreatedAt = DateTime.UtcNow
        };
        _db.Projects.Add(project);

        // Add CRM data
        var crm = new CrmData
        {
            CrmId = "OREAS 258",
            AnalysisMethod = "4-Acid",
            ElementValues = JsonSerializer.Serialize(new Dictionary<string, decimal>
            {
                ["Fe"] = 45.0m,
                ["Cu"] = 0.12m,
                ["Zn"] = 0.05m
            })
        };
        _db.CrmData.Add(crm);

        // Add sample data
        var samples = new (string Label, decimal Fe, decimal Cu, decimal Zn)[]
        {
            ("OREAS 258", 46.5m, 0.125m, 0.052m),
            ("Sample1", 42.3m, 0.11m, 0.048m),
            ("Sample2", 38.7m, 0.095m, 0.042m),
            ("OREAS 258", 46.2m, 0.123m, 0.051m)
        };

        foreach (var sample in samples)
        {
            var data = new Dictionary<string, object>
            {
                ["Solution Label"] = sample.Label,
                ["Fe"] = sample.Fe,
                ["Cu"] = sample.Cu,
                ["Zn"] = sample.Zn,
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
    public async Task OptimizeBlankScaleAsync_ShouldFindOptimalParameters()
    {
        // Arrange
        var request = new BlankScaleOptimizationRequest(
            _testProjectId,
            new List<string> { "Fe" },
            -10m,
            10m,
            50,
            20,
            false,
            null
        );

        // Act
        var result = await _optimizationService.OptimizeBlankScaleAsync(request);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task OptimizeBlankScaleAsync_WithMultipleElements_ShouldWork()
    {
        // Arrange
        var request = new BlankScaleOptimizationRequest(
            _testProjectId,
            new List<string> { "Fe", "Cu" },
            -10m,
            10m,
            50,
            20,
            false,
            null
        );

        // Act
        var result = await _optimizationService.OptimizeBlankScaleAsync(request);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task GetCurrentStatisticsAsync_ShouldReturnStats()
    {
        // Act
        var result = await _optimizationService.GetCurrentStatisticsAsync(_testProjectId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task OptimizeBlankScaleAsync_WithNoData_ShouldFail()
    {
        // Arrange
        var request = new BlankScaleOptimizationRequest(
            Guid.NewGuid(),
            new List<string> { "Fe" },
            -10m,
            10m,
            100,
            20,
            false,
            null
        );

        // Act
        var result = await _optimizationService.OptimizeBlankScaleAsync(request);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task PreviewBlankScaleAsync_ShouldReturnPreview()
    {
        // Arrange
        var request = new ManualBlankScaleRequest(
            _testProjectId,
            "Fe",
            0.5m,
            1.02m
        );

        // Act
        var result = await _optimizationService.PreviewBlankScaleAsync(request);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }
}