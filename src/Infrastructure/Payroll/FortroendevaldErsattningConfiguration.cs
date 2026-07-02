using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Payroll;

/// <summary>
/// EF-mappning för <see cref="FortroendevaldErsattning"/> (schema: payroll). Money-belopp lagras
/// via värdekonverterare; status som text-kolumn.
/// </summary>
public sealed class FortroendevaldErsattningConfiguration : IEntityTypeConfiguration<FortroendevaldErsattning>
{
    public void Configure(EntityTypeBuilder<FortroendevaldErsattning> builder)
    {
        builder.ToTable("fortroendevald_ersattningar", "payroll");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Namn).HasColumnName("namn").HasMaxLength(160);
        builder.Property(e => e.Personnummer).HasColumnName("personnummer").HasMaxLength(20);
        builder.Property(e => e.Uppdrag).HasColumnName("uppdrag").HasMaxLength(200);
        builder.Property(e => e.Organ).HasColumnName("organ").HasMaxLength(200);
        builder.Property(e => e.Sammantradesdatum).HasColumnName("sammantradesdatum");
        builder.Property(e => e.Sammantradesarvode).HasConversion(m => m.Amount, v => Money.SEK(v)).HasColumnName("sammantradesarvode");
        builder.Property(e => e.ForloradArbetsinkomst).HasConversion(m => m.Amount, v => Money.SEK(v)).HasColumnName("forlorad_arbetsinkomst");
        builder.Property(e => e.AntalKm).HasColumnName("antal_km");
        builder.Property(e => e.KmErsattning).HasColumnName("km_ersattning");
        builder.Property(e => e.Skattesats).HasColumnName("skattesats");
        builder.Property(e => e.Status).HasConversion<string>().HasColumnName("status").HasMaxLength(20);
        builder.Property(e => e.Registrerad).HasColumnName("registrerad");
        builder.Property(e => e.RegistreradAv).HasColumnName("registrerad_av").HasMaxLength(120);

        builder.HasIndex(e => e.Sammantradesdatum);
    }
}
