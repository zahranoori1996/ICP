using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the database mapping for the ProjectImportJob entity.
/// </summary>
public class ProjectImportJobConfiguration : IEntityTypeConfiguration<ProjectImportJob>
{
    public void Configure(EntityTypeBuilder<ProjectImportJob> builder)
    {
        builder.ToTable("ProjectImportJobs");

        builder.HasKey(j => j.JobId);

        builder.Property(j => j.ProjectName)
            .HasMaxLength(250);

        builder.Property(j => j.Message)
            .HasMaxLength(2000)
            .HasColumnType("nvarchar(2000)");

        builder.Property(j => j.State)
            .IsRequired();

        builder.Property(j => j.TotalRows)
            .IsRequired();

        builder.Property(j => j.ProcessedRows)
            .IsRequired();

        builder.Property(j => j.Percent)
            .IsRequired();

        builder.Property(j => j.TempFilePath)
            .HasMaxLength(2000)
            .IsRequired(false);

        builder.Property(j => j.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(j => j.UpdatedAt)
            .IsRequired(false);
    }
}