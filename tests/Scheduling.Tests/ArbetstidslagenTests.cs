using RegionHR.Scheduling.Domain;
using RegionHR.Scheduling.Optimization;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Scheduling.Tests;

public class ArbetstidslagenTests
{
    private readonly ArbetstidslagenValidator _sut = new();
    private readonly EmployeeId _anstallId = EmployeeId.New();

    #region Dygnsvila §13

    [Fact]
    public void Dygnsvila_9hVila_Underkant()
    {
        // Pass som slutar 23:00, nästa pass börjar 08:00 = 9h vila = FAIL
        var existing = new List<ShiftAssignment>
        {
            SkapaPass(_anstallId, new DateOnly(2025, 3, 17), new TimeOnly(14, 0), new TimeOnly(23, 0))
        };

        var resultat = _sut.UppfyllerDygnsvila(
            _anstallId,
            new DateOnly(2025, 3, 18),
            new TimeOnly(8, 0),
            existing);

        Assert.False(resultat);
    }

    [Fact]
    public void Dygnsvila_11hVila_Godkant()
    {
        // Pass som slutar 23:00, nästa pass börjar 10:00 = 11h vila = PASS
        var existing = new List<ShiftAssignment>
        {
            SkapaPass(_anstallId, new DateOnly(2025, 3, 17), new TimeOnly(14, 0), new TimeOnly(23, 0))
        };

        var resultat = _sut.UppfyllerDygnsvila(
            _anstallId,
            new DateOnly(2025, 3, 18),
            new TimeOnly(10, 0),
            existing);

        Assert.True(resultat);
    }

    [Fact]
    public void Dygnsvila_NattpassOeverMidnatt_11hVila_Godkant()
    {
        // Nattpass 21:00-07:00 (korsar midnatt), nästa pass 18:00 = 11h vila = PASS
        var existing = new List<ShiftAssignment>
        {
            SkapaPass(_anstallId, new DateOnly(2025, 3, 17), new TimeOnly(21, 0), new TimeOnly(7, 0))
        };

        var resultat = _sut.UppfyllerDygnsvila(
            _anstallId,
            new DateOnly(2025, 3, 18),
            new TimeOnly(18, 0),
            existing);

        Assert.True(resultat);
    }

    [Fact]
    public void Dygnsvila_NattpassOeverMidnatt_8hVila_Underkant()
    {
        // Nattpass 21:00-07:00 (korsar midnatt), nästa pass 15:00 = 8h vila = FAIL
        var existing = new List<ShiftAssignment>
        {
            SkapaPass(_anstallId, new DateOnly(2025, 3, 17), new TimeOnly(21, 0), new TimeOnly(7, 0))
        };

        var resultat = _sut.UppfyllerDygnsvila(
            _anstallId,
            new DateOnly(2025, 3, 18),
            new TimeOnly(15, 0),
            existing);

        Assert.False(resultat);
    }

    [Fact]
    public void Dygnsvila_IngetForegaendePass_Godkant()
    {
        var existing = new List<ShiftAssignment>();

        var resultat = _sut.UppfyllerDygnsvila(
            _anstallId,
            new DateOnly(2025, 3, 18),
            new TimeOnly(7, 0),
            existing);

        Assert.True(resultat);
    }

    #endregion

    #region Veckovila §14

    [Fact]
    public void Veckovila_6ArbetstdagarIRad_7eMasteFri()
    {
        // 6 dagar i rad (mån-lör), nytt pass på söndagen (7:e dagen) raderar veckovilan
        var existing = new List<ShiftAssignment>();
        for (int i = 0; i < 6; i++)
        {
            existing.Add(SkapaPass(
                _anstallId,
                new DateOnly(2025, 3, 17).AddDays(i), // Mån-Lör
                new TimeOnly(7, 0),
                new TimeOnly(16, 0)));
        }

        // Försök lägga till pass på söndagen (7:e dagen) — det NYA passet ska räknas in
        var nyttPass = SkapaPass(_anstallId, new DateOnly(2025, 3, 23), new TimeOnly(7, 0), new TimeOnly(16, 0));
        var resultat = _sut.UppfyllerVeckovila(
            _anstallId,
            new DateOnly(2025, 3, 23), // Söndag
            existing,
            nyttPass);

        Assert.False(resultat);
    }

