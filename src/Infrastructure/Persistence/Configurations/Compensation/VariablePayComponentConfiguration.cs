using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Compensation.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.Compensation;

public class VariablePayComponentConfiguration : IEntityTypeConfiguration<VariablePayComponent>
{
    public void Configure(EntityTypeBuilder<VariablePayComponent> builder)
    {
        builder.ToTable("variable_pay_components", "compensation");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Namn).HasColumnName("namn").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Typ).HasConversion<string>().HasColumnName("typ").HasMaxLength(20);
        builder.Property(e => e.BerakningsRegel).HasColumnName("beraknings_regel");
        builder.Property(e => e.KoppladTillTiddata).HasColumnName("kopplad_till_tiddata");
    }
}
