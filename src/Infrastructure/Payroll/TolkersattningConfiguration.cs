using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Payroll;

/// <summary>
/// EF-mappning för <see cref="Tolkersattning"/> (schema: payroll). Money-belopp lagras via
/// värdekonverterare; uppdragstyp och status som text-kolumner.
/// </summary>
public sealed class TolkersattningConfiguration : IEntityTypeConfiguration<Tolkersattning>
{
    public void Configure(EntityTypeBuilder<Tolkersattning> builder)
    {
        builder.ToTable("tolkersattningar", "payroll");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.TolkNamn).HasColumnName("tolk_namn").HasMaxLength(160);
        builder.Property(e => e.TolkPersonnummer).HasColumnName("tolk_personnummer").HasMaxLength(20);
        builder.Property(e => e.Sprak).HasColumnName("sprak").HasMaxLength(80);
        builder.Property(e => e.Typ).HasConversion<string>().HasColumnName("typ").HasMaxLength(30);
        builder.Property(e => e.Uppdragsdatum).HasColumnName("uppdragsdatum");
        builder.Property(e => e.BestallandeEnhet).HasColumnName("bestallande_enhet").HasMaxLength(160);
        builder.Property(e => e.Referens).HasColumnName("referens").HasMaxLength(80);
        builder.Property(e => e.AntalTimmar).HasColumnName("antal_timmar");
        builder.Property(e => e.Timarvode).HasConversion(m => m.Amount, v => Money.SEK(v)).HasColumnName("timarvode");
        builder.Property(e => e.Forberedelsearvode).HasConversion(m => m.Amount, v => Money.SEK(v)).HasColumnName("forberedelsearvode");
        builder.Property(e => e.Reseersattning).HasConversion(m => m.Amount, v => Money.SEK(v)).HasColumnName("reseersattning");
        builder.Property(e => e.HarFSkatt).HasColumnName("har_fskatt");
        builder.Property(e => e.Skattesats).HasColumnName("skattesats");
        builder.Property(e => e.Status).HasConversion<string>().HasColumnName("status").HasMaxLength(20);
        builder.Property(e => e.Registrerad).HasColumnName("registrerad");
        builder.Property(e => e.RegistreradAv).HasColumnName("registrerad_av").HasMaxLength(120);

        builder.HasIndex(e => e.Uppdragsdatum);
    }
}