    [Fact]
    public void Veckovila_5ArbetsdagarPlusNyttLordagspass_Underkant()
    {
        // Mån-fre befintliga pass. Ett NYTT lördagspass gör att längsta sammanhängande
        // vilan i veckan blir lör 16:00 → mån 00:00 = 32h < 36h → brott.
        // (Tidigare bugg: <6 pass i veckan gav alltid true och det nya passet räknades inte in.)
        var existing = new List<ShiftAssignment>();
        for (int i = 0; i < 5; i++)
        {
            existing.Add(SkapaPass(
                _anstallId,
                new DateOnly(2025, 3, 17).AddDays(i), // Mån-Fre
                new TimeOnly(7, 0),
                new TimeOnly(16, 0)));
        }

        var nyttPass = SkapaPass(_anstallId, new DateOnly(2025, 3, 22), new TimeOnly(7, 0), new TimeOnly(16, 0));
        var resultat = _sut.UppfyllerVeckovila(
            _anstallId,
            new DateOnly(2025, 3, 22), // Lördag
            existing,
            nyttPass);

        Assert.False(resultat);
    }

    [Fact]
    public void Veckovila_4ArbetsdagarPlusNyttFredagspass_Godkant()
    {
        // Mån-tors befintliga pass + nytt fredagspass → lör+sön fria
        // = fre 16:00 → mån 00:00 = 56h sammanhängande vila ≥ 36h → OK.
        var existing = new List<ShiftAssignment>();
        for (int i = 0; i < 4; i++)
        {
            existing.Add(SkapaPass(
                _anstallId,
                new DateOnly(2025, 3, 17).AddDays(i), // Mån-Tors
                new TimeOnly(7, 0),
                new TimeOnly(16, 0)));
        }

        var nyttPass = SkapaPass(_anstallId, new DateOnly(2025, 3, 21), new TimeOnly(7, 0), new TimeOnly(16, 0));
        var resultat = _sut.UppfyllerVeckovila(
            _anstallId,
            new DateOnly(2025, 3, 21), // Fredag
            existing,
            nyttPass);

        Assert.True(resultat);
    }

    #endregion

    #region Max veckoarbetstid §5

    [Fact]
    public void MaxVeckoarbetstid_40h_Godkant()
    {
        // 5 x 8h = 40h OK
        var existing = new List<ShiftAssignment>();
        for (int i = 0; i < 5; i++)
        {
            existing.Add(SkapaPass(
                _anstallId,
                new DateOnly(2025, 3, 17).AddDays(i),
                new TimeOnly(7, 0),
                new TimeOnly(16, 0),
                TimeSpan.FromHours(1))); // 8h netto
        }

        var resultat = _sut.InomMaxArbetstid(
            _anstallId,
            new DateOnly(2025, 3, 17),
            0m, // Inga ytterligare timmar
            existing);

        Assert.True(resultat);
    }

    [Fact]
    public void MaxVeckoarbetstid_44h_Underkant()
    {
        // 5 x 8h = 40h, + 4h mer = 44h FAIL
        var existing = new List<ShiftAssignment>();
        for (int i = 0; i < 5; i++)
        {
            existing.Add(SkapaPass(
                _anstallId,
                new DateOnly(2025, 3, 17).AddDays(i),
                new TimeOnly(7, 0),
                new TimeOnly(16, 0),
                TimeSpan.FromHours(1))); // 8h netto
        }

        // Försök lägga till 4h extra på lördag
        var resultat = _sut.InomMaxArbetstid(
            _anstallId,
            new DateOnly(2025, 3, 22), // Lördag, samma vecka
            4m,
            existing);

        Assert.False(resultat);
    }

