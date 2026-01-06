using Application.Interface;
using Application.Services;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// ============================================
// 1.  Blazor Server Services
// ============================================
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// ============================================
// 2. MudBlazor Services
// ============================================
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.NewestOnTop = true;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 1500;
    config.SnackbarConfiguration.ShowTransitionDuration = 300;
    config.SnackbarConfiguration.HideTransitionDuration = 300;
});

// ============================================
// 3. Database Connection (حیاتی برای رفع خطاها)
// ============================================
// اطمینان حاصل کنید که کانکشن استرینگ در appsettings.json موجود است
//var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
//if (string.IsNullOrEmpty(connectionString))
//{
//    // اگر فایل appsettings.json خوانده نشود یا مقدار خالی باشد، اینجا برنامه متوقف می‌شود و دلیل را می‌گوید
//    throw new InvalidOperationException("Connection string 'DefaultConnection' not found. Please check appsettings.json.");
//}

var connectionString = "Data Source=192.168.0.13;Initial Catalog=ICP;Persist Security Info=True;User ID=sa;Password=AliReza23280;MultipleActiveResultSets=False;TrustServerCertificate=True";

// بررسی می‌کنیم که خالی نباشد (که الان قطعا نیست)
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Connection String is Empty!");
}




// اگر از SQL Server استفاده می‌کنید:
builder.Services.AddDbContext<IsatisDbContext>(options =>
    options.UseSqlServer(connectionString));

// ============================================
// 4. Register Infrastructure Services
// ============================================

builder.Services.AddScoped<IProjectPersistenceService, Infrastructure.Services.ProjectPersistenceService>();


// ============================================
// 5. Application Services (Main Fixes)
// ============================================

builder.Services.AddScoped<WebUI.Services.AuthService>();

// ثبت سرویس‌ها با الگوی: <اینترفیس, پیاده‌سازی>
builder.Services.AddScoped<IImportService, Infrastructure.Services.ImportService>();
builder.Services.AddScoped<IUserManagementService, Infrastructure.Services.UserManagementService>();
builder.Services.AddScoped<ICrmService, Infrastructure.Services.CrmService>();
builder.Services.AddScoped<IPivotService, Infrastructure.Services.PivotService>();
builder.Services.AddScoped<IReportService, Infrastructure.Services.ReportService>();
builder.Services.AddScoped<IOptimizationService, Infrastructure.Services.OptimizationService>();
builder.Services.AddScoped<IVersionService, Infrastructure.Services.VersionService>();
builder.Services.AddScoped<Application.Services.IRmCheckService, Infrastructure.Services.RmCheckService>();

builder.Services.AddScoped<WebUI.Services.ProjectService>();
builder.Services.AddScoped<WebUI.Services.CorrectionService>();
builder.Services.AddScoped<WebUI.Services.DriftService>();
builder.Services.AddScoped<WebUI.Services.PivotService>();
builder.Services.AddScoped<WebUI.Services.CrmService>();
builder.Services.AddScoped<WebUI.Services.ReportService>();
builder.Services.AddScoped<WebUI.Services.OptimizationService>();
builder.Services.AddScoped<WebUI.Services.DashboardService>();
builder.Services.AddScoped<WebUI.Services.UserManagementService>();

// ============================================
// 6. HttpClient (Optional / Context dependent)
// ============================================
var apiBaseUrl = builder.Configuration.GetValue<string>("ApiSettings:BaseUrl") ?? "http://192.168.0.103:5000/api/";
builder.Services.AddHttpClient("Api", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// ============================================
// Build Application
// ============================================
var app = builder.Build();

// ============================================
// Middleware Pipeline
// ============================================
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// ============================================
// Log startup info
// ============================================
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("WebUI started. API Base URL: {ApiUrl}", apiBaseUrl);

app.Run();
