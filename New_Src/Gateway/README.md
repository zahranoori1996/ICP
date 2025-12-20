# 🔀 Isatis ICP - Gateway

YARP Reverse Proxy Gateway for routing and load balancing.

## 📋 Overview

این Gateway با استفاده از YARP (Yet Another Reverse Proxy) درخواست‌ها رو به API اصلی route می‌کنه.

## 🚀 Running

```bash
# Development
dotnet run

# Production
dotnet run --environment Production
```

## 📁 Structure

```
Gateway/
├── Program.cs           # Gateway configuration
├── appsettings.json     # Routing configuration
└── Properties/
    └── launchSettings.json
```

## ⚙️ Routing Configuration

```json
{
  "ReverseProxy": {
    "Routes": {
      "api-route": {
        "ClusterId": "api-cluster",
        "Match": { "Path": "/api/{**catch-all}" }
      }
    },
    "Clusters": {
      "api-cluster": {
        "Destinations": {
          "api-primary": {
            "Address": "http://localhost:5268"
          }
        }
      }
    }
  }
}
```

## 🌐 Endpoints

| Endpoint | Description |
|----------|-------------|
| `/` | Gateway info |
| `/health` | Health check |
| `/api/*` | Proxy to API |

## 🔧 Features

- ✅ Reverse Proxy
- ✅ Health Checks
- ✅ Request Logging
- ✅ CORS Support
- ✅ Rate Limiting (optional)