    [Fact]
    public void MaxVeckoarbetstid_38hPlus2h_Godkant()
    {
        // 4 x 8h = 32h, + 2h pass = 34h, well under 40h
        var existing = new List<ShiftAssignment>();
        for (int i = 0; i < 4; i++)
        {
            existing.Add(SkapaPass(
                _anstallId,
                new DateOnly(2025, 3, 17).AddDays(i),
                new TimeOnly(7, 0),
                new TimeOnly(16, 0),
                TimeSpan.FromHours(1)));
        }

        var resultat = _sut.InomMaxArbetstid(
            _anstallId,
            new DateOnly(2025, 3, 21), // Fredag
            8m,
            existing);

        Assert.True(resultat);
    }

    #endregion

    #region Nattarbetstid §13a

    [Fact]
    public void Nattarbetsgrans_8hNattpass_Godkant()
    {
        // Nattpass 22:00-07:00 med 1h rast = 8h netto, allt under nattperiod
        var nattPass = SkapaPass(
            _anstallId,
            new DateOnly(2025, 3, 17),
            new TimeOnly(22, 0),
            new TimeOnly(7, 0),
            TimeSpan.FromHours(1));

        var resultat = _sut.InomNattarbetsgrans(
            _anstallId,
            new DateOnly(2025, 3, 17),
            nattPass,
            []);

        Assert.True(resultat);
    }

    [Fact]
    public void Nattarbetsgrans_LangtNattpass_Underkant()
    {
        // Nattpass 20:00-07:00 med 30 min rast = 10.5h, varav nattdelen överstiger 8h
        var nattPass = SkapaPass(
            _anstallId,
            new DateOnly(2025, 3, 17),
            new TimeOnly(20, 0),
            new TimeOnly(7, 0),
            TimeSpan.FromMinutes(30));

        var resultat = _sut.InomNattarbetsgrans(
            _anstallId,
            new DateOnly(2025, 3, 17),
            nattPass,
            []);

        Assert.False(resultat);
    }

    [Fact]
    public void Nattarbetsgrans_DagpassInteNatt_Godkant()
    {
        // Dagpass 07:00-16:00 — inte nattarbete, alltid OK
        var dagPass = SkapaPass(
            _anstallId,
            new DateOnly(2025, 3, 17),
            new TimeOnly(7, 0),
            new TimeOnly(16, 0));

        var resultat = _sut.InomNattarbetsgrans(
            _anstallId,
            new DateOnly(2025, 3, 17),
            dagPass,
            []);

        Assert.True(resultat);
    }

    #endregion

    #region ValidateShift (integration)

