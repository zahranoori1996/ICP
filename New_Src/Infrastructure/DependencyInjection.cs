using Application.Interface;
using Application.Services;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Infrastructure.Services.Processors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

/// <summary>
/// Provides extension methods for registering infrastructure layer services.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the infrastructure services, including database context and service implementations.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configuration">The application configuration properties.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Database Context
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<IsatisDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.CommandTimeout(180);
                sqlOptions.EnableRetryOnFailure(3);
            }));

        // Persistence Services
        services.AddScoped<IProjectPersistenceService, ProjectPersistenceService>();

        // Import Services
        services.AddScoped<IImportService, ImportService>();

        // Background Queue Services
        services.AddSingleton<BackgroundImportQueueService>();
        services.AddSingleton<IImportQueueService>(sp => sp.GetRequiredService<BackgroundImportQueueService>());
        services.AddHostedService(sp => sp.GetRequiredService<BackgroundImportQueueService>());

        // Processing Services
        services.AddScoped<IProcessingService, ProcessingService>();
        services.AddScoped<IRowProcessor, ComputeStatisticsProcessor>();

        // Data Management Services
        services.AddScoped<ICrmService, CrmService>();
        services.AddScoped<IPivotService, PivotService>();
        services.AddScoped<IRmCheckService, RmCheckService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IDriftCorrectionService, DriftCorrectionService>();
        services.AddScoped<IOptimizationService, OptimizationService>();

        // User Management Services
        services.AddScoped<IUserManagementService, UserManagementService>();

        // ✅ REPLACED: Old CorrectionService removed, new segregated services added
        services.AddScoped<IWeightCorrectionService, WeightCorrectionService>();
        services.AddScoped<IVolumeCorrectionService, VolumeCorrectionService>();
        services.AddScoped<IDfCorrectionService, DfCorrectionService>();
        services.AddScoped<IRowCorrectionService, RowCorrectionService>();

        services.AddScoped<IChangeLogService, ChangeLogService>();
        services.AddScoped<IVersionService, VersionService>();

        // Undo Services
        services.AddScoped<IUndoService, UndoService>();

        // Background Maintenance Services
        services.AddSingleton<CleanupHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<CleanupHostedService>());

        return services;
    }
}