# 🧪 Isatis ICP - Tests

Unit and integration tests for the application.

## 📋 Overview

این پروژه شامل 68 تست برای اطمینان از صحت عملکرد سیستم است. 

## 🚀 Running Tests

```bash
# Run all tests
dotnet test

# Run with verbosity
dotnet test --verbosity normal

# Run specific test class
dotnet test --filter "FullyQualifiedName~CorrectionServiceTests"

# Run with coverage (requires coverlet)
dotnet test --collect:"XPlat Code Coverage"
```

## 📁 Test Files

| File | Tests | Description |
|------|:-----:|-------------|
| `CorrectionServiceTests. cs` | 8 | Weight/Volume corrections |
| `DriftCorrectionServiceTests.cs` | 9 | Drift algorithms |
| `OptimizationServiceTests.cs` | 6 | Blank & Scale optimization |
| `ImportServiceTests.cs` | 6 | CSV/Excel import |
| `ProcessingServiceTests.cs` | 5 | Data processing |
| `CrmServiceTests.cs` | 10 | CRM management |
| `PivotServiceTests. cs` | 8 | Pivot operations |
| `ReportServiceTests.cs` | 6 | Report generation |
| `RmCheckServiceTests. cs` | 5 | RM verification |
| `IntegrationTests.cs` | 5 | API integration |
| **Total** | **68** | ✅ All passing |

## 🛠️ Test Stack

- **xUnit** - Test framework
- **Moq** - Mocking library
- **InMemory Database** - For isolated tests

## 📝 Example Test

```csharp
[Fact]
public async Task FindBadWeightsAsync_ShouldReturnSamplesOutsideRange()
{
    // Arrange
    var request = new FindBadWeightsRequest(_testProjectId, 0. 45m, 0. 55m);

    // Act
    var result = await _correctionService.FindBadWeightsAsync(request);

    // Assert
    Assert.True(result. Succeeded);
    Assert. NotNull(result.Data);
    Assert.Contains(result.Data, b => b.SolutionLabel == "Sample4");
}
```