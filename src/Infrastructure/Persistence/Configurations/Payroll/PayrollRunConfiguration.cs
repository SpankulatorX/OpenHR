using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.Payroll;

public class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> builder)
    {
        builder.ToTable("payroll_runs", "payroll");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, v => PayrollRunId.From(v))
            .HasColumnName("id");

        builder.Property(e => e.Year).HasColumnName("ar");
        builder.Property(e => e.Month).HasColumnName("manad");
        builder.Property(e => e.Status).HasConversion<string>().HasColumnName("status").HasMaxLength(20);
        builder.Property(e => e.StartadVid).HasColumnName("startad_vid");
        builder.Property(e => e.AvslutadVid).HasColumnName("avslutad_vid");
        builder.Property(e => e.StartadAv).HasColumnName("startad_av").HasMaxLength(100);
        builder.Property(e => e.GodkandAv).HasColumnName("godkand_av").HasMaxLength(100);
        builder.Property(e => e.AntalAnstallda).HasColumnName("antal_anstallda");

        builder.Property(e => e.TotalBrutto).HasConversion(m => m.Amount, v => Money.SEK(v)).HasColumnName("total_brutto");
        builder.Property(e => e.TotalNetto).HasConversion(m => m.Amount, v => Money.SEK(v)).HasColumnName("total_netto");
        builder.Property(e => e.TotalSkatt).HasConversion(m => m.Amount, v => Money.SEK(v)).HasColumnName("total_skatt");
        builder.Property(e => e.TotalArbetsgivaravgifter).HasConversion(m => m.Amount, v => Money.SEK(v)).HasColumnName("total_arbetsgivaravgifter");

        builder.Property(e => e.ArRetroaktiv).HasColumnName("ar_retroaktiv");
        builder.Property(e => e.RetroaktivtForPeriod).HasColumnName("retroaktivt_for_period").HasMaxLength(7);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");

        builder.HasMany(e => e.Resultat).WithOne().HasForeignKey(r => r.KorningsId);

        // Optimistisk låsning: PostgreSQL:s systemkolumn xmin som samtidighetstoken
        // (shadow property — Npgsql utelämnar systemkolumnen ur CREATE TABLE;
        // InMemory-providern behandlar den som vanlig token → tester opåverkade).
        // Samtidiga skrivningar mot samma lönekörning ger DbUpdateConcurrencyException
        // i stället för tyst "last write wins".
        builder.Property<uint>("xmin")
            .IsRowVersion()
            .HasColumnName("xmin");

        builder.Ignore(e => e.DomainEvents);
        builder.Ignore(e => e.Period);
        builder.Ignore(e => e.UpdatedAt);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedBy);
    }
}
