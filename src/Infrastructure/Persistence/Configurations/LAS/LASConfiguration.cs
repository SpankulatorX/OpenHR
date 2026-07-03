using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.LAS.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.LAS;

public class LASAccumulationConfiguration : IEntityTypeConfiguration<LASAccumulation>
{
    public void Configure(EntityTypeBuilder<LASAccumulation> builder)
    {
        builder.ToTable("accumulations", "las");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.AnstallId)
            .HasConversion(id => id.Value, v => EmployeeId.From(v))
            .HasColumnName("anstalld_id");
        builder.Property(e => e.Anstallningsform).HasConversion<string>().HasColumnName("anstallningsform").HasMaxLength(30);
        builder.Property(e => e.AckumuleradeDagar).HasColumnName("ackumulerade_dagar");
        // Per-form-räknare (SAVA/vikariat räknas mot varsin LAS-gräns) — våg: LAS-buggfix
        builder.Property(e => e.AckumuleradeSavaDagar).HasColumnName("ackumulerade_sava_dagar");
        builder.Property(e => e.AckumuleradeVikariatDagar).HasColumnName("ackumulerade_vikariat_dagar");
        builder.Property(e => e.ReferensfonsterStart).HasColumnName("referensfonster_start");
        builder.Property(e => e.ReferensfonsterSlut).HasColumnName("referensfonster_slut");
        builder.Property(e => e.Status).HasConversion<string>().HasColumnName("status").HasMaxLength(30);
        builder.Property(e => e.KonverteringsDatum).HasColumnName("konverterings_datum");
        builder.Property(e => e.HarForetradesratt).HasColumnName("har_foretradesratt");
        builder.Property(e => e.ForetradesrattUtgar).HasColumnName("foretradesratt_utgar");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasMany(e => e.Perioder).WithOne().HasForeignKey("accumulation_id");
        builder.HasMany(e => e.Handelser).WithOne().HasForeignKey("accumulation_id");

        builder.Ignore(e => e.DomainEvents);
        builder.Ignore(e => e.Version);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedBy);
    }
}

public class LASPeriodConfiguration : IEntityTypeConfiguration<LASPeriod>
{
    public void Configure(EntityTypeBuilder<LASPeriod> builder)
    {
        builder.ToTable("periods", "las");
        builder.Property<int>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");
        builder.Property(e => e.StartDatum).HasColumnName("start_datum");
        builder.Property(e => e.SlutDatum).HasColumnName("slut_datum");
        builder.Property(e => e.AntalDagar).HasColumnName("antal_dagar");
        builder.Property(e => e.AnstallningsId).HasColumnName("anstallnings_id").HasMaxLength(100);
        // Periodens anställningsform avgör vilken LAS-gräns dagarna räknas mot — våg: LAS-buggfix
        builder.Property(e => e.Anstallningsform).HasConversion<string>().HasColumnName("anstallningsform").HasMaxLength(30);
    }
}

public class LASEventConfiguration : IEntityTypeConfiguration<LASEvent>
{
    public void Configure(EntityTypeBuilder<LASEvent> builder)
    {
        builder.ToTable("events", "las");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Typ).HasConversion<string>().HasColumnName("typ").HasMaxLength(50);
        builder.Property(e => e.Tidpunkt).HasColumnName("tidpunkt");
        builder.Property(e => e.Beskrivning).HasColumnName("beskrivning");
    }
}