    [Fact]
    public void ValidateShift_SvenskaBeskrivningar()
    {
        // Kontrollera att valideringsresultat innehåller svenska beskrivningar
        var existing = new List<ShiftAssignment>
        {
            SkapaPass(_anstallId, new DateOnly(2025, 3, 17), new TimeOnly(14, 0), new TimeOnly(23, 0))
        };

        var newShift = SkapaPass(_anstallId, new DateOnly(2025, 3, 18), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var resultat = _sut.ValidateShift(_anstallId, newShift, existing);

        Assert.False(resultat.ArGiltigt);
        Assert.NotEmpty(resultat.Overtraldelser);
        Assert.Contains("Dygnsvila", resultat.Overtraldelser[0].Regel);
        Assert.Contains("timmar", resultat.Overtraldelser[0].Beskrivning);
    }

    [Fact]
    public void ValidateShift_FleraOvertradelserSamtidigt()
    {
        // Skapa en situation med flera brott: dygnsvila OCH max veckoarbetstid
        var existing = new List<ShiftAssignment>();

        // 5 x 8h = 40h under veckan
        for (int i = 0; i < 5; i++)
        {
            existing.Add(SkapaPass(
                _anstallId,
                new DateOnly(2025, 3, 17).AddDays(i),
                new TimeOnly(7, 0),
                new TimeOnly(16, 0),
                TimeSpan.FromHours(1)));
        }

        // Kvällspass fredag som slutar 23:00
        existing.Add(SkapaPass(
            _anstallId,
            new DateOnly(2025, 3, 21), // Fredag
            new TimeOnly(17, 0),
            new TimeOnly(23, 0),
            TimeSpan.FromMinutes(30)));

        // Försök lägga till pass lördag morgon 06:00 — bryter dygnsvila (7h vila)
        // och kan bryta max veckoarbetstid
        var newShift = SkapaPass(
            _anstallId,
            new DateOnly(2025, 3, 22), // Lördag
            new TimeOnly(6, 0),
            new TimeOnly(14, 0),
            TimeSpan.FromMinutes(30));

        var resultat = _sut.ValidateShift(_anstallId, newShift, existing);

        Assert.False(resultat.ArGiltigt);
        // Förväntar minst dygnsvila-brott
        Assert.True(resultat.Overtraldelser.Count >= 1);
        Assert.Contains(resultat.Overtraldelser, v => v.Regel.Contains("Dygnsvila"));
    }

    [Fact]
    public void ValidateShift_AllViolationsAreHard()
    {
        var existing = new List<ShiftAssignment>
        {
            SkapaPass(_anstallId, new DateOnly(2025, 3, 17), new TimeOnly(14, 0), new TimeOnly(23, 0))
        };

        var newShift = SkapaPass(_anstallId, new DateOnly(2025, 3, 18), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var resultat = _sut.ValidateShift(_anstallId, newShift, existing);

        Assert.All(resultat.Overtraldelser, v => Assert.Equal(ViolationSeverity.Hard, v.Allvarlighet));
    }

    #endregion

    #region ValidateSchedule (helschema)

    [Fact]
    public void ValidateSchedule_GiltigtSchema_IngetBrott()
    {
        var assignments = new List<ShiftAssignment>();
        for (int i = 0; i < 5; i++)
        {
            assignments.Add(SkapaPass(
                _anstallId,
                new DateOnly(2025, 3, 17).AddDays(i),
                new TimeOnly(7, 0),
                new TimeOnly(16, 0),
                TimeSpan.FromHours(1)));
        }

        var period = new DateRange(new DateOnly(2025, 3, 17), new DateOnly(2025, 3, 23));
        var resultat = _sut.ValidateSchedule(assignments, period);

        Assert.True(resultat.ArGiltigt);
        Assert.Empty(resultat.Overtraldelser);
    }

    [Fact]
    public void ValidateSchedule_DygnsvilaBrott_DetekterasISchemat()
    {
        var assignments = new List<ShiftAssignment>
        {
            SkapaPass(_anstallId, new DateOnly(2025, 3, 17), new TimeOnly(14, 0), new TimeOnly(23, 0)),
            SkapaPass(_anstallId, new DateOnly(2025, 3, 18), new TimeOnly(7, 0), new TimeOnly(16, 0))
        };

        var period = new DateRange(new DateOnly(2025, 3, 17), new DateOnly(2025, 3, 18));
        var resultat = _sut.ValidateSchedule(assignments, period);

        Assert.False(resultat.ArGiltigt);
        Assert.Contains(resultat.Overtraldelser, v => v.Regel.Contains("Dygnsvila"));
    }

    #endregion

    #region Sjukvårdsundantag dygnsvila 9h

    [Fact]
    public void Dygnsvila_Sjukvard_9hVila_Godkant()
    {
        // Med sjukvårdsundantag: 9h vila är tillräckligt
        var sjukvardValidator = new ArbetstidslagenValidator(arSjukvard: true);
        var existing = new List<ShiftAssignment>
        {
            SkapaPass(_anstallId, new DateOnly(2025, 3, 17), new TimeOnly(14, 0), new TimeOnly(23, 0))
        };

        var resultat = sjukvardValidator.UppfyllerDygnsvila(
            _anstallId,
            new DateOnly(2025, 3, 18),
            new TimeOnly(8, 0),  // 9h vila (23:00 -> 08:00)
            existing);

        Assert.True(resultat);
    }

    [Fact]
    public void Dygnsvila_Sjukvard_8hVila_Underkant()
    {
        // Med sjukvårdsundantag: 8h vila är fortfarande otillräckligt
        var sjukvardValidator = new ArbetstidslagenValidator(arSjukvard: true);
        var existing = new List<ShiftAssignment>
        {
            SkapaPass(_anstallId, new DateOnly(2025, 3, 17), new TimeOnly(14, 0), new TimeOnly(23, 0))
        };

        var resultat = sjukvardValidator.UppfyllerDygnsvila(
            _anstallId,
            new DateOnly(2025, 3, 18),
            new TimeOnly(7, 0),  // 8h vila (23:00 -> 07:00)
            existing);

        Assert.False(resultat);
    }

    [Fact]
    public void Dygnsvila_UtanSjukvard_9hVila_Underkant()
    {
        // Utan sjukvårdsundantag: 9h vila är otillräckligt (kräver 11h)
        var standardValidator = new ArbetstidslagenValidator(arSjukvard: false);
        var existing = new List<ShiftAssignment>
        {
            SkapaPass(_anstallId, new DateOnly(2025, 3, 17), new TimeOnly(14, 0), new TimeOnly(23, 0))
        };

        var resultat = standardValidator.UppfyllerDygnsvila(
            _anstallId,
            new DateOnly(2025, 3, 18),
            new TimeOnly(8, 0),  // 9h vila
            existing);

        Assert.False(resultat);
    }

    [Fact]
    public void EffektivtMinDygnsvila_Sjukvard_Ar9()
    {
        var sjukvardValidator = new ArbetstidslagenValidator(arSjukvard: true);
        Assert.Equal(9, sjukvardValidator.EffektivtMinDygnsvila);
        Assert.True(sjukvardValidator.ArSjukvard);
    }

    [Fact]
    public void EffektivtMinDygnsvila_Standard_Ar11()
    {
        var standardValidator = new ArbetstidslagenValidator();
        Assert.Equal(11, standardValidator.EffektivtMinDygnsvila);
        Assert.False(standardValidator.ArSjukvard);
    }

    [Fact]
    public void ValidateShift_Sjukvard_9hVila_IngetBrott()
    {
        // Komplett ValidateShift med sjukvårdsundantag
        var sjukvardValidator = new ArbetstidslagenValidator(arSjukvard: true);
        var existing = new List<ShiftAssignment>
        {
            SkapaPass(_anstallId, new DateOnly(2025, 3, 17), new TimeOnly(14, 0), new TimeOnly(23, 0))
        };

        var newShift = SkapaPass(_anstallId, new DateOnly(2025, 3, 18), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var resultat = sjukvardValidator.ValidateShift(_anstallId, newShift, existing);

        // 9h vila med sjukvårdsundantag = OK
        Assert.True(resultat.ArGiltigt);
    }

    #endregion

    #region Hjälpmetoder

    private static ShiftAssignment SkapaPass(
        EmployeeId anstallId,
        DateOnly datum,
        TimeOnly start,
        TimeOnly slut,
        TimeSpan? rast = null)
    {
        return new ShiftAssignment
        {
            AnstallId = anstallId,
            Datum = datum,
            PassTyp = start >= new TimeOnly(21, 0) || slut <= new TimeOnly(7, 0) && slut > TimeOnly.MinValue
                ? ShiftType.Natt
                : start >= new TimeOnly(14, 0)
                    ? ShiftType.Kvall
                    : ShiftType.Dag,
            Start = start,
            Slut = slut,
            Rast = rast ?? TimeSpan.FromMinutes(30)
        };
    }

    #endregion
}
