using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.GDPR.Klassa;

namespace RegionHR.Infrastructure.Persistence.Configurations.Klassa;

public class InformationsklassPostConfiguration : IEntityTypeConfiguration<InformationsklassPost>
{
    public void Configure(EntityTypeBuilder<InformationsklassPost> builder)
    {
        builder.ToTable("informationsklass_poster", "gdpr");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Informationsmangd).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Beskrivning).HasColumnType("text");
        builder.Property(x => x.Kategori).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.Systemomrade).HasMaxLength(100);

        builder.Property(x => x.Konfidentialitet).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Riktighet).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Tillganglighet).HasConversion<string>().HasMaxLength(20);

        builder.Property(x => x.KonfidentialitetMotivering).HasColumnType("text");
        builder.Property(x => x.RiktighetMotivering).HasColumnType("text");
        builder.Property(x => x.TillganglighetMotivering).HasColumnType("text");
        builder.Property(x => x.Skyddsatgarder).HasColumnType("text");
        builder.Property(x => x.Lagrum).HasMaxLength(500);
        builder.Property(x => x.GranskadAv).HasMaxLength(200);

        builder.HasIndex(x => x.Informationsmangd).IsUnique();
        builder.HasIndex(x => x.Kategori);
    }
}
