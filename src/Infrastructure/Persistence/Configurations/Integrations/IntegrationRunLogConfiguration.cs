using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Infrastructure.Integrations.Framework;

namespace RegionHR.Infrastructure.Persistence.Configurations.Integrations;

/// <summary>
/// EF Core-konfiguration för <see cref="IntegrationRunLog"/>. Registreras
/// automatiskt via ApplyConfigurationsFromAssembly — därför behövs ingen DbSet i
/// RegionHRDbContext; run-loggen nås med <c>db.Set&lt;IntegrationRunLog&gt;()</c>.
/// </summary>
public sealed class IntegrationRunLogConfiguration : IEntityTypeConfiguration<IntegrationRunLog>
{
    public void Configure(EntityTypeBuilder<IntegrationRunLog> builder)
    {
        builder.ToTable("integration_run_log", "integration_hub");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IntegrationKey).HasMaxLength(80).IsRequired();
        builder.Property(x => x.IntegrationNamn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Filnamn).HasMaxLength(260);
        builder.Property(x => x.Plats).HasMaxLength(1000);
        builder.Property(x => x.TransportNamn).HasMaxLength(120).IsRequired();
        builder.Property(x => x.UtlostAv).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Fel).HasMaxLength(2000);
        builder.Property(x => x.Anmarkning).HasMaxLength(1000);
        builder.HasIndex(x => x.IntegrationKey);
        builder.HasIndex(x => x.StartadUtc);
    }
}
