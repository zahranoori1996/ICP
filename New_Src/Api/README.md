# 🌐 Isatis ICP - API

ASP.NET Core Web API for ICP-OES (Inductively Coupled Plasma - Optical Emission Spectrometry) data processing and analysis.

## 📋 Overview

This project provides a comprehensive REST API for managing and processing ICP-OES laboratory data. It includes endpoints for data import, correction, optimization, reporting, and project management.

### Key Features

- 🔐 **Authentication & Authorization** - JWT-based authentication system
- 📥 **Data Import** - Support for multiple file formats with background processing
- 🔧 **Data Correction** - Weight, volume, and dilution factor corrections
- 📊 **Drift Correction** - Advanced drift analysis and correction algorithms
- ⚡ **Optimization** - Blank and scale optimization using differential evolution
- 📈 **Pivot Tables** - Dynamic data aggregation and analysis
- 🧪 **CRM Management** - Certified Reference Material data management
- ✅ **RM Checking** - Reference Material validation against CRM values
- 📄 **Report Generation** - Export to Excel, CSV, JSON, and HTML formats

## 🚀 Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- SQL Server (or compatible database)
- Visual Studio 2022 or VS Code (recommended)

### Running the Application

```bash
# Development mode
dotnet run

# Production mode
dotnet run --environment Production

# With specific port
dotnet run --urls "http://0.0.0.0:5268"

# Watch mode (auto-reload on changes)
dotnet watch run
```

### Building

```bash
# Debug build
dotnet build

# Release build
dotnet build --configuration Release

# Publish
dotnet publish --configuration Release --output ./publish
```

## 📁 Project Structure

```
Api/
├── Controllers/
│   ├── Authentication/
│   │   └── AuthController.cs           # User authentication & session management
│   └── ICP/
│       ├── ApiResponse.cs              # Generic API response wrapper
│       ├── CorrectionController.cs     # Weight/Volume/DF corrections
│       ├── CrmController.cs            # CRM data management
│       ├── CrmImportController.cs      # Bulk CRM import operations
│       ├── DriftController.cs          # Drift analysis & correction
│       ├── ImportController.cs         # File import (CSV, Excel)
│       ├── ImportJobsController.cs     # Background job management
│       ├── OptimizationController.cs   # Blank & Scale optimization
│       ├── PivotController.cs          # Pivot table operations
│       ├── ProcessingController.cs     # Project processing
│       ├── ProjectsController.cs       # Project CRUD operations
│       ├── ReportController.cs         # Report generation & export
│       └── RmCheckController.cs        # RM validation
│
├── Program.cs                          # Application entry point & configuration
├── appsettings.json                    # Production configuration
└── appsettings.Development.json        # Development configuration
```

## 🔌 API Endpoints

### Authentication

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/auth/login` | User login |
| POST | `/api/auth/register` | User registration |
| GET | `/api/auth/current` | Get current user |
| POST | `/api/auth/logout` | User logout |

### Projects

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/projects` | List all projects |
| GET | `/api/projects/{id}` | Get project details |
| DELETE | `/api/projects/{id}` | Delete project |
| POST | `/api/projects/{id}/process` | Process project |

### Import

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/import/import` | Import CSV file |
| POST | `/api/import/detect-format` | Detect file format |
| POST | `/api/import/preview` | Preview file contents |
| POST | `/api/import/advanced` | Advanced import with options |
| GET | `/api/import/{jobId}/status` | Get import job status |

### Correction

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/correction/weight` | Apply weight correction |
| POST | `/api/correction/volume` | Apply volume correction |
| POST | `/api/correction/df` | Apply dilution factor correction |
| POST | `/api/correction/undo` | Undo corrections |
| GET | `/api/correction/{projectId}/bad-weights` | Find bad weights |

### Drift

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/drift/analyze` | Analyze drift patterns |
| POST | `/api/drift/correct` | Apply drift correction |
| GET | `/api/drift/{projectId}/segments` | Detect data segments |
| GET | `/api/drift/{projectId}/ratios` | Calculate drift ratios |

### Optimization

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/optimization/blank-scale` | Optimize blank & scale |
| POST | `/api/optimization/preview` | Preview adjustments |
| POST | `/api/optimization/apply` | Apply manual adjustments |
| GET | `/api/optimization/{projectId}/statistics` | Get CRM statistics |

