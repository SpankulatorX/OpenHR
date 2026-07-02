using RegionHR.Scheduling.Domain;
using RegionHR.Scheduling.Optimization;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Scheduling.Tests;

/// <summary>
/// Tester för <see cref="BehovsstyrdSchemaGenerator"/>: att auto-genererade scheman
/// (1) täcker bemanningsbehovet så långt personalen räcker, (2) rapporterar under-/
/// överbemanning ärligt och (3) aldrig bryter mot Arbetstidslagen (hellre obemannat pass
/// än lagbrott), inklusive sjukvårdsundantaget för dygnsvila (9h).
/// </summary>
public class BehovsstyrdSchemaGeneratorTests
{
    // 2025-03-17 = måndag, 03-21 = fredag, 03-22 = lördag, 03-23 = söndag.
    private static readonly DateOnly Mandag = new(2025, 3, 17);
    private static readonly DateOnly Tisdag = new(2025, 3, 18);
    private static readonly DateOnly Fredag = new(2025, 3, 21);
    private static readonly DateOnly Lordag = new(2025, 3, 22);
    private static readonly DateOnly Sondag = new(2025, 3, 23);

    private static StaffingRequirementLine DagLinje(DayOfWeek dag, int min, int optimal) => new()
    {
        Veckodag = dag,
        PassTyp = ShiftType.Dag,
        Start = new TimeOnly(7, 0),
        Slut = new TimeOnly(16, 0),
        Rast = TimeSpan.FromMinutes(60),
        MinAntal = min,
        OptimalAntal = optimal
    };

    private static PersonalInfo Person(params string[] kompetenser) => new()
    {
        AnstallId = EmployeeId.New(),
        Namn = "Test",
        Sysselsattningsgrad = 100m,
        Kompetenser = [.. kompetenser]
    };

    private static BehovsstyrdSchemaRequest Request(
        IReadOnlyList<StaffingRequirementLine> rader,
        IReadOnlyList<PersonalInfo> personal,
        DateOnly fran,
        DateOnly till,
        bool sjukvard = false,
        BemanningsMal mal = BemanningsMal.Optimal) => new()
    {
        EnhetId = OrganizationId.New(),
        Period = new DateRange(fran, till),
        Behovsrader = rader,
        TillgangligPersonal = personal,
        ArSjukvard = sjukvard,
        Mal = mal
    };

    [Fact]
    public void Generera_TillrackligPersonal_GerFullTackningOchATLKompliant()
    {
        // Dagpass mån–fre, optimal 2 per pass, gott om personal.
        var rader = new[]
        {
            DagLinje(DayOfWeek.Monday, 1, 2),
            DagLinje(DayOfWeek.Tuesday, 1, 2),
            DagLinje(DayOfWeek.Wednesday, 1, 2),
            DagLinje(DayOfWeek.Thursday, 1, 2),
            DagLinje(DayOfWeek.Friday, 1, 2)
        };
        var personal = new[] { Person(), Person(), Person(), Person() };

        var forslag = new BehovsstyrdSchemaGenerator()
            .Generera(Request(rader, personal, Mandag, Fredag));

        Assert.True(forslag.ATLKompliant);
        Assert.Empty(forslag.ATLVarningar);
        Assert.True(forslag.FullTackning);
        Assert.Equal(100m, forslag.TackningsgradProcent);
        Assert.Equal(5, forslag.Tackning.Count);
        Assert.All(forslag.Tackning, t => Assert.Equal(BemanningsLage.Balanserad, t.Lage));
        Assert.All(forslag.Tackning, t => Assert.Equal(2, t.Tillsatt)); // optimalnivå nådd
        Assert.Equal(10, forslag.Tilldelningar.Count);
        Assert.True(forslag.RattviseScore >= 0);
    }

