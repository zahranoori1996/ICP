using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// ============================================
// Services
// ============================================

// YARP Reverse Proxy
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Health Checks
builder.Services.AddHealthChecks();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Request timeouts (برای Route-level TimeoutPolicy در YARP)
builder.Services.AddRequestTimeouts(options =>
{
    options.AddPolicy("Default", TimeSpan.FromSeconds(60)); // برای اکثر API ها
    options.AddPolicy("Long", TimeSpan.FromMinutes(5));     // برای Undo/Export
});

// (اختیاری) اگر Authorization middleware داری و بعداً لازم میشه
builder.Services.AddAuthorization();

var app = builder.Build();

// ============================================
// Middleware (Order is IMPORTANT)
// ============================================

// Logging middleware
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("→ {Method} {Path}", context.Request.Method, context.Request.Path);

    await next();

    logger.LogInformation("← {StatusCode}", context.Response.StatusCode);
});

// ✅ مهم: باید قبل از Map* ها بیاد
app.UseRouting();

// ✅ مهم‌ترین خط برای رفع 500 YARP TimeoutPolicy
// باید بین UseRouting و endpoint mapping (MapReverseProxy و ...) باشد
app.UseRequestTimeouts();

// CORS
app.UseCors("AllowAll");

// (اختیاری) اگر نیاز داری
app.UseAuthorization();

// ============================================
// Endpoints
// ============================================

app.MapHealthChecks("/health");

// Gateway info
app.MapGet("/", () => new
{
    Name = "Isatis ICP Gateway",
    Version = "1.0.0",
    Status = "Running",
    Timestamp = DateTime.UtcNow,
    Endpoints = new
    {
        Health = "/health",
        Api = "/api/*"
    }
});

// ✅ YARP Reverse Proxy
app.MapReverseProxy();

app.Run();
