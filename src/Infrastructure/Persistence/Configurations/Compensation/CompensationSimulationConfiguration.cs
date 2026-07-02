using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Compensation.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.Compensation;

public class CompensationSimulationConfiguration : IEntityTypeConfiguration<CompensationSimulation>
{
    public void Configure(EntityTypeBuilder<CompensationSimulation> builder)
    {
        builder.ToTable("compensation_simulations", "compensation");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Namn).HasColumnName("namn").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Parametrar).HasColumnName("parametrar");
        builder.Property(e => e.BeraknatResultat).HasColumnName("beraknat_resultat");
        builder.Property(e => e.SkapadVid).HasColumnName("skapad_vid");
        builder.Property(e => e.SkapadAv).HasColumnName("skapad_av").HasMaxLength(200);
    }
}
