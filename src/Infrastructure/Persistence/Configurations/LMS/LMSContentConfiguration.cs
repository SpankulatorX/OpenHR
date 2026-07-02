using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.LMS.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.LMS;

// EF-konfiguration för wave7 LMS-innehåll (lektioner, SCORM, genomförandespårning,
// externa deltagare). Auto-registreras via ApplyConfigurationsFromAssembly i
// RegionHRDbContext — inga DbSet krävs, men rekommenderade DbSet finns i
// integration-notes för bekvämt LINQ-anrop (db.Lessons etc.). Alla tabeller i schemat "lms".

public class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        builder.ToTable("lessons", "lms");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Titel).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Typ).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.TextInnehall).HasColumnType("text"); // fritext
        builder.Property(x => x.MediaUrl).HasMaxLength(2000);
        builder.Property(x => x.FilStoragePath).HasMaxLength(1000);
        builder.Property(x => x.FilNamn).HasMaxLength(400);
        builder.HasIndex(x => x.CourseId);
        builder.HasIndex(x => new { x.CourseId, x.Ordning });
    }
}

public class ScormPackageConfiguration : IEntityTypeConfiguration<ScormPackage>
{
    public void Configure(EntityTypeBuilder<ScormPackage> builder)
    {
        builder.ToTable("scorm_packages", "lms");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Titel).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Identifier).HasMaxLength(500);
        builder.Property(x => x.Version).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.LaunchUrl).HasMaxLength(2000);
        builder.Property(x => x.StoragePath).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.OriginalFilNamn).HasMaxLength(400);
        builder.Property(x => x.MasteryScore).HasPrecision(6, 2);
        builder.Ignore(x => x.VersionText);
        builder.HasIndex(x => x.CourseId);
    }
}

public class LessonCompletionConfiguration : IEntityTypeConfiguration<LessonCompletion>
{
    public void Configure(EntityTypeBuilder<LessonCompletion> builder)
    {
        builder.ToTable("lesson_completions", "lms");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.CourseId);
        builder.HasIndex(x => x.LessonId);
        builder.HasIndex(x => new { x.CourseId, x.DeltagareId, x.ArExtern });
    }
}

public class ExternalParticipantConfiguration : IEntityTypeConfiguration<ExternalParticipant>
{
    public void Configure(EntityTypeBuilder<ExternalParticipant> builder)
    {
        builder.ToTable("external_participants", "lms");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Epost).HasMaxLength(320).IsRequired();
        builder.Property(x => x.Namn).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Organisation).HasMaxLength(300);
        builder.Property(x => x.AccessToken).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(x => x.HarAktivAccess);
        builder.HasIndex(x => x.AccessToken).IsUnique();
        builder.HasIndex(x => x.Epost);
    }
}

public class ExternalCourseEnrollmentConfiguration : IEntityTypeConfiguration<ExternalCourseEnrollment>
{
    public void Configure(EntityTypeBuilder<ExternalCourseEnrollment> builder)
    {
        builder.ToTable("external_course_enrollments", "lms");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Progress).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => x.ExternalParticipantId);
        builder.HasIndex(x => x.CourseId);
        builder.HasIndex(x => new { x.ExternalParticipantId, x.CourseId }).IsUnique();
    }
}
