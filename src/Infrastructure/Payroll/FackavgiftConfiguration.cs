using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Payroll;

/// <summary>
/// EF-mappning för <see cref="Fackavgift"/> (schema: payroll). Money och EmployeeId lagras
/// via värdekonverterare; förbund och medlemsnummer som text-kolumner.
/// </summary>
public sealed class FackavgiftConfiguration : IEntityTypeConfiguration<Fackavgift>
{
    public void Configure(EntityTypeBuilder<Fackavgift> builder)
    {
        builder.ToTable("fackavgifter", "payroll");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.AnstallId)
            .HasConversion(id => id.Value, v => EmployeeId.From(v))
            .HasColumnName("anstalld_id");

        builder.Property(e => e.Fackforbund).HasColumnName("fackforbund").HasMaxLength(120);
        builder.Property(e => e.Typ).HasConversion<string>().HasColumnName("typ").HasMaxLength(20);
        builder.Property(e => e.Belopp).HasConversion(m => m.Amount, v => Money.SEK(v)).HasColumnName("belopp");
        builder.Property(e => e.Procent).HasColumnName("procent");
        builder.Property(e => e.Medlemsnummer).HasColumnName("medlemsnummer").HasMaxLength(64);
        builder.Property(e => e.Startdatum).HasColumnName("startdatum");
        builder.Property(e => e.Slutdatum).HasColumnName("slutdatum");
        builder.Property(e => e.Registrerad).HasColumnName("registrerad");
        builder.Property(e => e.RegistreradAv).HasColumnName("registrerad_av");

        builder.HasIndex(e => e.AnstallId);
    }
}
