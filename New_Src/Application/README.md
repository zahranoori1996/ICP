# 📦 Isatis ICP - Application Layer

The **Application Layer** serves as the orchestration core of the Isatis ICP solution. It defines the business logic contracts (Interfaces), data structures (DTOs), and application-specific rules, acting as the bridge between the **Domain** (Core Entities) and the **Infrastructure** (Implementation Details).

## 📋 Overview

This layer enforces **Clean Architecture** principles by having no dependencies on external frameworks, databases, or UI details. It strictly depends on the **Domain** layer.

### Key Responsibilities
- **Service Contracts**: Defines `Interfaces` for all business operations (Import, Processing, Reporting, etc.).
- **Data Transfer**: Defines `DTOs` (Data Transfer Objects) to decouple internal domain entities from the API/UI layer.
- **Dependency Injection**: Provides logic to register application services.

## 📁 Project Structure

```bash
Application/
├── DependencyInjection.cs       # Extension methods for IoC registration (AddApplicationServices)
│
├── DTOs/                        # Data Transfer Objects
│   ├── AdvancedPivotDtos.cs     # Complex pivot table reporting structures
│   ├── CorrectionDtos.cs        # Weight, Volume, and DF correction requests
│   ├── CrmDtos.cs               # Certified Reference Material data & comparisons
│   ├── DriftDTOs.cs             # Drift analysis and correction models
│   ├── ImportDtos.cs            # File import definitions (formats, warnings)
│   ├── OptimizedSampleDto.cs    # Blank/Scale optimization results
│   ├── PivotRequest.cs          # Standard pivot table requests & metadata
│   ├── ReportDtos.cs            # Reporting configurations & export requests
│   └── RmCheckDtos.cs           # Reference Material validation results
│
└── Interface/                   # Service Contracts (Abstractions)
    ├── IChangeLogService.cs     # Change tracking and audit logs
    ├── ICorrectionService.cs    # Data correction business logic
    ├── ICrmService.cs           # CRM management and verification logic
    ├── IDriftCorrectionService.cs # Instrument drift calculation & correction
    ├── IImportQueueService.cs   # Background job queuing for imports
    ├── IImportService.cs        # File parsing and import orchestration
    ├── IOptimizationService.cs  # Evolutionary algorithms for data optimization
    ├── IPivotService.cs         # Pivot table generation engine
    ├── IProcessingService.cs    # Core project processing pipeline
    ├── IProjectPersistenceService.cs # Project CRUD and storage abstraction
    ├── IReportService.cs        # Report generation (Excel, CSV, HTML)
    ├── IRmCheckService.cs       # RM QC/QA check logic
    ├── IRowProcessor.cs         # Low-level row processing contract
    └── IVersionService.cs       # Project versioning and history management
```

## 🛠 Usage & patterns

### 1. DTOs (Records)
We use C# `record` types for DTOs to ensure immutability and value-based equality.
```csharp
public record PivotRequest(
    Guid ProjectId,
    string? SearchText = null,
    int Page = 1
);
```

### 2. Service Interfaces
All business logic is exposed via interfaces. Implementations are injected via Dependency Injection (DI) in the `Infrastructure` layer.
```csharp
public interface IImportService
{
    Task<Result<ProjectSaveResult>> ImportCsvAsync(Stream csvStream, string projectName);
}
```

### 3. Dependency Injection
Use `DependencyInjection.AddApplicationServices` to register validators and internal application logic (if any specific logic resides here). Note that the actual Service *Implementations* are typically registered in the Infrastructure layer.

```csharp
// In Program.cs or Startup.cs
services.AddApplicationServices();
```

## 🎯 Design Principles
- **Separation of Concerns**: DTOs define strictly *what* data is exchanged; Interfaces define strictly *what* behaviors are available.
- **Null Safety**: Extensive use of nullable reference types (`string?`, `int?`) to clearly indicate optional data.
- **Result Pattern**: Most services return `Result<T>` (via `Shared.Wrapper`) to handle successes and failures gracefully without throwing exceptions for logic errors.