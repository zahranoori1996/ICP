using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

public class ImportServiceTests : IDisposable
{
    private readonly IsatisDbContext _db;
    private readonly ImportService _importService;
    private readonly Mock<IProjectPersistenceService> _persistenceMock;

    public ImportServiceTests()
    {
        var options = new DbContextOptionsBuilder<IsatisDbContext>()
            .UseInMemoryDatabase(databaseName: $"ImportTest_{Guid.NewGuid()}")
            .Options;

        _db = new IsatisDbContext(options);
        _persistenceMock = new Mock<IProjectPersistenceService>();
        var logger = new Mock<ILogger<ImportService>>();

        // Setup mock to return success
        _persistenceMock
            .Setup(x => x.SaveProjectAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<List<RawDataDto>>(),
                It.IsAny<string?>()))
            .ReturnsAsync((Guid id, string name, string? owner, List<RawDataDto> data, string? state) =>
                Shared.Wrapper.Result<ProjectSaveResult>.Success(new ProjectSaveResult(id == Guid.Empty ? Guid.NewGuid() : id)));

        _importService = new ImportService(_persistenceMock.Object, logger.Object);
    }

    [Fact]
    public async Task DetectFormatAsync_TabularCsv_ShouldDetectCorrectly()
    {
        // Arrange
        var csvContent = "Solution Label,Fe,Cu,Zn\nSample1,45. 2,0.12,0.05\nSample2,38.7,0. 15,0.08";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

        // Act
        var result = await _importService.DetectFormatAsync(stream, "test.csv");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task ImportCsvAsync_ValidCsv_ShouldImportSuccessfully()
    {
        // Arrange
        var csvContent = "Solution Label,Fe,Cu,Zn\nSample1,45.2,0. 12,0.05\nSample2,38.7,0.15,0.08";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

        // Act
        var result = await _importService.ImportCsvAsync(stream, "Test Project", null, null);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotEqual(Guid.Empty, result.Data?.ProjectId ?? Guid.Empty);
    }

    [Fact]
    public async Task ImportCsvAsync_EmptyFile_ShouldFail()
    {
        // Arrange
        var csvContent = "";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

        // Act
        var result = await _importService.ImportCsvAsync(stream, "Empty", null, null);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task PreviewFileAsync_ShouldReturnPreview()
    {
        // Arrange
        var csvContent = "Solution Label,Fe\nSample1,1\nSample2,2\nSample3,3\nSample4,4\nSample5,5";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

        // Act
        var result = await _importService.PreviewFileAsync(stream, "test. csv", 3);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DetectFormatAsync_WithNumericColumns_ShouldIdentifyThem()
    {
        // Arrange
        var csvContent = "Solution Label,Fe,Cu,Notes\nSample1,45.2,0.12,text\nSample2,38.7,0.15,more";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

        // Act
        var result = await _importService.DetectFormatAsync(stream, "test.csv");

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