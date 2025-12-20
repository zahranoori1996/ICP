using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the database mapping for the RawDataRow entity.
/// </summary>
public class RawDataRowConfiguration : IEntityTypeConfiguration<RawDataRow>
{
    public void Configure(EntityTypeBuilder<RawDataRow> builder)
    {
        builder.ToTable("RawDataRows");

        builder.HasKey(r => r.DataId);

        builder.Property(r => r.ProjectId)
            .IsRequired();

        builder.Property(r => r.ColumnData)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(r => r.SampleId)
            .HasMaxLength(200);

        // Explicit FK mapping using the navigation properties
        builder.HasOne(r => r.Project)
               .WithMany(p => p.RawDataRows)
               .HasForeignKey(r => r.ProjectId)
               .HasPrincipalKey(p => p.ProjectId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}