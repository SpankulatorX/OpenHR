using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Payroll;

/// <summary>
/// EF-mappning för <see cref="Facktillhorighet"/> (schema: payroll). EmployeeId lagras via
/// värdekonverterare; förbund, roll och medlemsnummer som text-kolumner.
/// </summary>
public sealed class FacktillhorighetConfiguration : IEntityTypeConfiguration<Facktillhorighet>
{
    public void Configure(EntityTypeBuilder<Facktillhorighet> builder)
    {
        builder.ToTable("facktillhorigheter", "payroll");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.AnstallId)
            .HasConversion(id => id.Value, v => EmployeeId.From(v))
            .HasColumnName("anstalld_id");

        builder.Property(e => e.Fackforbund).HasColumnName("fackforbund").HasMaxLength(120);
        builder.Property(e => e.FackforbundKod).HasColumnName("fackforbund_kod").HasMaxLength(40);
        builder.Property(e => e.Medlemsnummer).HasColumnName("medlemsnummer").HasMaxLength(64);
        builder.Property(e => e.Roll).HasConversion<string>().HasColumnName("roll").HasMaxLength(30);
        builder.Property(e => e.Avtalsomrade).HasColumnName("avtalsomrade").HasMaxLength(30);
        builder.Property(e => e.Startdatum).HasColumnName("startdatum");
        builder.Property(e => e.Slutdatum).HasColumnName("slutdatum");
        builder.Property(e => e.Registrerad).HasColumnName("registrerad");
        builder.Property(e => e.RegistreradAv).HasColumnName("registrerad_av").HasMaxLength(120);

        builder.HasIndex(e => e.AnstallId);
    }
}
