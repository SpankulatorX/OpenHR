using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Scheduling.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.Scheduling;

public class DemandForecastConfiguration : IEntityTypeConfiguration<DemandForecast>
{
    public void Configure(EntityTypeBuilder<DemandForecast> builder)
    {
        builder.ToTable("demand_forecasts", "scheduling");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, v => DemandForecastId.From(v))
            .HasColumnName("id");

        builder.Property(e => e.EnhetId)
            .HasConversion(id => id.Value, v => OrganizationId.From(v))
            .HasColumnName("enhet_id");

        builder.Property(e => e.Datum).HasColumnName("datum");
        builder.Property(e => e.BeraknatAntal).HasColumnName("beraknat_antal");
        builder.Property(e => e.BeraknadeTidmmar).HasColumnName("beraknade_tidmmar").HasPrecision(8, 2);
        builder.Property(e => e.Konfidensgrad).HasColumnName("konfidensgrad").HasPrecision(5, 2);
        builder.Property(e => e.BeraknadVid).HasColumnName("beraknad_vid");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Ignore(e => e.DomainEvents);
        builder.Ignore(e => e.Version);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedBy);
    }
}

public class DemandPatternConfiguration : IEntityTypeConfiguration<DemandPattern>
{
    public void Configure(EntityTypeBuilder<DemandPattern> builder)
    {
        builder.ToTable("demand_patterns", "scheduling");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.EnhetId)
            .HasConversion(id => id.Value, v => OrganizationId.From(v))
            .HasColumnName("enhet_id");

        builder.Property(e => e.Veckodag).HasColumnName("veckodag");
        builder.Property(e => e.TimPaAret).HasColumnName("tim_pa_aret");
        builder.Property(e => e.GenomsnittligBelastning).HasColumnName("genomsnittlig_belastning").HasPrecision(8, 2);
        builder.Property(e => e.SasongsVariation).HasColumnName("sasongs_variation").HasPrecision(5, 2);
    }
}

public class DemandEventConfiguration : IEntityTypeConfiguration<DemandEvent>
{
    public void Configure(EntityTypeBuilder<DemandEvent> builder)
    {
        builder.ToTable("demand_events", "scheduling");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Namn).HasColumnName("namn").HasMaxLength(200);
        builder.Property(e => e.Typ).HasColumnName("typ").HasMaxLength(50);
        builder.Property(e => e.PaverkanGrad).HasColumnName("paverkan_grad").HasPrecision(5, 2);
        builder.Property(e => e.DatumFran).HasColumnName("datum_fran");
        builder.Property(e => e.DatumTill).HasColumnName("datum_till");
    }
}

public class SchedulingConstraintConfiguration : IEntityTypeConfiguration<SchedulingConstraint>
{
    public void Configure(EntityTypeBuilder<SchedulingConstraint> builder)
    {
        builder.ToTable("scheduling_constraints", "scheduling");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Typ).HasColumnName("typ").HasMaxLength(50);
        builder.Property(e => e.Beskrivning).HasColumnName("beskrivning").HasMaxLength(500);
        builder.Property(e => e.Vikt).HasColumnName("vikt").HasPrecision(5, 2);
        builder.Property(e => e.ArHard).HasColumnName("ar_hard");
    }
}

public class ShiftCoverageRequestConfiguration : IEntityTypeConfiguration<ShiftCoverageRequest>
{
    public void Configure(EntityTypeBuilder<ShiftCoverageRequest> builder)
    {
        builder.ToTable("shift_coverage_requests", "scheduling");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.ScheduledShiftId).HasColumnName("scheduled_shift_id");
        builder.Property(e => e.Anledning).HasColumnName("anledning").HasMaxLength(500);
        builder.Property(e => e.Status).HasConversion<string>().HasColumnName("status").HasMaxLength(20);
        builder.Property(e => e.TilldeladAnstallId)
            .HasConversion(
                v => v.HasValue ? v.Value.Value : (Guid?)null,
                v => v.HasValue ? EmployeeId.From(v.Value) : null)
            .HasColumnName("tilldelad_anstalld_id");
        builder.Property(e => e.SkapadVid).HasColumnName("skapad_vid");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Ignore(e => e.DomainEvents);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedBy);
    }
}

public class EmployeeAvailabilityConfiguration : IEntityTypeConfiguration<EmployeeAvailability>
{
    public void Configure(EntityTypeBuilder<EmployeeAvailability> builder)
    {
        builder.ToTable("employee_availability", "scheduling");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.AnstallId)
            .HasConversion(id => id.Value, v => EmployeeId.From(v))
            .HasColumnName("anstalld_id");
        builder.Property(e => e.Veckodag).HasColumnName("veckodag");
        builder.Property(e => e.Datum).HasColumnName("datum");
        builder.Property(e => e.TidFran).HasColumnName("tid_fran");
        builder.Property(e => e.TidTill).HasColumnName("tid_till");
        builder.Property(e => e.Preferens).HasColumnName("preferens").HasMaxLength(20);
        builder.Property(e => e.ArRepeterande).HasColumnName("ar_repeterande");
    }
}

public class FatigueScoreConfiguration : IEntityTypeConfiguration<FatigueScore>
{
    public void Configure(EntityTypeBuilder<FatigueScore> builder)
    {
        builder.ToTable("fatigue_scores", "scheduling");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.AnstallId)
            .HasConversion(id => id.Value, v => EmployeeId.From(v))
            .HasColumnName("anstalld_id");
        builder.Property(e => e.Poang).HasColumnName("poang");
        builder.Property(e => e.KonsekutivaDagar).HasColumnName("konsekutiva_dagar");
        builder.Property(e => e.NattpassSenaste7Dagar).HasColumnName("nattpass_senaste_7_dagar");
        builder.Property(e => e.TotalTimmarSenaste7Dagar).HasColumnName("total_timmar_senaste_7_dagar").HasPrecision(8, 2);
        builder.Property(e => e.KortVila).HasColumnName("kort_vila");
        builder.Property(e => e.HelgarbeteSenaste4Veckor).HasColumnName("helgarbete_senaste_4_veckor");
        builder.Property(e => e.BeraknadVid).HasColumnName("beraknad_vid");
    }
}

public class SchedulingRunConfiguration : IEntityTypeConfiguration<SchedulingRun>
{
    public void Configure(EntityTypeBuilder<SchedulingRun> builder)
    {
        builder.ToTable("scheduling_runs", "scheduling");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, v => SchedulingRunId.From(v))
            .HasColumnName("id");

        builder.Property(e => e.EnhetId)
            .HasConversion(id => id.Value, v => OrganizationId.From(v))
            .HasColumnName("enhet_id");

        builder.Property(e => e.PeriodFran).HasColumnName("period_fran");
        builder.Property(e => e.PeriodTill).HasColumnName("period_till");
        builder.Property(e => e.Parametrar).HasColumnName("parametrar");
        builder.Property(e => e.GenereradePass).HasColumnName("genererade_pass");
        builder.Property(e => e.TotalOBKostnad).HasColumnName("total_ob_kostnad").HasPrecision(12, 2);
        builder.Property(e => e.TotalOvertidKostnad).HasColumnName("total_overtid_kostnad").HasPrecision(12, 2);
        builder.Property(e => e.ATLKompliant).HasColumnName("atl_kompliant");
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
        builder.Property(e => e.SkapadVid).HasColumnName("skapad_vid");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Ignore(e => e.DomainEvents);
        builder.Ignore(e => e.Version);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedBy);
    }
}
