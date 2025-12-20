# ⚙️ Isatis ICP - Infrastructure Layer

Service implementations, database, and external integrations. 

## 📋 Overview

این لایه شامل پیاده‌سازی سرویس‌ها و ارتباط با دیتابیس است.

## 📁 Structure

```
Infrastructure/
├── Persistence/
│   ├── IsatisDbContext. cs       # EF Core DbContext
│   ├── Configurations/          # Entity configurations
│   │   ├── UserConfiguration.cs
│   │   ├── ProjectConfiguration. cs
│   │   └── ... 
│   └── Migrations/              # Database migrations
│
├── Services/
│   ├── AuthService.cs           # JWT Authentication
│   ├── ImportService. cs         # CSV/Excel import
│   ├── ProcessingService.cs     # Data processing
│   ├── PivotService.cs          # Pivot operations
│   ├── CorrectionService.cs     # Weight/Volume
│   ├── DriftCorrectionService.cs # Drift algorithms
│   ├── OptimizationService.cs   # Differential Evolution
│   ├── CrmService.cs            # CRM management
│   ├── RmCheckService.cs        # RM verification
│   ├── ReportService.cs         # Excel export
│   ├── ChangeLogService.cs      # Audit logging
│   ├── ProjectPersistenceService.cs
│   ├── BackgroundImportQueueService.cs
│   └── AdvancedFileParser.cs    # File parsing
│
└── DependencyInjection.cs       # DI registration
```

## 🔧 Key Services

### AuthService
- JWT token generation
- Password hashing (SHA256 + Salt)
- Refresh token management

### OptimizationService
- Differential Evolution algorithm
- Multi-model optimization (A, B, C)
- Blank & Scale calculation

### DriftCorrectionService
- Linear interpolation
- Stepwise correction
- Segment detection

## 💾 Database

```bash
# Create migration
dotnet ef migrations add MigrationName --project Infrastructure --startup-project Api

# Apply migrations
dotnet ef database update --project Infrastructure --startup-project Api
```