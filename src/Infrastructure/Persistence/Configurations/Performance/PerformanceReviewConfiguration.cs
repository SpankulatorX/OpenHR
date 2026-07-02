using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Performance.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.Performance;

public class PerformanceReviewConfiguration : IEntityTypeConfiguration<PerformanceReview>
{
    public void Configure(EntityTypeBuilder<PerformanceReview> builder)
    {
        builder.ToTable("performance_reviews", "performance");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.SjalvBedomning);
        builder.Property(x => x.ChefsBedomning);
        builder.Property(x => x.Malsattning).HasMaxLength(4000);
        builder.Property(x => x.Kommentar).HasMaxLength(2000);
        builder.HasIndex(x => new { x.AnstallId, x.Ar });
        builder.HasIndex(x => x.ChefId);
    }
}
