using MetaGrow.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Data;

internal static class SoilReportDefinitionMapping
{
    internal static void Configure(ModelBuilder builder)
    {
        builder.Entity<SoilDefinitionField>(field =>
        {
            field.ToTable("SoilReportDefinitionFields");
            field.HasKey(item => item.Key);
            field.Property(item => item.Key).HasMaxLength(100);
            field.Property(item => item.Label).HasMaxLength(160).IsRequired();
        });
        builder.Entity<SoilReportDefinition>(definition =>
        {
            definition.ToTable("SoilReportDefinitions");
            definition.HasKey(item => item.Key);
            definition.Property(item => item.Key).HasMaxLength(100);
            definition.Property(item => item.Name).HasMaxLength(120).IsRequired();
            // A client must submit the version it read, even for edits to child rows only.
            definition.Property(item => item.Version).IsConcurrencyToken();
            definition.OwnsMany(item => item.Sections, section =>
            {
                section.ToTable("SoilReportDefinitionSections");
                section.WithOwner().HasForeignKey("DefinitionKey");
                section.HasKey("DefinitionKey", "Id");
                section.Property(item => item.Id).ValueGeneratedNever();
                section.Property(item => item.Name).HasMaxLength(120).IsRequired();
                section.Property(item => item.Colour).HasMaxLength(7).IsRequired();
                section.OwnsMany(item => item.Items, measure =>
                {
                    measure.ToTable("SoilReportDefinitionItems");
                    measure.WithOwner().HasForeignKey("DefinitionKey", "SectionId");
                    measure.HasKey("DefinitionKey", "SectionId", "Id");
                    measure.Property(item => item.Id).ValueGeneratedNever();
                    measure.Property(item => item.Field).HasMaxLength(100).IsRequired();
                    measure.Property(item => item.Label).HasMaxLength(160).IsRequired();
                    measure.Property(item => item.ShortName).HasMaxLength(40);
                    measure.Property(item => item.NumberFormat).HasMaxLength(30).IsRequired();
                    measure.Property(item => item.BackgroundColour).HasMaxLength(7);
                    measure.HasIndex("DefinitionKey", "Field").IsUnique();
                });
            });
        });
        builder.Entity<SoilReportDefinitionAudit>(audit =>
        {
            audit.ToTable("SoilReportDefinitionAudits");
            audit.HasKey(item => item.Id);
            audit.Property(item => item.DefinitionKey).HasMaxLength(100).IsRequired();
            audit.Property(item => item.ChangedByUserId).HasMaxLength(450).IsRequired();
            audit.Property(item => item.ChangedBy).HasMaxLength(256).IsRequired();
            audit.Property(item => item.Reason).HasMaxLength(500).IsRequired();
            audit.HasIndex(item => new { item.DefinitionKey, item.ChangedUtc });
        });
    }
}
