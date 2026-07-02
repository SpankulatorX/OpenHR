using RegionHR.Competence.Domain;
using RegionHR.Competence.Services;
using Xunit;

namespace RegionHR.Competence.Tests;

public class CompetenceGapAnalyzerTests
{
    private readonly CompetenceGapAnalyzer _analyzer = new();

    private readonly Guid _anstall = Guid.NewGuid();
    private readonly Guid _position = Guid.NewGuid();

    private readonly Guid _hlr = Guid.NewGuid();
    private readonly Guid _lakemedel = Guid.NewGuid();
    private readonly Guid _journal = Guid.NewGuid();

    private Dictionary<Guid, string> SkillNamn() => new()
    {
        [_hlr] = "HLR",
        [_lakemedel] = "Läkemedelshantering",
        [_journal] = "Journalföring"
    };

    [Fact]
    public void Analysera_MixedLevels_ComputesGapPerSkill()
    {
        var krav = new[]
        {
            PositionSkillRequirement.Skapa(_position, _hlr, 3),
            PositionSkillRequirement.Skapa(_position, _lakemedel, 4),
            PositionSkillRequirement.Skapa(_position, _journal, 2)
        };
        var emp = new[]
        {
            EmployeeSkill.Skapa(_anstall, _hlr, 3),        // uppfyllt (3>=3)
            EmployeeSkill.Skapa(_anstall, _lakemedel, 2),  // gap 2 (2->4)
            // journal saknas helt => gap 2 (0->2)
        };

        var result = _analyzer.Analysera(_anstall, _position, "Sjuksköterska", krav, emp, SkillNamn());

        Assert.Equal(3, result.AntalKrav);
        Assert.Equal(1, result.AntalUppfyllda);
        Assert.Equal(2, result.AntalGap);
        Assert.True(result.HarGap);
        Assert.Equal(4, result.TotaltGapPoang); // 2 (lakemedel) + 2 (journal)
        Assert.Equal(33, result.TackningsgradProcent); // 1/3
    }

    [Fact]
    public void Analysera_MissingSkill_TreatedAsLevelZero()
    {
        var krav = new[] { PositionSkillRequirement.Skapa(_position, _journal, 2) };
        var emp = Array.Empty<EmployeeSkill>();

        var result = _analyzer.Analysera(_anstall, _position, null, krav, emp, SkillNamn());

        var gap = Assert.Single(result.Gap);
        Assert.Equal(0, gap.NuvarandeNiva);
        Assert.True(gap.Saknas);
        Assert.Equal(2, gap.GapPoang);
        Assert.Equal("Journalföring", gap.SkillNamn);
    }

    [Fact]
    public void Analysera_AllRequirementsMet_NoGap()
    {
        var krav = new[]
        {
            PositionSkillRequirement.Skapa(_position, _hlr, 3),
            PositionSkillRequirement.Skapa(_position, _lakemedel, 3)
        };
        var emp = new[]
        {
            EmployeeSkill.Skapa(_anstall, _hlr, 4),        // överträffar
            EmployeeSkill.Skapa(_anstall, _lakemedel, 3)
        };

        var result = _analyzer.Analysera(_anstall, _position, "Roll", krav, emp, SkillNamn());

        Assert.False(result.HarGap);
        Assert.Empty(result.Gap);
        Assert.Equal(100, result.TackningsgradProcent);
        Assert.Equal(0, result.TotaltGapPoang);
    }

    [Fact]
    public void Analysera_GapsSortedLargestFirst()
    {
        var krav = new[]
        {
            PositionSkillRequirement.Skapa(_position, _hlr, 2),        // gap 1
            PositionSkillRequirement.Skapa(_position, _lakemedel, 5),  // gap 4
            PositionSkillRequirement.Skapa(_position, _journal, 4)     // gap 2
        };
        var emp = new[]
        {
            EmployeeSkill.Skapa(_anstall, _hlr, 1),
            EmployeeSkill.Skapa(_anstall, _lakemedel, 1),
            EmployeeSkill.Skapa(_anstall, _journal, 2)
        };

        var result = _analyzer.Analysera(_anstall, _position, "Roll", krav, emp, SkillNamn());

        Assert.Equal(new[] { 4, 2, 1 }, result.Gap.Select(g => g.GapPoang).ToArray());
        Assert.Equal("Läkemedelshantering", result.Gap[0].SkillNamn);
    }

    [Fact]
    public void Analysera_IgnoresOtherEmployeesSkills()
    {
        var annan = Guid.NewGuid();
        var krav = new[] { PositionSkillRequirement.Skapa(_position, _hlr, 3) };
        var emp = new[]
        {
            EmployeeSkill.Skapa(annan, _hlr, 5),      // annan person — ska ignoreras
            EmployeeSkill.Skapa(_anstall, _hlr, 1)
        };

        var result = _analyzer.Analysera(_anstall, _position, "Roll", krav, emp, SkillNamn());

        var gap = Assert.Single(result.Gap);
        Assert.Equal(1, gap.NuvarandeNiva);
        Assert.Equal(2, gap.GapPoang);
    }

    [Fact]
    public void Analysera_UnknownSkillName_FallsBack()
    {
        var okand = Guid.NewGuid();
        var krav = new[] { PositionSkillRequirement.Skapa(_position, okand, 3) };

        var result = _analyzer.Analysera(_anstall, _position, "Roll", krav,
            Array.Empty<EmployeeSkill>(), SkillNamn());

        Assert.Equal("Okänd kompetens", result.Gap.Single().SkillNamn);
    }
}
