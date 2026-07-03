using RegionHR.Scheduling.Domain;
using RegionHR.Scheduling.Optimization;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Scheduling.Tests;

/// <summary>
/// Tester för spar-vägarna bakom UI:t:
/// (1) Schemaoptimeringen (Optimering.razor) ska kunna materialisera lösarens
///     tilldelningar till ett <see cref="Schedule"/> med faktiska <see cref="ScheduledShift"/>.
/// (2) Bemanningsmallar (Bemanningsmallar.razor) ska byggas ur veckodagsrader och
///     persistera dem, med bevarad giltighet och kompetenskrav, samt hålla invarianten
///     att optimalbemanning aldrig får understiga minimibemanning.
/// </summary>
public class BemanningsmallOchOptimeringSparaTests
{
    private readonly OrganizationId _enhet = OrganizationId.New();

    // 2025-03-17 = måndag.
    private static readonly DateOnly Mandag = new(2025, 3, 17);
    private static readonly DateOnly Fredag = new(2025, 3, 21);

    [Fact]
    public void Optimering_SparaPath_MaterialiserarAllaTilldelningarSomPass()
    {
        // Kör lösaren precis som sidan gör (dag- och kvällspass mån–fre).
        var personal = new[]
        {
            NyPerson(), NyPerson(), NyPerson(), NyPerson()
        };
        var behov = new List<StaffingRequirement>();
        for (var d = Mandag; d <= Fredag; d = d.AddDays(1))
        {
            behov.Add(new StaffingRequirement { Datum = d, PassTyp = ShiftType.Dag, Start = new TimeOnly(7, 0), Slut = new TimeOnly(16, 0), Rast = TimeSpan.FromMinutes(60), AntalBehov = 2 });
            behov.Add(new StaffingRequirement { Datum = d, PassTyp = ShiftType.Kvall, Start = new TimeOnly(15, 0), Slut = new TimeOnly(22, 0), Rast = TimeSpan.FromMinutes(30), AntalBehov = 1 });
        }
        var problem = new ScheduleProblem
        {
            EnhetId = _enhet,
            Period = new DateRange(Mandag, Fredag),
            PassBehov = behov,
            TillgangligPersonal = [.. personal]
        };

        var solution = new ConstraintScheduleSolver().Solve(problem);
        Assert.NotEmpty(solution.Tilldelningar);

        // Spar-vägen (identisk med Optimering.razor.SparaSchema).
        var schema = Schedule.SkapaPeriodschema(_enhet, "Optimering", Mandag, Fredag);
        foreach (var a in solution.Tilldelningar)
            schema.LaggTillPass(a.AnstallId, a.Datum, a.PassTyp, a.Start, a.Slut, a.Rast);

        Assert.Equal(ScheduleType.Periodschema, schema.Typ);
        Assert.Equal(ScheduleStatus.Utkast, schema.Status);
        Assert.Equal(solution.Tilldelningar.Count, schema.Pass.Count);

        // Varje tilldelning ska finnas som ett planerat pass med bevarad tid och person.
        foreach (var a in solution.Tilldelningar)
        {
            Assert.Contains(schema.Pass, p =>
                p.AnstallId == a.AnstallId &&
                p.Datum == a.Datum &&
                p.PassTyp == a.PassTyp &&
                p.PlaneradStart == a.Start &&
                p.PlaneradSlut == a.Slut &&
                p.Rast == a.Rast &&
                p.Status == ShiftStatus.Planerad);
        }
    }

    [Fact]
    public void Bemanningsmall_ByggMedRader_BevararRaderKompetensOchGiltighet()
    {
        var mall = StaffingTemplate.Skapa(_enhet, "Avd 3 grundbemanning", Mandag);
        mall.LaggTillRad(new StaffingRequirementLine
        {
            Veckodag = DayOfWeek.Monday,
            PassTyp = ShiftType.Dag,
            Start = new TimeOnly(7, 0),
            Slut = new TimeOnly(16, 0),
            Rast = TimeSpan.FromMinutes(60),
            MinAntal = 1,
            OptimalAntal = 2,
            KravdaKompetenser = ["Sjuksköterska", "HLR"]
        });
        mall.LaggTillRad(new StaffingRequirementLine
        {
            Veckodag = DayOfWeek.Monday,
            PassTyp = ShiftType.Natt,
            Start = new TimeOnly(21, 30),
            Slut = new TimeOnly(7, 30),
            Rast = TimeSpan.FromMinutes(60),
            MinAntal = 1,
            OptimalAntal = 1
        });

        Assert.Equal(2, mall.Rader.Count);
        Assert.True(mall.Giltighet.IsOpenEnded);
        Assert.Equal(Mandag, mall.Giltighet.Start);
        Assert.Contains("Sjuksköterska", mall.Rader[0].KravdaKompetenser);
        // Nattpass över midnatt: 07:30 nästa dag − 21:30 − 1h rast = 9h netto.
        Assert.Equal(9m, mall.Rader[1].PlaneradeTimmar);
    }

    [Fact]
    public void Bemanningsmall_MedGiltigTom_SatterSlutdatum()
    {
        var tom = Mandag.AddDays(90);
        var mall = StaffingTemplate.Skapa(_enhet, "Sommarbemanning", Mandag, tom);

        Assert.False(mall.Giltighet.IsOpenEnded);
        Assert.Equal(tom, mall.Giltighet.End);
    }

    [Fact]
    public void Bemanningsmall_OptimalMindreAnMin_KastarUndantag()
    {
        // Invarianten som UI:t (Bemanningsmallar.razor) skyddar mot: optimal < min.
        var mall = StaffingTemplate.Skapa(_enhet, "Ogiltig", Mandag);

        Assert.Throws<ArgumentException>(() => mall.LaggTillRad(new StaffingRequirementLine
        {
            Veckodag = DayOfWeek.Monday,
            PassTyp = ShiftType.Dag,
            Start = new TimeOnly(7, 0),
            Slut = new TimeOnly(16, 0),
            Rast = TimeSpan.FromMinutes(60),
            MinAntal = 2,
            OptimalAntal = 1
        }));
    }

    private static PersonalInfo NyPerson() => new()
    {
        AnstallId = EmployeeId.New(),
        Namn = "Test",
        Sysselsattningsgrad = 100m,
        Kompetenser = [],
        LedigaDagar = []
    };
}
