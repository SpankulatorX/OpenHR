using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Configuration.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.Configuration;

public class TenantConfigurationConfiguration : IEntityTypeConfiguration<TenantConfiguration>
{
    public void Configure(EntityTypeBuilder<TenantConfiguration> builder)
    {
        builder.ToTable("tenant_configurations", "configuration");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantNamn).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Organisationsnummer).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Land).HasMaxLength(5);
        builder.Property(x => x.Sprak).HasMaxLength(10);
        builder.Property(x => x.Valuta).HasMaxLength(5);
        builder.Property(x => x.LogoUrl).HasMaxLength(1000);
        builder.Property(x => x.Konfiguration);
        builder.HasIndex(x => x.Organisationsnummer).IsUnique();
    }
}

public class CustomFieldConfiguration : IEntityTypeConfiguration<CustomField>
{
    public void Configure(EntityTypeBuilder<CustomField> builder)
    {
        builder.ToTable("custom_fields", "configuration");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FieldName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(300).IsRequired();
        builder.Property(x => x.FieldType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Target).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Alternativ);
        builder.Property(x => x.Standardvarde).HasMaxLength(500);
        builder.HasIndex(x => x.Target);
        builder.HasIndex(x => new { x.FieldName, x.Target }).IsUnique();
    }
}

public class CustomFieldValueConfiguration : IEntityTypeConfiguration<CustomFieldValue>
{
    public void Configure(EntityTypeBuilder<CustomFieldValue> builder)
    {
        builder.ToTable("custom_field_values", "configuration");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EntityId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Varde).HasMaxLength(2000).IsRequired();
        builder.HasIndex(x => x.CustomFieldId);
        builder.HasIndex(x => new { x.CustomFieldId, x.EntityId }).IsUnique();
    }
}

public class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("workflow_definitions", "configuration");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Namn).HasMaxLength(300).IsRequired();
        builder.Property(x => x.TargetEntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.StegDefinition).HasColumnType("jsonb");
        builder.HasIndex(x => x.TargetEntityType);
    }
}
