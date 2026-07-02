using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Competence.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.Competence;

public class SkillCategoryEntityConfiguration : IEntityTypeConfiguration<SkillCategoryEntity>
{
    public void Configure(EntityTypeBuilder<SkillCategoryEntity> builder)
    {
        builder.ToTable("skill_categories", "competence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Namn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Beskrivning).HasMaxLength(1000);
        builder.HasIndex(x => x.Namn).IsUnique();
    }
}

public class SkillRelationConfiguration : IEntityTypeConfiguration<SkillRelation>
{
    public void Configure(EntityTypeBuilder<SkillRelation> builder)
    {
        builder.ToTable("skill_relations", "competence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Typ).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => new { x.FranSkillId, x.TillSkillId }).IsUnique();
    }
}

public class InferredSkillConfiguration : IEntityTypeConfiguration<InferredSkill>
{
    public void Configure(EntityTypeBuilder<InferredSkill> builder)
    {
        builder.ToTable("inferred_skills", "competence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kalla).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Konfidens).IsRequired();
        builder.HasIndex(x => new { x.AnstallId, x.SkillId });
    }
}

public class CareerPathConfiguration : IEntityTypeConfiguration<CareerPath>
{
    public void Configure(EntityTypeBuilder<CareerPath> builder)
    {
        builder.ToTable("career_paths", "competence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Namn).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Bransch).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Beskrivning).HasMaxLength(2000);
        builder.HasMany(x => x.Steg).WithOne().HasForeignKey(x => x.CareerPathId);
    }
}

public class CareerPathStepConfiguration : IEntityTypeConfiguration<CareerPathStep>
{
    public void Configure(EntityTypeBuilder<CareerPathStep> builder)
    {
        builder.ToTable("career_path_steps", "competence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Befattning).HasMaxLength(300).IsRequired();
        builder.Property(x => x.KravdaSkills);
        builder.Property(x => x.Ordning).IsRequired();
    }
}

public class DevelopmentPlanConfiguration : IEntityTypeConfiguration<DevelopmentPlan>
{
    public void Configure(EntityTypeBuilder<DevelopmentPlan> builder)
    {
        builder.ToTable("development_plans", "competence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MalRoll).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasMany(x => x.Milstolpar).WithOne().HasForeignKey(x => x.DevelopmentPlanId);
        builder.HasIndex(x => x.AnstallId);
    }
}

public class DevelopmentMilestoneConfiguration : IEntityTypeConfiguration<DevelopmentMilestone>
{
    public void Configure(EntityTypeBuilder<DevelopmentMilestone> builder)
    {
        builder.ToTable("development_milestones", "competence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Beskrivning).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Typ).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
    }
}

public class InternalOpportunityConfiguration : IEntityTypeConfiguration<InternalOpportunity>
{
    public void Configure(EntityTypeBuilder<InternalOpportunity> builder)
    {
        builder.ToTable("internal_opportunities", "competence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Typ).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Titel).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Kravprofil);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasMany(x => x.Ansokningar).WithOne().HasForeignKey(x => x.InternalOpportunityId);
        builder.HasIndex(x => x.Status);
    }
}

public class OpportunityApplicationConfiguration : IEntityTypeConfiguration<OpportunityApplication>
{
    public void Configure(EntityTypeBuilder<OpportunityApplication> builder)
    {
        builder.ToTable("opportunity_applications", "competence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Motivering).HasMaxLength(2000);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => new { x.InternalOpportunityId, x.AnstallId }).IsUnique();
    }
}

public class MentorRelationConfiguration : IEntityTypeConfiguration<MentorRelation>
{
    public void Configure(EntityTypeBuilder<MentorRelation> builder)
    {
        builder.ToTable("mentor_relations", "competence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FokusOmrade).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.MentorId, x.AdeptId });
    }
}

public class SkillEndorsementConfiguration : IEntityTypeConfiguration<SkillEndorsement>
{
    public void Configure(EntityTypeBuilder<SkillEndorsement> builder)
    {
        builder.ToTable("skill_endorsements", "competence");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.SkillId, x.AnstallId, x.BekraftadAv }).IsUnique();
    }
}
