using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Configuration.Domain;
using RegionHR.Platform.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.Platform;

public class CustomObjectConfiguration : IEntityTypeConfiguration<CustomObject>
{
    public void Configure(EntityTypeBuilder<CustomObject> builder)
    {
        builder.ToTable("custom_objects", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Namn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PluralNamn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Beskrivning).HasMaxLength(2000);
        builder.Property(x => x.FaltSchema).HasColumnType("jsonb");
        builder.Property(x => x.Relationer).HasColumnType("jsonb");
        builder.Property(x => x.Ikon).HasMaxLength(100);
        builder.HasIndex(x => x.Namn).IsUnique();
    }
}

public class CustomObjectRecordConfiguration : IEntityTypeConfiguration<CustomObjectRecord>
{
    public void Configure(EntityTypeBuilder<CustomObjectRecord> builder)
    {
        builder.ToTable("custom_object_records", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Data).HasColumnType("jsonb");
        builder.Property(x => x.SkapadAv).HasMaxLength(200);
        builder.HasIndex(x => x.CustomObjectId);
    }
}

public class CustomObjectRelationConfiguration : IEntityTypeConfiguration<CustomObjectRelation>
{
    public void Configure(EntityTypeBuilder<CustomObjectRelation> builder)
    {
        builder.ToTable("custom_object_relations", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.KallEntityTyp).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RelationsTyp).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.CustomObjectId);
    }
}

public class WorkflowNodeConfiguration : IEntityTypeConfiguration<WorkflowNode>
{
    public void Configure(EntityTypeBuilder<WorkflowNode> builder)
    {
        builder.ToTable("workflow_nodes", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Ordning).IsRequired();
        builder.Property(x => x.Typ).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Konfiguration);
        builder.Property(x => x.Namn).HasMaxLength(300).IsRequired();
        builder.HasIndex(x => new { x.WorkflowDefinitionId, x.Ordning });
    }
}

public class WorkflowRunInstanceConfiguration : IEntityTypeConfiguration<WorkflowRunInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowRunInstance> builder)
    {
        builder.ToTable("workflow_run_instances", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.EntityTyp).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Data).HasColumnType("jsonb");
        builder.HasIndex(x => x.WorkflowDefinitionId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.EntityTyp, x.EntityId });
    }
}

public class ExtensionConfiguration : IEntityTypeConfiguration<Extension>
{
    public void Configure(EntityTypeBuilder<Extension> builder)
    {
        builder.ToTable("extensions", "platform");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Namn).HasColumnName("namn").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Forfattare).HasColumnName("forfattare").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Beskrivning).HasColumnName("beskrivning").HasMaxLength(2000);
        builder.Property(x => x.Typ).HasConversion<string>().HasColumnName("typ").HasMaxLength(30);
        builder.Property(x => x.Licens).HasColumnName("licens").HasMaxLength(100);
        builder.Property(x => x.Kompatibilitet).HasColumnName("kompatibilitet").HasMaxLength(50);
        builder.Property(x => x.Innehall).HasColumnName("innehall");
        builder.Property(x => x.SkapadVid).HasColumnName("skapad_vid");

        builder.HasIndex(x => x.Namn);
    }
}

public class ExtensionInstallationConfiguration : IEntityTypeConfiguration<ExtensionInstallation>
{
    public void Configure(EntityTypeBuilder<ExtensionInstallation> builder)
    {
        builder.ToTable("extension_installations", "platform");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ExtensionId).HasColumnName("extension_id");
        builder.Property(x => x.Version).HasColumnName("version").HasMaxLength(50).IsRequired();
        builder.Property(x => x.InstallationsDatum).HasColumnName("installations_datum");
        builder.Property(x => x.Status).HasConversion<string>().HasColumnName("status").HasMaxLength(20);
        builder.Property(x => x.Konfiguration).HasColumnName("konfiguration");

        builder.HasIndex(x => x.ExtensionId);
    }
}
