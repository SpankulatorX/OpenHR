using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Payroll;

/// <summary>
/// EF-mappning för <see cref="Loneutmatning"/> (schema: payroll). Money och EmployeeId lagras
/// via värdekonverterare; fritext (mottagare, avslutsorsak) som text-kolumner.
/// </summary>
public sealed class LoneutmatningConfiguration : IEntityTypeConfiguration<Loneutmatning>
{
    public void Configure(EntityTypeBuilder<Loneutmatning> builder)
    {
        builder.ToTable("loneutmatningar", "payroll");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.AnstallId)
            .HasConversion(id => id.Value, v => EmployeeId.From(v))
            .HasColumnName("anstalld_id");

        builder.Property(e => e.Malnummer).HasColumnName("malnummer").HasMaxLength(64);
        builder.Property(e => e.Typ).HasConversion<string>().HasColumnName("typ").HasMaxLength(20);
        builder.Property(e => e.Belopp).HasConversion(m => m.Amount, v => Money.SEK(v)).HasColumnName("belopp");
        builder.Property(e => e.Andel).HasColumnName("andel");
        builder.Property(e => e.Forbehallsbelopp).HasConversion(m => m.Amount, v => Money.SEK(v)).HasColumnName("forbehallsbelopp");
        builder.Property(e => e.Mottagare).HasColumnName("mottagare");
        builder.Property(e => e.Startdatum).HasColumnName("startdatum");
        builder.Property(e => e.Slutdatum).HasColumnName("slutdatum");
        builder.Property(e => e.Registrerad).HasColumnName("registrerad");
        builder.Property(e => e.RegistreradAv).HasColumnName("registrerad_av");
        builder.Property(e => e.Avslutsorsak).HasColumnName("avslutsorsak");

        builder.HasIndex(e => e.AnstallId);
    }
}