    [Fact]
    public void Generera_ForLitePersonal_RapporterarUnderbemanningMenForblirLaglig()
    {
        // Kräver 2 per dag men bara en person finns → underbemannat, men aldrig lagbrott.
        var rader = new[]
        {
            DagLinje(DayOfWeek.Monday, 2, 2),
            DagLinje(DayOfWeek.Tuesday, 2, 2),
            DagLinje(DayOfWeek.Wednesday, 2, 2),
            DagLinje(DayOfWeek.Thursday, 2, 2),
            DagLinje(DayOfWeek.Friday, 2, 2)
        };
        var personal = new[] { Person() };

        var forslag = new BehovsstyrdSchemaGenerator()
            .Generera(Request(rader, personal, Mandag, Fredag));

        Assert.True(forslag.ATLKompliant);        // fortfarande lagligt
        Assert.False(forslag.FullTackning);        // men inte fullbemannat
        Assert.Equal(5, forslag.Underbemannade.Count);
        Assert.Equal(50m, forslag.TackningsgradProcent); // 5 av 10 behov täckta
        Assert.Equal(5, forslag.Tilldelningar.Count);     // max 40h/vecka = 5 dagpass
        Assert.All(forslag.Tackning, t => Assert.Equal(1, t.UnderskottMotMin));
    }

    [Fact]
    public void Generera_DygnsvilaBryts_LamnarPassObemannatIStalletForLagbrott()
    {
        // Kvällspass (15–23) måndag följt av dagpass (07–16) tisdag = 8h vila < 11h.
        var rader = new[]
        {
            new StaffingRequirementLine
            {
                Veckodag = DayOfWeek.Monday, PassTyp = ShiftType.Kvall,
                Start = new TimeOnly(15, 0), Slut = new TimeOnly(23, 0),
                Rast = TimeSpan.FromMinutes(30), MinAntal = 1, OptimalAntal = 1
            },
            DagLinje(DayOfWeek.Tuesday, 1, 1)
        };
        var personal = new[] { Person() };

        var forslag = new BehovsstyrdSchemaGenerator()
            .Generera(Request(rader, personal, Mandag, Tisdag));

        Assert.True(forslag.ATLKompliant);          // inget lagbrott
        Assert.Empty(forslag.ATLVarningar);
        Assert.Single(forslag.Tilldelningar);        // bara ett av två pass kunde bemannas
        Assert.Single(forslag.Underbemannade);
    }

    [Fact]
    public void Generera_Sjukvardsundantag_9h_GerHogreTackningAn_11h()
    {
        // Kväll 14–22 måndag + dag 07–16 tisdag = exakt 9h vila.
        // 11h-profil förbjuder andra passet; 9h-vårdprofil tillåter det.
        StaffingRequirementLine[] Rader() =>
        [
            new StaffingRequirementLine
            {
                Veckodag = DayOfWeek.Monday, PassTyp = ShiftType.Kvall,
                Start = new TimeOnly(14, 0), Slut = new TimeOnly(22, 0),
                Rast = TimeSpan.FromMinutes(30), MinAntal = 1, OptimalAntal = 1
            },
            DagLinje(DayOfWeek.Tuesday, 1, 1)
        ];

        var standardPerson = new[] { Person() };
        var vardPerson = new[] { Person() };

        var standard = new BehovsstyrdSchemaGenerator()
            .Generera(Request(Rader(), standardPerson, Mandag, Tisdag, sjukvard: false));
        var vard = new BehovsstyrdSchemaGenerator()
            .Generera(Request(Rader(), vardPerson, Mandag, Tisdag, sjukvard: true));

        Assert.Equal(1, standard.TotaltTillsatt);   // 9h < 11h → ett pass obemannat
        Assert.Equal(2, vard.TotaltTillsatt);        // 9h >= 9h → båda bemannade
        Assert.True(vard.FullTackning);
        Assert.True(vard.ATLKompliant);
        Assert.True(vard.TotaltTillsatt > standard.TotaltTillsatt);
    }

    [Fact]
    public void Generera_HelgpassFordelasRattvistMellanPersonal()
    {
        var rader = new[]
        {
            DagLinje(DayOfWeek.Saturday, 1, 1),
            DagLinje(DayOfWeek.Sunday, 1, 1)
        };
        var personal = new[] { Person(), Person() };

        var forslag = new BehovsstyrdSchemaGenerator()
            .Generera(Request(rader, personal, Lordag, Sondag));

        Assert.True(forslag.FullTackning);
        Assert.Equal(2, forslag.Tilldelningar.Count);
        // Rättvist: de två helgpassen ska hamna på olika personer.
        Assert.Equal(2, forslag.Tilldelningar.Select(t => t.AnstallId).Distinct().Count());
    }

