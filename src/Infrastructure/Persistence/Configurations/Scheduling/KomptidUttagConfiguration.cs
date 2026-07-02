using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Scheduling.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.Scheduling;

/// <summary>
/// EF-konfiguration för <see cref="KomptidUttag"/>. Registreras automatiskt via
/// ApplyConfigurationsFromAssembly, vilket också gör att entiteten ingår i modellen
/// utan en egen DbSet i <c>RegionHRDbContext</c>. Åtkomst sker via <c>db.Set&lt;KomptidUttag&gt;()</c>.
/// </summary>
public class KomptidUttagConfiguration : IEntityTypeConfiguration<KomptidUttag>
{
    public void Configure(EntityTypeBuilder<KomptidUttag> builder)
    {
        builder.ToTable("komptid_uttag", "scheduling");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.AnstallId)
            .HasConversion(id => id.Value, v => EmployeeId.From(v))
            .HasColumnName("anstalld_id");

        builder.Property(e => e.Timmar).HasColumnName("timmar").HasPrecision(8, 2);
        builder.Property(e => e.Typ).HasConversion<string>().HasColumnName("typ").HasMaxLength(20);
        builder.Property(e => e.Status).HasConversion<string>().HasColumnName("status").HasMaxLength(20);
        builder.Property(e => e.FranDatum).HasColumnName("fran_datum");
        builder.Property(e => e.TillDatum).HasColumnName("till_datum");
        builder.Property(e => e.LedighetspostId).HasColumnName("ledighetspost_id");
        builder.Property(e => e.Beskrivning).HasColumnName("beskrivning").HasMaxLength(1000);
        builder.Property(e => e.BegardVid).HasColumnName("begard_vid");
        builder.Property(e => e.HandlagdAv).HasColumnName("handlagd_av");
        builder.Property(e => e.HandlagdVid).HasColumnName("handlagd_vid");
        builder.Property(e => e.Kommentar).HasColumnName("kommentar").HasMaxLength(1000);

        builder.HasIndex(e => e.AnstallId);
        builder.HasIndex(e => e.Status);
    }
}
