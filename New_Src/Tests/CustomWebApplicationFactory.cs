using Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // 1) Remove all previous IsatisDbContext registrations
            var descriptorsToRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<IsatisDbContext>) ||
                    d.ServiceType == typeof(IsatisDbContext))
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            // 2) If TEST_SQL_CONNECTION is set, use SQL Server for testing
            var sqlConn = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION");
            if (!string.IsNullOrWhiteSpace(sqlConn))
            {
                services.AddDbContext<IsatisDbContext>(options =>
                {
                    options.UseSqlServer(sqlConn, sqlOptions =>
                    {
                        // Increase timeout for CI/heavy tests
                        sqlOptions.CommandTimeout(180);
                    });
                });

                // Build provider and apply migrations
                using var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IsatisDbContext>();

                db.Database.Migrate();
            }
            else
            {
                // 3) Default: Use InMemory for tests
                services.AddDbContext<IsatisDbContext>(options =>
                {
                    options.UseInMemoryDatabase("Isatis_TestDb");
                });

                // Build provider and clean/create InMemory database
                using var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IsatisDbContext>();

                db.Database.EnsureDeleted();
                db.Database.EnsureCreated();

                // You can perform data seeding here if necessary
                // SeedTestData.Initialize(db);
            }
        });
    }
}