    [Fact]
    public void Generera_KompetenskravStyrTilldelning()
    {
        var rader = new[]
        {
            new StaffingRequirementLine
            {
                Veckodag = DayOfWeek.Monday, PassTyp = ShiftType.Dag,
                Start = new TimeOnly(7, 0), Slut = new TimeOnly(16, 0),
                Rast = TimeSpan.FromMinutes(60), MinAntal = 1, OptimalAntal = 1,
                KravdaKompetenser = ["Sjuksköterska"]
            }
        };
        var underskoterska = Person("Undersköterska");
        var sjukskoterska = Person("Sjuksköterska", "HLR");

        var forslag = new BehovsstyrdSchemaGenerator()
            .Generera(Request(rader, [underskoterska, sjukskoterska], Mandag, Mandag));

        Assert.True(forslag.FullTackning);
        Assert.Single(forslag.Tilldelningar);
        Assert.Equal(sjukskoterska.AnstallId, forslag.Tilldelningar[0].AnstallId);
        Assert.Contains("Sjuksköterska", forslag.Tackning[0].KravdaKompetenser);
    }

    [Fact]
    public void Generera_MinimumMal_SiktarBaraMotLagstaNivan()
    {
        // Optimal 3 men mål = Minimum(1) → bara ett pass tillsätts trots att fler finns.
        var rader = new[] { DagLinje(DayOfWeek.Monday, 1, 3) };
        var personal = new[] { Person(), Person(), Person() };

        var forslag = new BehovsstyrdSchemaGenerator()
            .Generera(Request(rader, personal, Mandag, Mandag, mal: BemanningsMal.Minimum));

        Assert.Single(forslag.Tilldelningar);
        Assert.Equal(1, forslag.Tackning[0].Tillsatt);
        Assert.True(forslag.FullTackning);            // minimikrav uppfyllt
        Assert.Equal(BemanningsLage.Balanserad, forslag.Tackning[0].Lage);
    }

    [Fact]
    public void PassTackning_KlassificerarUnderOchOverbemanning()
    {
        var under = new PassTackning { MinBehov = 2, OptimalBehov = 3, Tillsatt = 1 };
        var balans = new PassTackning { MinBehov = 2, OptimalBehov = 3, Tillsatt = 3 };
        var over = new PassTackning { MinBehov = 1, OptimalBehov = 2, Tillsatt = 4 };

        Assert.Equal(BemanningsLage.Underbemannad, under.Lage);
        Assert.Equal(1, under.UnderskottMotMin);
        Assert.Equal(BemanningsLage.Balanserad, balans.Lage);
        Assert.Equal(BemanningsLage.Overbemannad, over.Lage);
        Assert.Equal(2, over.OverskottMotOptimal);
    }

    [Fact]
    public void FranMall_ByggerRequestUrBemanningsmall()
    {
        var enhet = OrganizationId.New();
        var mall = StaffingTemplate.Skapa(enhet, "Avd 3 grundbemanning", Mandag);
        mall.LaggTillRad(DagLinje(DayOfWeek.Monday, 1, 1));
        var personal = new[] { Person() };

        var request = BehovsstyrdSchemaRequest.FranMall(
            mall, new DateRange(Mandag, Mandag), personal);
        var forslag = new BehovsstyrdSchemaGenerator().Generera(request);

        Assert.Equal(enhet, request.EnhetId);
        Assert.Single(forslag.Tilldelningar);
        Assert.True(forslag.FullTackning);
        Assert.True(forslag.ATLKompliant);
    }

    [Fact]
    public void Generera_PeriodUtanSlutdatum_KastarUndantag()
    {
        var request = new BehovsstyrdSchemaRequest
        {
            EnhetId = OrganizationId.New(),
            Period = DateRange.Infinite(Mandag),
            Behovsrader = [DagLinje(DayOfWeek.Monday, 1, 1)],
            TillgangligPersonal = [Person()]
        };

        Assert.Throws<ArgumentException>(() => new BehovsstyrdSchemaGenerator().Generera(request));
    }
}
