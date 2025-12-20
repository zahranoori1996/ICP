using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the database mapping for the CrmData entity.
/// </summary>
public class CrmDataConfiguration : IEntityTypeConfiguration<CrmData>
{
    public void Configure(EntityTypeBuilder<CrmData> builder)
    {
        builder.ToTable("CrmData");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.CrmId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.AnalysisMethod)
            .HasMaxLength(200);

        builder.Property(c => c.Type)
            .HasMaxLength(100);

        builder.Property(c => c.ElementValues)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(c => c.IsOurOreas)
            .HasDefaultValue(false);

        builder.Property(c => c.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(c => c.UpdatedAt)
            .IsRequired(false);

        // Indexes for common queries
        builder.HasIndex(c => c.CrmId)
            .HasDatabaseName("IX_CrmData_CrmId");

        builder.HasIndex(c => c.AnalysisMethod)
            .HasDatabaseName("IX_CrmData_AnalysisMethod");

        builder.HasIndex(c => c.IsOurOreas)
            .HasDatabaseName("IX_CrmData_IsOurOreas");

        // Composite index for common filter pattern
        builder.HasIndex(c => new { c.CrmId, c.AnalysisMethod })
            .HasDatabaseName("IX_CrmData_CrmId_AnalysisMethod");
    }
}