### CRM

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/crm` | List all CRMs |
| GET | `/api/crm/{id}` | Get CRM by ID |
| POST | `/api/crm` | Create/Update CRM |
| DELETE | `/api/crm/{id}` | Delete CRM |
| POST | `/api/crm/import` | Import CRMs from CSV |

### Pivot

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/pivot` | Get pivot table |
| POST | `/api/pivot/advanced` | Advanced pivot with GCD/Repeat |
| GET | `/api/pivot/{projectId}/labels` | Get solution labels |
| GET | `/api/pivot/{projectId}/elements` | Get elements |
| POST | `/api/pivot/export` | Export to CSV |

### Reports

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/reports` | Generate report |
| GET | `/api/reports/{projectId}/excel` | Export to Excel |
| GET | `/api/reports/{projectId}/csv` | Export to CSV |
| GET | `/api/reports/{projectId}/json` | Export to JSON |
| GET | `/api/reports/{projectId}/html` | Generate HTML report |

### RM Check

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/rmcheck` | Check RM samples |
| GET | `/api/rmcheck/{projectId}/samples` | Get RM samples |
| POST | `/api/rmcheck/weight-volume` | Check weight/volume |
| GET | `/api/rmcheck/{projectId}/weight-volume-issues` | Get issues |

## ⚙️ Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=IsatisICP;Trusted_Connection=True;"
  },
  "Jwt": {
    "Secret": "your-secret-key-min-32-characters",
    "Issuer": "IsatisICP",
    "Audience": "IsatisICP",
    "AccessTokenExpiryMinutes": 60
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Cors": {
    "AllowedOrigins": ["http://localhost:3000", "http://localhost:5173"]
  }
}
```

### Environment Variables

| Variable | Description | Required |
|----------|-------------|----------|
| `ASPNETCORE_ENVIRONMENT` | Environment (Development/Production) | No |
| `ConnectionStrings__DefaultConnection` | Database connection string | Yes |
| `Jwt__Secret` | JWT signing key | Yes |

## 🔗 Dependencies

This project depends on the following internal projects:

- **Application** - Application layer with interfaces, DTOs, and service contracts
- **Infrastructure** - Infrastructure layer with service implementations and data access
- **Domain** - Domain layer with entity classes and business logic
- **Shared** - Shared utilities, models, and common code

### NuGet Packages

- `Microsoft.AspNetCore.Authentication.JwtBearer` - JWT authentication
- `Microsoft.EntityFrameworkCore.SqlServer` - Database access
- `Swashbuckle.AspNetCore` - Swagger/OpenAPI documentation
- `Serilog.AspNetCore` - Structured logging

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true

# Run specific test
dotnet test --filter "FullyQualifiedName~AuthController"
```

## 📝 API Documentation

When running in Development mode, Swagger UI is available at:
- **Swagger UI**: `http://localhost:5268/swagger`
- **OpenAPI JSON**: `http://localhost:5268/swagger/v1/swagger.json`

## 🔒 Security

- JWT-based authentication with configurable expiry
- CORS configuration for allowed origins
- Request size limits for file uploads (200MB max)
- Input validation on all endpoints

## 🚀 Deployment

### Docker

```bash
# Build image
docker build -t isatis-icp-api .

# Run container
docker run -p 5268:8080 -e ConnectionStrings__DefaultConnection="..." isatis-icp-api
```

### IIS

1. Publish the application: `dotnet publish -c Release`
2. Copy the published files to IIS directory
3. Create a new Application Pool (.NET CLR Version: No Managed Code)
4. Create a new website pointing to the published directory
5. Configure environment variables in web.config or Application Pool settings

## 📊 Performance

- Background job processing for large imports
- Async/await pattern throughout
- Database query optimization with EF Core
- Response caching where applicable

## 🐛 Troubleshooting

### Common Issues

**Database Connection Errors**
- Verify connection string in appsettings.json
- Ensure SQL Server is running
- Check firewall settings

**JWT Authentication Failures**
- Verify JWT secret is at least 32 characters
- Check token expiry settings
- Ensure clock synchronization between client and server

**File Upload Issues**
- Check request size limits in Program.cs
- Verify file format is supported
- Ensure sufficient disk space

## 📄 License

This project is proprietary software. All rights reserved.

## 👥 Contributors

- Development Team - Isatis ICP Project

## 📞 Support

For issues and questions, please contact the development team.

---

**Version**: 1.0.0  
**Last Updated**: 2025-12-14
