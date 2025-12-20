using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the database mapping for the ProjectState entity.
/// </summary>
public class ProjectStateConfiguration : IEntityTypeConfiguration<ProjectState>
{
    public void Configure(EntityTypeBuilder<ProjectState> builder)
    {
        builder.ToTable("ProjectStates");

        builder.HasKey(s => s.StateId);

        builder.Property(s => s.ProjectId)
            .IsRequired();

        // Parent state for tree structure
        builder.Property(s => s.ParentStateId)
            .IsRequired(false);

        builder.Property(s => s.VersionNumber)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(s => s.ProcessingType)
            .IsRequired()
            .HasMaxLength(100)
            .HasDefaultValue("Import");

        builder.Property(s => s.Data)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(s => s.Timestamp)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(s => s.Description)
            .HasMaxLength(500);

        builder.Property(s => s.IsActive)
            .IsRequired()
            .HasDefaultValue(false);

        // Self-referencing relationship for tree structure
        builder.HasOne(s => s.ParentState)
               .WithMany(s => s.ChildStates)
               .HasForeignKey(s => s.ParentStateId)
               .OnDelete(DeleteBehavior.Restrict); // Don't cascade delete children when parent deleted

        // Explicit FK mapping to Project
        builder.HasOne(s => s.Project)
               .WithMany(p => p.ProjectStates)
               .HasForeignKey(s => s.ProjectId)
               .HasPrincipalKey(p => p.ProjectId)
               .OnDelete(DeleteBehavior.Cascade);

        // Index for faster queries
        builder.HasIndex(s => new { s.ProjectId, s.IsActive });
        builder.HasIndex(s => s.ParentStateId);
    }
}