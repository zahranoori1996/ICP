# 🌐 Isatis ICP - API

ASP.NET Core Web API for ICP-OES data processing.

## 📋 Overview

این پروژه شامل تمام API Controllers و Entry Point برنامه است.

## 🚀 Running

```bash
# Development
dotnet run

# Production
dotnet run --environment Production

# With specific port
dotnet run --urls "http://0.0.0. 0:5268"
```

## 📁 Structure

```
Api/
├── Controllers/
│   ├── AuthController. cs        # Authentication endpoints
│   ├── HealthController.cs      # Health check
│   ├── ImportController.cs      # Data import
│   ├── ProjectsController.cs    # Project management
│   ├── PivotController.cs       # Data pivot
│   ├── CorrectionController.cs  # Weight/Volume correction
│   ├── DriftController.cs       # Drift correction
│   ├── OptimizationController. cs # Blank & Scale
│   ├── CrmController.cs         # CRM management
│   ├── RmCheckController.cs     # RM verification
│   └── ReportController.cs      # Export & reports
│
├── Program.cs                   # Application entry point
├── appsettings.json             # Configuration
└── appsettings.Development.json # Dev configuration
```

## ⚙️ Configuration

| Setting | Description | Default |
|---------|-------------|---------|
| `ConnectionStrings:DefaultConnection` | Database connection | - |
| `Jwt:Secret` | JWT signing key | - |
| `Jwt:Issuer` | Token issuer | IsatisICP |
| `Jwt:AccessTokenExpiryMinutes` | Token lifetime | 60 |

## 🔗 Dependencies

- `Application` - Interfaces & DTOs
- `Infrastructure` - Service implementations
- `Domain` - Entity classes
- `Shared` - Common utilities