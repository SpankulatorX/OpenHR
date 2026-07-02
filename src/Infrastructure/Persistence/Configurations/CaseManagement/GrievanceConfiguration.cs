using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.CaseManagement.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.CaseManagement;

public class GrievanceConfiguration : IEntityTypeConfiguration<Grievance>
{
    public void Configure(EntityTypeBuilder<Grievance> builder)
    {
        builder.ToTable("grievances", "case_mgmt");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, v => GrievanceId.From(v))
            .HasColumnName("id");

        builder.Property(e => e.AnstallId)
            .HasConversion(id => id.Value, v => EmployeeId.From(v))
            .HasColumnName("anstalld_id");

        builder.Property(e => e.Typ).HasConversion<string>().HasColumnName("typ").HasMaxLength(30);
        builder.Property(e => e.Beskrivning).HasColumnName("beskrivning");
        builder.Property(e => e.FackligRepresentant).HasColumnName("facklig_representant").HasMaxLength(200);
        builder.Property(e => e.Status).HasConversion<string>().HasColumnName("status").HasMaxLength(30);
        builder.Property(e => e.Beslut).HasColumnName("beslut");
        builder.Property(e => e.InlamnadVid).HasColumnName("inlamnad_vid");
        builder.Property(e => e.SkapadVid).HasColumnName("skapad_vid");

        builder.HasMany(e => e.Utredningar).WithOne().HasForeignKey(u => u.GrievanceId);
        builder.HasMany(e => e.Forhandlingar).WithOne().HasForeignKey(f => f.GrievanceId);
        builder.HasMany(e => e.Overklaganden).WithOne().HasForeignKey(o => o.GrievanceId);

        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Ignore(e => e.DomainEvents);
        builder.Ignore(e => e.Version);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedBy);
    }
}

public class GrievanceInvestigationConfiguration : IEntityTypeConfiguration<GrievanceInvestigation>
{
    public void Configure(EntityTypeBuilder<GrievanceInvestigation> builder)
    {
        builder.ToTable("grievance_investigations", "case_mgmt");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.GrievanceId)
            .HasConversion(id => id.Value, v => GrievanceId.From(v))
            .HasColumnName("grievance_id");
        builder.Property(e => e.Utredare).HasColumnName("utredare").HasMaxLength(200);
        builder.Property(e => e.Resultat).HasColumnName("resultat");
        builder.Property(e => e.Bevis).HasColumnName("bevis");
        builder.Property(e => e.VittneUttalanden).HasColumnName("vittne_uttalanden");
        builder.Property(e => e.StartadVid).HasColumnName("startad_vid");
        builder.Property(e => e.AvslutadVid).HasColumnName("avslutad_vid");
    }
}

public class GrievanceHearingConfiguration : IEntityTypeConfiguration<GrievanceHearing>
{
    public void Configure(EntityTypeBuilder<GrievanceHearing> builder)
    {
        builder.ToTable("grievance_hearings", "case_mgmt");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.GrievanceId)
            .HasConversion(id => id.Value, v => GrievanceId.From(v))
            .HasColumnName("grievance_id");
        builder.Property(e => e.Datum).HasColumnName("datum");
        builder.Property(e => e.Deltagare).HasColumnName("deltagare");
        builder.Property(e => e.Protokoll).HasColumnName("protokoll");
        builder.Property(e => e.Beslut).HasColumnName("beslut");
    }
}

public class GrievanceAppealConfiguration : IEntityTypeConfiguration<GrievanceAppeal>
{
    public void Configure(EntityTypeBuilder<GrievanceAppeal> builder)
    {
        builder.ToTable("grievance_appeals", "case_mgmt");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.GrievanceId)
            .HasConversion(id => id.Value, v => GrievanceId.From(v))
            .HasColumnName("grievance_id");
        builder.Property(e => e.Grund).HasColumnName("grund").HasMaxLength(1000);
        builder.Property(e => e.InlamnadVid).HasColumnName("inlamnad_vid");
        builder.Property(e => e.Resultat).HasColumnName("resultat");
        builder.Property(e => e.AvgjordVid).HasColumnName("avgjord_vid");
    }
}
