using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Core.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.CoreHR;

public class EmploymentConfiguration : IEntityTypeConfiguration<Employment>
{
    public void Configure(EntityTypeBuilder<Employment> builder)
    {
        builder.ToTable("employments", "core_hr");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, v => EmploymentId.From(v))
            .HasColumnName("id");

        builder.Property(e => e.AnstallId)
            .HasConversion(id => id.Value, v => EmployeeId.From(v))
            .HasColumnName("anstalld_id");

        builder.Property(e => e.EnhetId)
            .HasConversion(id => id.Value, v => OrganizationId.From(v))
            .HasColumnName("enhet_id");

        builder.Property(e => e.Anstallningsform)
            .HasConversion<string>()
            .HasColumnName("anstallningsform")
            .HasMaxLength(30);

        builder.Property(e => e.Kollektivavtal)
            .HasConversion<string>()
            .HasColumnName("kollektivavtal")
            .HasMaxLength(10);

        builder.Property(e => e.Manadslon)
            .HasConversion(m => m.Amount, v => Money.SEK(v))
            .HasColumnName("manadslon");

        builder.Property(e => e.Sysselsattningsgrad)
            .HasConversion(p => p.Value, v => new Percentage(v))
            .HasColumnName("sysselsattningsgrad");

        // DateRange as owned
        builder.OwnsOne(e => e.Giltighetsperiod, dr =>
        {
            dr.Property(x => x.Start).HasColumnName("start_datum");
            dr.Property(x => x.End).HasColumnName("slut_datum");
        });

        builder.Property(e => e.AvtalsId)
            .HasConversion(id => id == null ? (Guid?)null : id.Value.Value, v => v == null ? null : CollectiveAgreementId.From(v.Value))
            .HasColumnName("avtals_id");

        builder.Property(e => e.BESTAKod).HasColumnName("besta_kod").HasMaxLength(10);
        builder.Property(e => e.AIDKod).HasColumnName("aid_kod").HasMaxLength(10);
        builder.Property(e => e.Befattningstitel).HasColumnName("befattningstitel").HasMaxLength(200);

        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(100);
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(100);

        // Optimistisk låsning: PostgreSQL:s systemkolumn xmin som samtidighetstoken
        // (shadow property — Npgsql utelämnar systemkolumnen ur CREATE TABLE;
        // InMemory-providern behandlar den som vanlig token → tester opåverkade).
        // Samtidiga skrivningar mot samma anställning ger DbUpdateConcurrencyException
        // i stället för tyst "last write wins".
        builder.Property<uint>("xmin")
            .IsRowVersion()
            .HasColumnName("xmin");

        builder.Ignore(e => e.DomainEvents);
        builder.Ignore(e => e.ArTillsvidareanstallning);
        builder.Ignore(e => e.ArTidsbegransad);
    }
}
