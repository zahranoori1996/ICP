using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// A hosted background service that periodically cleans up old import jobs and temporary files.
/// </summary>
public class CleanupHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CleanupHostedService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _ttl;

    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupHostedService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider to create scopes.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="configuration">The configuration to read settings from.</param>
    public CleanupHostedService(IServiceProvider serviceProvider, ILogger<CleanupHostedService> logger, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _interval = TimeSpan.TryParse(configuration?["Cleanup:Interval"], out var i) ? i : TimeSpan.FromHours(1);
        _ttl = TimeSpan.TryParse(configuration?["Cleanup:JobTTL"], out var t) ? t : TimeSpan.FromDays(30);
    }

    /// <summary>
    /// Executes the background cleanup task.
    /// </summary>
    /// <param name="stoppingToken">The token to monitor for cancellation requests.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CleanupHostedService started. Interval={Interval}, TTL={TTL}", _interval, _ttl);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IsatisDbContext>();

                var cutoff = DateTime.UtcNow - _ttl;
                var oldJobs = await db.ProjectImportJobs.Where(j => j.CreatedAt < cutoff).ToListAsync(stoppingToken);
                foreach (var j in oldJobs)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(j.TempFilePath) && File.Exists(j.TempFilePath))
                        {
                            File.Delete(j.TempFilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete temp file {Path} for old job {JobId}", j.TempFilePath, j.JobId);
                    }

                    db.ProjectImportJobs.Remove(j);
                }

                if (oldJobs.Count > 0) await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CleanupHostedService encountered an error");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}