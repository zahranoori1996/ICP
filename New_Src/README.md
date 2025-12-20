\# 🧪 Isatis ICP



\*\*ICP-OES Data Processing and Quality Control System\*\*



یک سیستم جامع برای پردازش و کنترل کیفیت داده‌های ICP-OES (Inductively Coupled Plasma - Optical Emission Spectrometry) در آزمایشگاه‌های ژئوشیمی.



---



\## 📋 Table of Contents



\- \[Features](#-features)

\- \[Architecture](#-architecture)

\- \[Tech Stack](#-tech-stack)

\- \[Getting Started](#-getting-started)

\- \[API Documentation](#-api-documentation)

\- \[Project Structure](#-project-structure)

\- \[Testing](#-testing)

\- \[Deployment](#-deployment)

\- \[License](#-license)



---



\## ✨ Features



\### 📥 Data Import

\- پشتیبانی از فرمت‌های CSV و Excel

\- تشخیص خودکار فرمت فایل

\- Import پس‌زمینه برای فایل‌های بزرگ

\- Progress tracking در زمان واقعی



\### 🔄 Data Processing

\- Pivot و تبدیل داده‌ها

\- محاسبه Corrected Concentration

\- فیلتر و جستجوی پیشرفته



\### ✅ Quality Control

\- \*\*Weight/Volume Correction\*\*: اصلاح وزن و حجم نمونه‌ها

\- \*\*Drift Correction\*\*: تصحیح دریفت با روش‌های Linear و Stepwise

\- \*\*Blank \& Scale Optimization\*\*: بهینه‌سازی با الگوریتم Differential Evolution

\- \*\*RM Check\*\*: بررسی Reference Materials با CRM



\### 📊 Reporting

\- گزارش‌گیری Excel

\- خلاصه آماری

\- Export داده‌های پردازش شده



\### 🔐 Security

\- JWT Authentication

\- Role-based Authorization (Admin, Analyst, Viewer)

\- Refresh Token support



---



\## 🏗 Architecture



```

┌─────────────────────────────────────────────────────────────┐

│                        Clients                               │

│                  (Web / Mobile / Desktop)                    │

└─────────────────────────────┬───────────────────────────────┘

&nbsp;                             │

&nbsp;                             ▼

┌─────────────────────────────────────────────────────────────┐

│                      YARP Gateway                            │

│                    (Reverse Proxy)                           │

│                     Port: 5000                               │

└─────────────────────────────┬───────────────────────────────┘

&nbsp;                             │

&nbsp;                             ▼

┌─────────────────────────────────────────────────────────────┐

│                       Isatis API                             │

│                    (ASP.NET Core)                            │

│                     Port: 5268                               │

├─────────────────────────────────────────────────────────────┤

│  Controllers │ Services │ DTOs │ Entities │ DbContext       │

└─────────────────────────────┬───────────────────────────────┘

&nbsp;                             │

&nbsp;                             ▼

┌─────────────────────────────────────────────────────────────┐

│                      SQL Server                              │

│                      Database                                │

└─────────────────────────────────────────────────────────────┘

```



---



\## 🛠 Tech Stack



| Layer | Technology |

|-------|------------|

| \*\*API\*\* | ASP.NET Core 10 |

| \*\*Gateway\*\* | YARP Reverse Proxy |

| \*\*Database\*\* | SQL Server / SQLite |

| \*\*ORM\*\* | Entity Framework Core |

| \*\*Auth\*\* | JWT Bearer Tokens |

| \*\*Testing\*\* | xUnit + Moq |

| \*\*Documentation\*\* | OpenAPI / Swagger |



---



\## 🚀 Getting Started



\### Prerequisites



\- .NET 10 SDK

\- SQL Server (یا SQLite برای توسعه)

\- Git



\### Installation



```bash

\# Clone repository

git clone https://github.com/amm1394/ICP. git

cd ICP/New\_Src



\# Restore packages

dotnet restore



\# Update database

dotnet ef database update --project Infrastructure --startup-project Api



\# Run API

dotnet run --project Api



\# Run Gateway (در ترمینال جدید)

dotnet run --project Gateway

```



\### Configuration



فایل `Api/appsettings. json`:



```json

{

&nbsp; "ConnectionStrings": {

&nbsp;   "DefaultConnection": "Server=localhost;Database=IsatisICP;Trusted\_Connection=True;TrustServerCertificate=True;"

&nbsp; },

&nbsp; "Jwt": {

&nbsp;   "Secret": "Your-Secret-Key-At-Least-32-Characters! ",

&nbsp;   "Issuer": "IsatisICP",

&nbsp;   "Audience": "IsatisICP-Users",

&nbsp;   "AccessTokenExpiryMinutes": 60,

&nbsp;   "RefreshTokenExpiryDays": 7

&nbsp; }

}

```



---



\## 📡 API Documentation



\### Base URLs



| Environment | URL |

|-------------|-----|

| Development | `http://localhost:5000` |

| Production | `http://192.168. 0.103:5000` |



\### Authentication



```bash

\# Register

POST /api/auth/register

{

&nbsp; "username": "user1",

&nbsp; "email": "user1@example.com",

&nbsp; "password": "Password123!",

&nbsp; "role": "Analyst"

}



\# Login

POST /api/auth/login

{

&nbsp; "username": "user1",

&nbsp; "password": "Password123!"

}



\# Response

{

&nbsp; "succeeded": true,

&nbsp; "accessToken": "eyJhbGciOiJIUzI1NiIs...",

&nbsp; "refreshToken": "abc123.. .",

&nbsp; "user": { ... }

}

```



\### Using Token



```bash

curl -X GET http://localhost:5000/api/auth/me \\

&nbsp; -H "Authorization: Bearer YOUR\_ACCESS\_TOKEN"

```



\### Main Endpoints



| Method | Endpoint | Description | Auth |

|--------|----------|-------------|:----:|

| GET | `/api/health` | Health check | ❌ |

| POST | `/api/auth/login` | Login | ❌ |

| POST | `/api/auth/register` | Register | ❌ |

| GET | `/api/auth/me` | Current user | ✅ |

| GET | `/api/projects` | List projects | ✅ |

| POST | `/api/import` | Import CSV/Excel | ✅ |

| POST | `/api/pivot` | Pivot data | ✅ |

| POST | `/api/correction/weight` | Weight correction | ✅ |

| POST | `/api/drift/apply` | Drift correction | ✅ |

| POST | `/api/optimization/blank-scale` | Optimize B\&S | ✅ |

| GET | `/api/crm` | List CRMs | ✅ |

| GET | `/api/report/export` | Export report | ✅ |



---



\## 📁 Project Structure



```

New\_Src/

├── Api/                           # Web API Layer

│   ├── Controllers/               # API Controllers

│   ├── Program.cs                 # Entry point

│   └── appsettings.json           # Configuration

│

├── Application/                   # Application Layer

│   ├── DTOs/                      # Data Transfer Objects

│   └── Interface/                 # Service Interfaces

│

├── Domain/                        # Domain Layer

│   └── Entities/                  # Entity classes

│

├── Infrastructure/                # Infrastructure Layer

│   ├── Persistence/               # DbContext \& Migrations

│   └── Services/                  # Service Implementations

│

├── Gateway/                       # YARP Reverse Proxy

│   ├── Program.cs

│   └── appsettings.json

│

├── Shared/                        # Shared Utilities

│   └── Wrapper/                   # Result wrapper

│

└── Tests/                         # Unit Tests

&nbsp;   ├── CorrectionServiceTests.cs

&nbsp;   ├── DriftCorrectionServiceTests. cs

&nbsp;   ├── OptimizationServiceTests.cs

&nbsp;   └── ...  (68 tests total)

```



---



\## 🧪 Testing



```bash

\# Run all tests

cd New\_Src

dotnet test



\# Run with verbosity

dotnet test --verbosity normal



\# Run specific test class

dotnet test --filter "FullyQualifiedName~CorrectionServiceTests"

```



\### Test Coverage



| Category | Tests | Status |

|----------|:-----:|:------:|

| Correction Service | 8 | ✅ |

| Drift Correction | 9 | ✅ |

| Optimization | 6 | ✅ |

| Import Service | 6 | ✅ |

| Processing | 5 | ✅ |

| CRM Service | 10 | ✅ |

| Pivot Service | 8 | ✅ |

| Report Service | 6 | ✅ |

| RM Check | 5 | ✅ |

| Integration | 5 | ✅ |

| \*\*Total\*\* | \*\*68\*\* | ✅ |



---



\## 🚀 Deployment



\### Linux Server



```bash

\# Build for Linux

dotnet publish Api -c Release -r linux-x64 --self-contained



\# Copy to server

scp -r Api/bin/Release/net10. 0/linux-x64/\* user@server:/app/api/



\# On server

chmod +x /app/api/Api

./Api

```



\### Systemd Service



```ini

\# /etc/systemd/system/isatis-api.service

\[Unit]

Description=Isatis ICP API

After=network.target



\[Service]

WorkingDirectory=/app/api

ExecStart=/app/api/Api

Restart=always

User=www-data

Environment=ASPNETCORE\_ENVIRONMENT=Production



\[Install]

WantedBy=multi-user.target

```



```bash

sudo systemctl enable isatis-api

sudo systemctl start isatis-api

```



---



\## 👥 Roles \& Permissions



| Role | Permissions |

|------|-------------|

| \*\*Admin\*\* | Full access + User management |

| \*\*Analyst\*\* | Import, Process, Export |

| \*\*Viewer\*\* | Read-only access |



---



\## 📄 License



This project is proprietary software for Isatis Laboratory. 



---



\## 👨‍💻 Author



\*\*Isatis Development Team\*\*



\- GitHub: \[@amm1394](https://github.com/amm1394)



---



\## 🙏 Acknowledgments



\- Python ICP Processing Scripts (Original Implementation)

\- .NET Community

\- xUnit Testing Framework

