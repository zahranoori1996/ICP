using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

/// <summary>
/// Represents the database context for the Isatis application.
/// </summary>
public class IsatisDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IsatisDbContext"/> class.
    /// </summary>
    /// <param name="options">The database context options.</param>
    public IsatisDbContext(DbContextOptions<IsatisDbContext> options) : base(options) { }

    /// <summary>
    /// Gets or sets the collection of projects.
    /// </summary>
    public DbSet<Project> Projects { get; set; } = null!;

    /// <summary>
    /// Gets or sets the collection of raw data rows.
    /// </summary>
    public DbSet<RawDataRow> RawDataRows { get; set; } = null!;

    /// <summary>
    /// Gets or sets the collection of project states.
    /// </summary>
    public DbSet<ProjectState> ProjectStates { get; set; } = null!;

    /// <summary>
    /// Gets or sets the collection of project import jobs.
    /// </summary>
    public DbSet<ProjectImportJob> ProjectImportJobs { get; set; } = null!;

    /// <summary>
    /// Gets or sets the collection of CRM data.
    /// </summary>
    public DbSet<CrmData> CrmData { get; set; } = null!;

    /// <summary>
    /// Gets or sets the collection of change logs.
    /// </summary>
    public DbSet<ChangeLog> ChangeLogs { get; set; } = null!;

    /// <summary>
    /// Configures the model mapping using the fluent API.
    /// </summary>

    public DbSet<Users> Users { get; set; }

    /// <param name="modelBuilder">The model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IsatisDbContext).Assembly);
    }
}