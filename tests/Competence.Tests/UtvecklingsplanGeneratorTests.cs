using RegionHR.Competence.Domain;
using RegionHR.Competence.Services;
using Xunit;

namespace RegionHR.Competence.Tests;

public class UtvecklingsplanGeneratorTests
{
    private readonly CompetenceGapAnalyzer _analyzer = new();
    private readonly UtvecklingsplanGenerator _generator = new();

    private readonly Guid _anstall = Guid.NewGuid();
    private readonly Guid _position = Guid.NewGuid();
    private readonly Guid _hlr = Guid.NewGuid();
    private readonly Guid _lakemedel = Guid.NewGuid();
    private readonly Guid _journal = Guid.NewGuid();
    private readonly DateOnly _start = new(2026, 1, 1);

    private Dictionary<Guid, string> SkillNamn() => new()
    {
        [_hlr] = "HLR",
        [_lakemedel] = "Läkemedelshantering",
        [_journal] = "Journalföring"
    };

    private GapAnalys BuildGap()
    {
        var krav = new[]
        {
            PositionSkillRequirement.Skapa(_position, _hlr, 3),        // uppfyllt
            PositionSkillRequirement.Skapa(_position, _lakemedel, 4),  // gap 2
            PositionSkillRequirement.Skapa(_position, _journal, 3)     // gap 3 (saknas)
        };
        var emp = new[]
        {
            EmployeeSkill.Skapa(_anstall, _hlr, 3),
            EmployeeSkill.Skapa(_anstall, _lakemedel, 2)
        };
        return _analyzer.Analysera(_anstall, _position, "Sjuksköterska", krav, emp, SkillNamn());
    }

    [Fact]
    public void GenereraFranGap_CreatesMilestonePerGap_LinkedToReview()
    {
        var samtalId = Guid.NewGuid();
        var analys = BuildGap();

        var plan = _generator.GenereraFranGap(analys, samtalId, _start);

        Assert.NotNull(plan);
        Assert.Equal(samtalId, plan!.KopplatSamtalId);
        Assert.Equal(_anstall, plan.AnstallId);
        Assert.Equal("Sjuksköterska", plan.MalRoll);
        Assert.Equal(DevelopmentPlanStatus.Draft, plan.Status);
        // ett milstolpe per gap (två gap; HLR är uppfyllt)
        Assert.Equal(2, plan.Milstolpar.Count);
        Assert.All(plan.Milstolpar, m => Assert.Equal("Skill", m.Typ));
    }

    [Fact]
    public void GenereraFranGap_MilestonesCarrySkillAndLevels()
    {
        var analys = BuildGap();

        var plan = _generator.GenereraFranGap(analys, Guid.NewGuid(), _start)!;

        // störst gap först => journal (0->3) före lakemedel (2->4)
        var journalMil = plan.Milstolpar.Single(m => m.SkillId == _journal);
        Assert.Equal(0, journalMil.FranNiva);
        Assert.Equal(3, journalMil.MalNiva);
        Assert.Contains("saknas", journalMil.Beskrivning);

        var lakMil = plan.Milstolpar.Single(m => m.SkillId == _lakemedel);
        Assert.Equal(2, lakMil.FranNiva);
        Assert.Equal(4, lakMil.MalNiva);
    }

    [Fact]
    public void GenereraFranGap_TargetDatesScaleWithGapSize()
    {
        var analys = BuildGap();

        var plan = _generator.GenereraFranGap(analys, Guid.NewGuid(), _start)!;

        var journalMil = plan.Milstolpar.Single(m => m.SkillId == _journal); // gap 3
        var lakMil = plan.Milstolpar.Single(m => m.SkillId == _lakemedel);   // gap 2

        Assert.Equal(_start.AddMonths(3 * UtvecklingsplanGenerator.ManaderPerNivasteg), journalMil.MalDatum);
        Assert.Equal(_start.AddMonths(2 * UtvecklingsplanGenerator.ManaderPerNivasteg), lakMil.MalDatum);
        // planens måldatum = största gapet
        Assert.Equal(_start.AddMonths(3 * UtvecklingsplanGenerator.ManaderPerNivasteg), plan.MalDatum);
    }

    [Fact]
    public void GenereraFranGap_NoGap_ReturnsNull()
    {
        var krav = new[] { PositionSkillRequirement.Skapa(_position, _hlr, 2) };
        var emp = new[] { EmployeeSkill.Skapa(_anstall, _hlr, 4) };
        var analys = _analyzer.Analysera(_anstall, _position, "Roll", krav, emp, SkillNamn());

        var plan = _generator.GenereraFranGap(analys, Guid.NewGuid(), _start);

        Assert.Null(plan);
    }

    [Fact]
    public void GenereraFranGap_OverrideMalRoll_UsesProvidedRole()
    {
        var analys = BuildGap();

        var plan = _generator.GenereraFranGap(analys, Guid.NewGuid(), _start, malRoll: "Specialistsjuksköterska")!;

        Assert.Equal("Specialistsjuksköterska", plan.MalRoll);
    }

    [Fact]
    public void GenereraFranGap_EmptySamtalId_Throws()
    {
        var analys = BuildGap();
        Assert.Throws<ArgumentException>(() => _generator.GenereraFranGap(analys, Guid.Empty, _start));
    }

    [Fact]
    public void SammanfattaMalsattning_ListsGapsCompactly()
    {
        var analys = BuildGap();

        var text = _generator.SammanfattaMalsattning(analys);

        Assert.Contains("2 kompetensgap", text);
        Assert.Contains("Sjuksköterska", text);
        Assert.Contains("Läkemedelshantering", text);
        Assert.Contains("Journalföring", text);
    }

    [Fact]
    public void SammanfattaMalsattning_NoGap_SaysFulfilled()
    {
        var krav = new[] { PositionSkillRequirement.Skapa(_position, _hlr, 2) };
        var emp = new[] { EmployeeSkill.Skapa(_anstall, _hlr, 3) };
        var analys = _analyzer.Analysera(_anstall, _position, "Roll", krav, emp, SkillNamn());

        var text = _generator.SammanfattaMalsattning(analys);

        Assert.Contains("uppfylld", text);
    }

    [Fact]
    public void KopplaTillSamtal_EmptyGuid_Throws()
    {
        var plan = DevelopmentPlan.Skapa(_anstall, "Roll", _start);
        Assert.Throws<ArgumentException>(() => plan.KopplaTillSamtal(Guid.Empty));
    }
}
