using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Scheduling.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.Scheduling;

public class OpenShiftConfiguration : IEntityTypeConfiguration<OpenShift>
{
    public void Configure(EntityTypeBuilder<OpenShift> builder)
    {
        builder.ToTable("open_shifts", "scheduling");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.EnhetId)
            .HasConversion(id => id.Value, v => OrganizationId.From(v))
            .HasColumnName("enhet_id");

        builder.Property(e => e.Datum).HasColumnName("datum");
        builder.Property(e => e.PassTyp).HasColumnName("pass_typ").HasMaxLength(20);
        builder.Property(e => e.StartTid).HasColumnName("start_tid");
        builder.Property(e => e.SlutTid).HasColumnName("slut_tid");
        builder.Property(e => e.KravProfil).HasColumnName("krav_profil");
        builder.Property(e => e.Ersattning).HasColumnName("ersattning").HasMaxLength(30);
        builder.Property(e => e.Status).HasConversion<string>().HasColumnName("status").HasMaxLength(20);
        builder.Property(e => e.TilldeladAnstallId)
            .HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? EmployeeId.From(v.Value) : null)
            .HasColumnName("tilldelad_anstalld_id");
        builder.Property(e => e.TilldelningsMetod).HasColumnName("tilldelnings_metod").HasMaxLength(50);
        builder.Property(e => e.SkapadVid).HasColumnName("skapad_vid");

        builder.HasMany(e => e.Bud).WithOne().HasForeignKey(b => b.OpenShiftId);
    }
}

public class ShiftBidConfiguration : IEntityTypeConfiguration<ShiftBid>
{
    public void Configure(EntityTypeBuilder<ShiftBid> builder)
    {
        builder.ToTable("shift_bids", "scheduling");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.OpenShiftId).HasColumnName("open_shift_id");
        builder.Property(e => e.AnstallId)
            .HasConversion(id => id.Value, v => EmployeeId.From(v))
            .HasColumnName("anstalld_id");
        builder.Property(e => e.Prioritet).HasColumnName("prioritet");
        builder.Property(e => e.Motivering).HasColumnName("motivering").HasMaxLength(500);
        builder.Property(e => e.Status).HasConversion<string>().HasColumnName("status").HasMaxLength(20);
        builder.Property(e => e.SkapadVid).HasColumnName("skapad_vid");
    }
}

public class ShiftBidResultConfiguration : IEntityTypeConfiguration<ShiftBidResult>
{
    public void Configure(EntityTypeBuilder<ShiftBidResult> builder)
    {
        builder.ToTable("shift_bid_results", "scheduling");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.OpenShiftId).HasColumnName("open_shift_id");
        builder.Property(e => e.VinnareAnstallId)
            .HasConversion(id => id.Value, v => EmployeeId.From(v))
            .HasColumnName("vinnare_anstalld_id");
        builder.Property(e => e.Metod).HasColumnName("metod").HasMaxLength(50);
        builder.Property(e => e.Motivering).HasColumnName("motivering").HasMaxLength(500);
        builder.Property(e => e.SkapadVid).HasColumnName("skapad_vid");
    }
}
