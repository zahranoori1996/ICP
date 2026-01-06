using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class CrmSelectionConfiguration : IEntityTypeConfiguration<CrmSelection>
{
    public void Configure(EntityTypeBuilder<CrmSelection> builder)
    {
        builder.ToTable("CrmSelections");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProjectId)
            .IsRequired();

        builder.Property(x => x.SolutionLabel)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.SelectedCrmKey)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.SelectedBy)
            .HasMaxLength(200);

        builder.Property(x => x.SelectedAt)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => new { x.ProjectId, x.SolutionLabel, x.RowIndex })
            .IsUnique()
            .HasDatabaseName("IX_CrmSelections_Project_Label_Row");
    }
}
