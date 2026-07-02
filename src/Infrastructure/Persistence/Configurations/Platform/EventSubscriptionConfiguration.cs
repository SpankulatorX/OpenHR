using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Platform.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.Platform;

public class EventSubscriptionConfiguration : IEntityTypeConfiguration<EventSubscription>
{
    public void Configure(EntityTypeBuilder<EventSubscription> builder)
    {
        builder.ToTable("event_subscriptions", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Namn).HasColumnName("namn").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Url).HasColumnName("url").HasMaxLength(500).IsRequired();
        builder.Property(x => x.HemligNyckel).HasColumnName("hemlig_nyckel").HasMaxLength(500).IsRequired();
        builder.Property(x => x.EventFilter).HasColumnName("event_filter").HasColumnType("jsonb");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.RetryConfig).HasColumnName("retry_config");
        builder.Property(x => x.SkapadVid).HasColumnName("skapad_vid").IsRequired();
        builder.Property(x => x.KonsekutivaMisslyckanden).HasColumnName("konsekutiva_misslyckanden");
        builder.HasIndex(x => x.Status);
    }
}
