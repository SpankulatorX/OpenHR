using Xunit;
using RegionHR.Agreements.Domain;
using RegionHR.Core.Contracts;
using RegionHR.Payroll.Domain;
using RegionHR.Payroll.Engine;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Payroll.Tests;

/// <summary>
/// Enhetstester för löneberäkningsmotorn.
/// Testar hela brutto-till-netto-pipelinen med svenska skatteregler och kollektivavtal.
/// </summary>
public class PayrollCalculationEngineTests
{
    private readonly PayrollCalculationEngine _engine;
    private readonly MockTaxTableProvider _taxProvider;
    private readonly MockCollectiveAgreementRulesEngine _rulesEngine;
    private readonly MockCoreHRModule _coreHR;

    // Standard testmedarbetare
    private readonly EmployeeId _employeeId = EmployeeId.New();
    private readonly EmploymentId _employmentId = EmploymentId.New();
    private readonly PayrollRunId _runId = PayrollRunId.New();
    private readonly OrganizationId _unitId = OrganizationId.New();

    public PayrollCalculationEngineTests()
    {
        _taxProvider = new MockTaxTableProvider();
        _rulesEngine = new MockCollectiveAgreementRulesEngine();
        _coreHR = new MockCoreHRModule();
        _engine = new PayrollCalculationEngine(_taxProvider, _rulesEngine, _coreHR);
    }

    private void SetupStandardEmployee(
        decimal manadslon = 35000m,
        decimal sysselsattningsgrad = 100m,
        string personnummerMaskerat = "19850515-****",
        int? skattetabell = 33,
        int? skattekolumn = 1,
        bool harJamkning = false,
        decimal? jamkningBelopp = null)
    {
        _coreHR.Employee = new EmployeeDto(
            _employeeId, "Test", "Testsson", personnummerMaskerat, "test@test.se",
            skattetabell, skattekolumn, "Göteborg", 32.00m, false, null,
            harJamkning, jamkningBelopp);

        _coreHR.Employment = new EmploymentDto(
            _employmentId, _employeeId, _unitId,
            EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            manadslon, sysselsattningsgrad,
            new DateOnly(2020, 1, 1), null, "201100");
    }

    #region Grundlön

    [Fact]
    public async Task CalculateAsync_FullManad_ReturnsCorrectGrundlon()
    {
        SetupStandardEmployee(manadslon: 35000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            Kostnadsstalle = "1234"
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Full månadslön = 35000
        var grundlonRad = result.Rader.First(r => r.LoneartKod == "1100");
        Assert.Equal(35000m, grundlonRad.Belopp.Amount);
    }

    [Fact]
    public async Task CalculateAsync_DelAvManad_ProportionalGrundlon()
    {
        SetupStandardEmployee(manadslon: 35000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 10,
            ArbetsdagarIManadens = 21,
            Kostnadsstalle = "1234"
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Proportionell: 35000 * 10/21
        var expected = Math.Round(35000m * 10m / 21m, 2);
        var grundlonRad = result.Rader.First(r => r.LoneartKod == "1100");
        Assert.Equal(expected, Math.Round(grundlonRad.Belopp.Amount, 2));
    }

    [Fact]
    public async Task CalculateAsync_Deltid75Procent_CorrectGrundlon()
    {
        SetupStandardEmployee(manadslon: 35000m, sysselsattningsgrad: 75m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Grundlön = 35000 * 75/100 = 26250
        var grundlonRad = result.Rader.First(r => r.LoneartKod == "1100");
        Assert.Equal(26250m, grundlonRad.Belopp.Amount);
    }

    #endregion

    #region OB-tillägg

    [Fact]
    public async Task CalculateAsync_MedOBTimmar_ReturnsCorrectOBTillagg()
    {
        SetupStandardEmployee();
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            OBTimmar =
            [
                new OBInput { Kategori = OBCategory.VardagKvall, Timmar = 10 },
                new OBInput { Kategori = OBCategory.VardagNatt, Timmar = 5 },
                new OBInput { Kategori = OBCategory.Storhelg, Timmar = 8 }
            ]
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // AB satser: VardagKvall=126.50, VardagNatt=152.00, Storhelg=195.00
        var expectedOB = 10 * 126.50m + 5 * 152.00m + 8 * 195.00m;
        Assert.Equal(expectedOB, result.OBTillagg.Amount);

        var obRader = result.Rader.Where(r => r.LoneartKod == "1310").ToList();
        Assert.Equal(3, obRader.Count);
    }

    [Theory]
    [InlineData(OBCategory.VardagKvall, 126.50)]
    [InlineData(OBCategory.VardagNatt, 152.00)]
    [InlineData(OBCategory.Helg, 89.00)]
    [InlineData(OBCategory.Storhelg, 195.00)]
    public async Task CalculateAsync_OBPerKategori_CorrectRate(OBCategory kategori, decimal expectedRate)
    {
        SetupStandardEmployee();
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            OBTimmar = [new OBInput { Kategori = kategori, Timmar = 10 }]
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        Assert.Equal(10 * expectedRate, result.OBTillagg.Amount);
    }

    [Fact]
    public async Task CalculateAsync_OBHelgMedNattTimmar_AnvanderNatthojdSats()
    {
        // AB § 21 anm.: O-tillägg B (helg) höjs kl. 22–06. Natt-timmarna ska prissättas
        // med den kanoniska natthöjda satsen (2025: 76,00 kr/h), inte grundsatsen.
        SetupStandardEmployee();
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            OBTimmar = [new OBInput { Kategori = OBCategory.Helg, Timmar = 10, NattTimmar = 4 }]
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 6, input);

        // Dagdel: 6 h × grundsats (mock: 89,00). Nattdel: 4 h × kanonisk natthöjd B-sats.
        var nattsats = ABOTillaggSatser.Nattsats(OBCategory.Helg, new DateOnly(2025, 6, 1));
        var expected = 6 * 89.00m + 4 * nattsats;
        Assert.Equal(expected, result.OBTillagg.Amount);

        var nattRad = result.Rader.First(r => r.Benamning == "OB-tillägg Helg natt");
        Assert.Equal(4m, nattRad.Antal);
        Assert.Equal(nattsats, nattRad.Sats.Amount);
    }

    [Fact]
    public async Task CalculateAsync_OBVardagNatt_NattTimmarAndrarInteSatsen()
    {
        // Vardagsnatt (C) saknar natthöjning — hela beloppet prissätts med grundsatsen
        // från regelverket även om timmarna ligger kl. 22–06.
        SetupStandardEmployee();
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            OBTimmar = [new OBInput { Kategori = OBCategory.VardagNatt, Timmar = 8, NattTimmar = 8 }]
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        Assert.Equal(8 * 152.00m, result.OBTillagg.Amount); // mock-grundsats
    }

    #endregion

    #region Resersättning (resekrav)

    [Fact]
    public async Task CalculateAsync_MedResersattning_SkattefriIngarIBruttoMenEjSkatteunderlag()
    {
        SetupStandardEmployee(manadslon: 35000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            ReseTraktamente = Money.SEK(600m),
            ReseMilersattning = Money.SEK(250m),
            ReseUtlagg = Money.SEK(150m)
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Utbetalningen (brutto) inkluderar ersättningarna, skatteunderlaget gör det inte.
        Assert.Equal(36000m, result.Brutto.Amount);
        Assert.Equal(35000m, result.SkattepliktBrutto.Amount);

        var traktamenteRad = result.Rader.First(r => r.LoneartKod == "5100");
        Assert.Equal(600m, traktamenteRad.Belopp.Amount);
        Assert.Equal(TaxCategory.Traktamente, traktamenteRad.Skattekategori);
        Assert.False(traktamenteRad.ArPensionsgrundande);
        Assert.False(traktamenteRad.ArSemestergrundande);

        Assert.Equal(250m, result.Rader.First(r => r.LoneartKod == "5200").Belopp.Amount);
        Assert.Equal(150m, result.Rader.First(r => r.LoneartKod == "5300").Belopp.Amount);

        // Arbetsgivaravgifter beräknas på det avgiftspliktiga underlaget — inte på
        // skattefria kostnadsersättningar.
        var expectedAG = Math.Round(35000m * 0.3142m, 0, MidpointRounding.ToEven);
        Assert.Equal(expectedAG, result.Arbetsgivaravgifter.Amount);
    }

    #endregion

    #region Övertid

    [Fact]
    public async Task CalculateAsync_EnkelOvertid_CorrectBerakna()
    {
        SetupStandardEmployee(manadslon: 35000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            OvertidTimmar = 10,
            KvalificeradOvertid = false
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // AB § 20 mom. 3: timlön = månadslön / 165, enkel övertid = 180 % av timlönen
        // per övertidstimme (hela beloppet — övertidstimmen ligger utanför månadslönen)
        var timlon = 35000m / 165m;
        var expected = 10 * (timlon * 1.8m);
        Assert.Equal(expected, result.Overtidstillagg.Amount);

        var overtidRad = result.Rader.First(r => r.LoneartKod == "1410");
        Assert.Equal("Enkel övertid", overtidRad.Benamning);
    }

    [Fact]
    public async Task CalculateAsync_KvalificeradOvertid_CorrectBerakna()
    {
        SetupStandardEmployee(manadslon: 35000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            OvertidTimmar = 5,
            KvalificeradOvertid = true
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // AB § 20 mom. 3: kvalificerad övertid = 240 % av timlönen (månadslön / 165)
        var timlon = 35000m / 165m;
        var expected = 5 * (timlon * 2.4m);
        Assert.Equal(expected, result.Overtidstillagg.Amount);

        var overtidRad = result.Rader.First(r => r.LoneartKod == "1420");
        Assert.Equal("Kvalificerad övertid", overtidRad.Benamning);
    }

    #endregion

    #region Jour och beredskap

    [Fact]
    public async Task CalculateAsync_MedJourTimmar_CorrectJourTillagg()
    {
        SetupStandardEmployee(manadslon: 35000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            JourTimmar = 16
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Jour = timlön * 0.40 * 16, med AB-timlön = månadslön / 165
        // Motorn beräknar: jourSats = timlon * 0.40, sedan jourTillagg = timmar * jourSats
        var timlon = 35000m / 165m;
        var jourSats = timlon * 0.40m;
        var expected = 16 * jourSats;
        Assert.Equal(expected, result.JourTillagg.Amount);

        var jourRad = result.Rader.First(r => r.LoneartKod == "1500");
        Assert.Equal("Jour", jourRad.Benamning);
        Assert.True(jourRad.ArSemestergrundande);
        Assert.True(jourRad.ArPensionsgrundande);
    }

    [Fact]
    public async Task CalculateAsync_MedBeredskapsTimmar_CorrectBeredskapsTillagg()
    {
        SetupStandardEmployee(manadslon: 35000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            BeredskapsTimmar = 24
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Beredskap = timlön * 0.20 * 24, med AB-timlön = månadslön / 165
        var timlon = 35000m / 165m;
        var expected = 24 * (timlon * 0.20m);
        Assert.Equal(expected, result.BeredskapsTillagg.Amount);

        var beredskapsRad = result.Rader.First(r => r.LoneartKod == "1510");
        Assert.Equal("Beredskap", beredskapsRad.Benamning);
    }

    #endregion

    #region Sjuklön och karensavdrag

    [Fact]
    public async Task CalculateAsync_MedSjukdagar_CorrectSjuklonOchKarens()
    {
        SetupStandardEmployee(manadslon: 35000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            SjukdagarMedLon = 5
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Karensavdrag = veckolön * 80% * 20% = (35000 * 12 / 52) * 0.80 * 0.20
        // Per Sjuklönelagen 6§: 20% av genomsnittlig veckosjuklön (som är 80% av veckolön)
        var veckolon = 35000m * 100m / 100m * 12m / 52m;
        var expectedKarens = veckolon * 0.80m * 0.20m;
        Assert.Equal(expectedKarens, result.Karensavdrag.Amount);

        // Sjuklön = dagslön * 80% * 5 dagar
        var daglon = 35000m * 100m / 100m / 21m;
        var expectedSjuklon = daglon * 0.80m * 5;
        Assert.Equal(expectedSjuklon, result.Sjuklon.Amount);

        // Karensavdrag ska vara negativt (avdrag) på raden
        var karensRad = result.Rader.First(r => r.LoneartKod == "3001");
        Assert.True(karensRad.ArAvdrag);
        Assert.True(karensRad.Belopp.Amount < 0);

        // Sjukavdrag: full daglön dras för varje sjukdag (AB § 28) — utan detta skulle
        // sjukfrånvaro HÖJA bruttot (full månadslön + sjuklön ovanpå).
        var expectedSjukavdrag = daglon * 5;
        var sjukavdragRad = result.Rader.First(r => r.LoneartKod == "3005");
        Assert.True(sjukavdragRad.ArAvdrag);
        Assert.Equal(-expectedSjukavdrag, sjukavdragRad.Belopp.Amount);

        // Brutto = grundlön − sjukavdrag + sjuklön − karensavdrag → alltid LÄGRE än frisk månad
        var expectedBrutto = 35000m - expectedSjukavdrag + expectedSjuklon - expectedKarens;
        Assert.Equal(Math.Round(expectedBrutto, 2), Math.Round(result.Brutto.Amount, 2));
        Assert.True(result.Brutto.Amount < 35000m,
            "En sjukmånad ska ge lägre brutto än full månadslön");
    }

    [Fact]
    public async Task CalculateAsync_34500Med5Sjukdagar_GerLagreBruttoAn34500()
    {
        // Handräknat exempel: 34 500 kr månadslön, 21 arbetsdagar, 5 sjukdagar.
        // Daglön = 34 500 / 21 = 1 642,857...
        // Sjukavdrag = 5 × daglön = 8 214,286
        // Sjuklön    = 5 × daglön × 80 % = 6 571,429
        // Karens     = (34 500 × 12 / 52) × 80 % × 20 % = 1 273,846
        // Brutto     = 34 500 − 8 214,286 + 6 571,429 − 1 273,846 ≈ 31 583,30 < 34 500
        SetupStandardEmployee(manadslon: 34500m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            SjukdagarMedLon = 5
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        var daglon = 34500m / 21m;
        var expectedBrutto = 34500m - daglon * 5 + daglon * 0.80m * 5
                             - 34500m * 12m / 52m * 0.80m * 0.20m;
        Assert.Equal(Math.Round(expectedBrutto, 2), Math.Round(result.Brutto.Amount, 2));
        Assert.True(result.Brutto.Amount < 34500m,
            "34 500 kr + 5 sjukdagar ska ge LÄGRE brutto än 34 500 kr");
    }

    [Fact]
    public async Task CalculateAsync_SjukdagarUtanLon_DrasUtanSjuklon()
    {
        // Dag 15+ ersätts av Försäkringskassan: fullt löneavdrag men ingen sjuklön.
        SetupStandardEmployee(manadslon: 35000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            SjukdagarMedLon = 14,
            SjukdagarUtanLon = 7
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        var daglon = 35000m / 21m;
        var sjukavdragRad = result.Rader.First(r => r.LoneartKod == "3005");
        Assert.Equal(21m, sjukavdragRad.Antal);             // avdrag för alla 21 sjukdagar
        Assert.Equal(-(daglon * 21), sjukavdragRad.Belopp.Amount);
        Assert.Equal(daglon * 0.80m * 14, result.Sjuklon.Amount); // sjuklön endast dag 2–14
        Assert.True(result.Brutto.Amount < 35000m);
    }

    #endregion

    #region Semester

    [Fact]
    public async Task CalculateAsync_MedSemester_CorrectSemesterAvdragOchTillagg()
    {
        SetupStandardEmployee(manadslon: 35000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            SemesterdagarUttagna = 5
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Semesteravdrag = 35000 * 100/100 * 0.80% * 5 = 1400
        var semesterAvdrag = 35000m * 0.0080m * 5;
        var avdragRad = result.Rader.First(r => r.LoneartKod == "2710");
        Assert.Equal(-semesterAvdrag, avdragRad.Belopp.Amount);
        Assert.True(avdragRad.ArAvdrag);

        // Semesterlön (sammalöneregeln) = samma belopp som avdraget
        var semesterlon = result.Rader.First(r => r.LoneartKod == "2700");
        Assert.Equal(semesterAvdrag, semesterlon.Belopp.Amount);

        // Semestertillägg = 35000 * 100/100 * 0.605% * 5 (AB §27 mom 15)
        var expectedTillagg = 35000m * 100m / 100m * 0.00605m * 5;
        Assert.Equal(expectedTillagg, result.Semestertillagg.Amount);
        Assert.Equal(5, result.SemesterdagarUttagna);
    }

    #endregion

    #region Arbetsgivaravgifter

    [Theory]
    // CalculateAsync anropas med månad 1 → ungdomsfönstret (apr–dec 2026) är EJ öppet.
    [InlineData("20030101-****", 2025, 0.3142)]  // Ingen ungdomsnedsättning fanns 2025 → full avgift
    [InlineData("20030101-****", 2026, 0.3142)]  // Januari 2026 → före ungdomsfönstret → full avgift
    [InlineData("19570101-****", 2025, 0.1021)]  // Äldre: född 1957 ≤ 1958 → endast ålderspensionsavgift
    [InlineData("19580101-****", 2026, 0.1021)]  // Äldre: fyllt 67 vid ingången av 2026
    [InlineData("19590101-****", 2026, 0.3142)]  // Fyller 67 under 2026 → full avgift
    [InlineData("19850515-****", 2025, 0.3142)]  // Standard (medelålders)
    [InlineData("20020101-****", 2025, 0.3142)]  // Utanför ungdomsintervallet → standard
    public async Task CalculateAsync_Arbetsgivaravgifter_CorrectSatsBasedOnAge(
        string personnummer, int year, decimal expectedSats)
    {
        SetupStandardEmployee(manadslon: 35000m, personnummerMaskerat: personnummer);

        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, year, 1, input);

        Assert.Equal(expectedSats, result.ArbetsgivaravgiftSats);
    }

    [Fact]
    public async Task CalculateAsync_Ungdom_April2026_ReduceradSatsUppTillTak()
    {
        // Född 2005 (19 vid årets ingång) + löneperiod april 2026 → ungdomsnedsättning aktiv.
        SetupStandardEmployee(manadslon: 35000m, personnummerMaskerat: "20050101-****");
        var input = new PayrollInput { ArbetadeDagar = 21, ArbetsdagarIManadens = 21 };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2026, 4, input);

        Assert.Equal(0.2081m, result.ArbetsgivaravgiftSats);
        // Belopp: 25 000 * 20,81 % + (brutto - 25 000) * 31,42 %, avrundat till kronor
        var brutto = result.Brutto.Amount;
        var forvantat = Math.Round(25000m * 0.2081m + (brutto - 25000m) * 0.3142m, 0, MidpointRounding.ToEven);
        Assert.Equal(forvantat, result.Arbetsgivaravgifter.Amount);
    }

    [Fact]
    public async Task CalculateAsync_Fodd1937_IngenArbetsgivaravgift()
    {
        SetupStandardEmployee(manadslon: 35000m, personnummerMaskerat: "19370101-****");
        var input = new PayrollInput { ArbetadeDagar = 21, ArbetsdagarIManadens = 21 };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2026, 1, input);

        Assert.Equal(0m, result.ArbetsgivaravgiftSats);
        Assert.Equal(0m, result.Arbetsgivaravgifter.Amount);
    }

    [Fact]
    public async Task CalculateAsync_UtanSkattetabell_SkattFallerTillbakaPaKommunTabell()
    {
        // Anställd saknar explicit skattetabell men har kommunalskattesats → skatten ska EJ bli 0.
        SetupStandardEmployee(manadslon: 35000m, skattetabell: null, skattekolumn: null);
        var input = new PayrollInput { ArbetadeDagar = 21, ArbetsdagarIManadens = 21 };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2026, 1, input);

        Assert.True(result.Skatt.Amount > 0m,
            "Preliminärskatt ska beräknas via kommunens tabell även utan explicit skattetabell");
    }

    #endregion

    #region AKAP-KR Pension

    [Fact]
    public async Task CalculateAsync_PensionUnder7_5IBB_6Procent()
    {
        // 35000 * 12 = 420 000 < 7.5 * 80 600 = 604 500
        SetupStandardEmployee(manadslon: 35000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Pensionsgrundande = brutto (alla pensionsgrundande rader)
        // Pension = pensionsgrundande * 6% (avrundat till ören)
        var expectedPension = Math.Round(result.Pensionsgrundande.Amount * 0.06m, 2, MidpointRounding.ToEven);
        Assert.Equal(expectedPension, result.Pensionsavgift.Amount);
    }

    [Fact]
    public async Task CalculateAsync_PensionOver7_5IBB_SplitRate()
    {
        // 80000 * 12 = 960 000 > 7.5 * 80 600 = 604 500
        SetupStandardEmployee(manadslon: 80000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Gräns per månad = 604 500 / 12 = 50 375
        var gransPerManad = 7.5m * 80600m / 12m;
        var underGrans = gransPerManad * 0.06m;
        var overGrans = (result.Pensionsgrundande.Amount - gransPerManad) * 0.315m;
        var expectedPension = Math.Round(underGrans + overGrans, 2, MidpointRounding.ToEven);

        Assert.Equal(expectedPension, result.Pensionsavgift.Amount);
        Assert.True(result.Pensionsavgift.Amount > result.Pensionsgrundande.Amount * 0.06m);
    }

    #endregion

    #region Jämkning

    [Fact]
    public async Task CalculateAsync_MedJamkning_ReduceradSkatt()
    {
        SetupStandardEmployee(harJamkning: true, jamkningBelopp: 2000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Skatt ska reduceras med jämkningsbelopp
        // Skatten från tabellen = 8000 (mock)
        // Jämkning = 2000
        // Netto skatt = 6000
        Assert.Equal(6000m, result.Skatt.Amount);
    }

    [Fact]
    public async Task CalculateAsync_JamkningStorreAnSkatt_SkattBlirNoll()
    {
        SetupStandardEmployee(harJamkning: true, jamkningBelopp: 999999m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Skatt ska inte vara negativ
        Assert.Equal(0m, result.Skatt.Amount);
    }

    #endregion

    #region Föräldralöneutfyllnad

    [Fact]
    public async Task CalculateAsync_MedForaldraledighet_CorrectUtfyllnad()
    {
        SetupStandardEmployee(manadslon: 35000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            ForaldraledigaDagar = 10
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Föräldralöneutfyllnad = dagslön * 10% * 10 dagar
        var daglon = 35000m * 100m / 100m / 21m;
        var expected = daglon * 0.10m * 10;
        Assert.Equal(expected, result.ForaldraloneUtfyllnad.Amount);

        var rad = result.Rader.First(r => r.LoneartKod == "3100");
        Assert.Equal("Föräldralöneutfyllnad", rad.Benamning);
        Assert.True(rad.ArSemestergrundande);
        Assert.True(rad.ArPensionsgrundande);

        // Löneavdrag för den föräldralediga tiden (full daglön per dag) — utan detta
        // skulle föräldraledighet HÖJA bruttot i stället för att sänka det.
        var avdragRad = result.Rader.First(r => r.LoneartKod == "3210");
        Assert.True(avdragRad.ArAvdrag);
        Assert.Equal(-(daglon * 10), avdragRad.Belopp.Amount);

        // Brutto = grundlön − avdrag + utfyllnad < full månadslön
        Assert.Equal(
            Math.Round(35000m - daglon * 10 + expected, 2),
            Math.Round(result.Brutto.Amount, 2));
        Assert.True(result.Brutto.Amount < 35000m,
            "En föräldraledig månad ska ge lägre brutto än full månadslön");
    }

    #endregion

    #region Kombinerat scenario

    [Fact]
    public async Task CalculateAsync_FulltScenario_CorrectBruttoOchNetto()
    {
        SetupStandardEmployee(manadslon: 35000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            OBTimmar = [new OBInput { Kategori = OBCategory.VardagKvall, Timmar = 10 }],
            OvertidTimmar = 5,
            KvalificeradOvertid = false,
            SemesterdagarUttagna = 3,
            Kostnadsstalle = "1234"
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Grundlön = 35000
        // OB = 10 * 126.50 = 1265
        // Övertid = 5 * timlön * 1.8 (AB § 20 mom. 3: hela beloppet, timlön = månadslön / 165)
        var timlon = 35000m / 165m;
        var overtid = 5 * (timlon * 1.8m);
        // Semestertillägg = 35000 * 100/100 * 0.00605 * 3 (AB §27 mom 15)
        var semestertillagg = 35000m * 100m / 100m * 0.00605m * 3;

        var expectedBrutto = 35000m + 1265m + overtid + semestertillagg;
        Assert.Equal(expectedBrutto, result.Brutto.Amount);

        // Netto = Brutto - Skatt - avdrag
        Assert.True(result.Netto > Money.Zero);
        Assert.True(result.Netto < result.Brutto);
        Assert.Equal(0.3142m, result.ArbetsgivaravgiftSats);
    }

    #endregion

    #region Golden file test (öre-precision)

    [Fact]
    public async Task CalculateAsync_GoldenFileTest_KnownInputKnownOutput()
    {
        // Känd indata: 35 000 kr månadslön, heltid, 21 arbetsdagar,
        // 10h OB vardagkväll, 3 semesterdagar, skattetabell 33/1
        SetupStandardEmployee(manadslon: 35000m);
        var input = new PayrollInput
        {
            ArbetadeDagar = 21,
            ArbetsdagarIManadens = 21,
            OBTimmar = [new OBInput { Kategori = OBCategory.VardagKvall, Timmar = 10 }],
            SemesterdagarUttagna = 3,
            Kostnadsstalle = "1234"
        };

        var result = await _engine.CalculateAsync(
            _runId, _employeeId, _employmentId, 2025, 1, input);

        // Grundlön: exakt 35000.00
        Assert.Equal(35000.00m, result.Rader.First(r => r.LoneartKod == "1100").Belopp.Amount);

        // OB: 10 * 126.50 = 1265.00
        Assert.Equal(1265.00m, result.OBTillagg.Amount);

        // Semestertillägg: 35000 * 1.0 * 0.00605 * 3 = 635.25 (AB §27 mom 15: 0.605%)
        Assert.Equal(635.25m, result.Semestertillagg.Amount);

        // Brutto: 35000 + 1265 + 635.25 = 36900.25
        Assert.Equal(36900.25m, result.Brutto.Amount);

        // Skatt: 8000 (mock tabell)
        Assert.Equal(8000m, result.Skatt.Amount);

        // Netto: 36900.25 - 8000 = 28900.25
        Assert.Equal(28900.25m, result.Netto.Amount);

        // Arbetsgivaravgifter: 36900.25 * 0.3142, avrundad till kronor
        var expectedAG = Math.Round(36900.25m * 0.3142m, 0, MidpointRounding.ToEven);
        Assert.Equal(expectedAG, result.Arbetsgivaravgifter.Amount);

        // Pension: alla pensionsgrundande rader * 6%
        Assert.True(result.Pensionsavgift.Amount > 0);
    }

    #endregion

    #region Mock implementations

    private sealed class MockTaxTableProvider : ITaxTableProvider
    {
        public Task<TaxTable?> GetTableAsync(int year, int tableNumber, int column, CancellationToken ct = default)
        {
            var table = new TaxTable
            {
                Id = 1,
                Ar = year,
                Tabellnummer = tableNumber,
                Kolumn = column
            };

            // Generera realistisk skattetabell
            table.LaggTillRad(new TaxTableRow { InkomstFran = 0m, InkomstTill = 19999m, Skattebelopp = 0m });
            table.LaggTillRad(new TaxTableRow { InkomstFran = 20000m, InkomstTill = 24999m, Skattebelopp = 3000m });
            table.LaggTillRad(new TaxTableRow { InkomstFran = 25000m, InkomstTill = 29999m, Skattebelopp = 5000m });
            table.LaggTillRad(new TaxTableRow { InkomstFran = 30000m, InkomstTill = 34999m, Skattebelopp = 7000m });
            table.LaggTillRad(new TaxTableRow { InkomstFran = 35000m, InkomstTill = 39999m, Skattebelopp = 8000m });
            table.LaggTillRad(new TaxTableRow { InkomstFran = 40000m, InkomstTill = 49999m, Skattebelopp = 10000m });
            table.LaggTillRad(new TaxTableRow { InkomstFran = 50000m, InkomstTill = 59999m, Skattebelopp = 14000m });
            table.LaggTillRad(new TaxTableRow { InkomstFran = 60000m, InkomstTill = 79999m, Skattebelopp = 18000m });
            table.LaggTillRad(new TaxTableRow { InkomstFran = 80000m, InkomstTill = 99999m, Skattebelopp = 25000m });
            table.LaggTillRad(new TaxTableRow { InkomstFran = 100000m, InkomstTill = 999999m, Skattebelopp = 35000m });

            return Task.FromResult<TaxTable?>(table);
        }

        public Task<IReadOnlyList<TaxTable>> GetAllTablesForYearAsync(int year, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<TaxTable>>(new List<TaxTable>());
        }
    }

    private sealed class MockCollectiveAgreementRulesEngine : ICollectiveAgreementRulesEngine
    {
        public Task<decimal> GetOBRateAsync(CollectiveAgreementType agreement, OBCategory category, DateOnly date, CancellationToken ct = default)
        {
            if (category == OBCategory.Ingen)
                return Task.FromResult(0m);

            var rate = (agreement, category) switch
            {
                (CollectiveAgreementType.AB, OBCategory.VardagKvall) => 126.50m,
                (CollectiveAgreementType.AB, OBCategory.VardagNatt) => 152.00m,
                (CollectiveAgreementType.AB, OBCategory.Helg) => 89.00m,
                (CollectiveAgreementType.AB, OBCategory.Storhelg) => 195.00m,
                _ => 0m
            };
            return Task.FromResult(rate);
        }

        public Task<OvertimeRules> GetOvertimeRulesAsync(CollectiveAgreementType agreement, DateOnly date, CancellationToken ct = default)
        {
            return Task.FromResult(new OvertimeRules
            {
                EnkelOvertidFaktor = 0.8m,
                KvalificeradOvertidFaktor = 1.4m,
                MaxOvertidPerVecka = 48m,
                MaxOvertidPerManad = 50m,
                MaxOvertidPerAr = 200m,
                KomptidFaktor = 1.5m
            });
        }

        public Task<VacationRules> GetVacationRulesAsync(CollectiveAgreementType agreement, DateOnly date, CancellationToken ct = default)
        {
            return GetVacationRulesAsync(agreement, date, fodelseAr: null, ct);
        }

        public Task<VacationRules> GetVacationRulesAsync(CollectiveAgreementType agreement, DateOnly date, int? fodelseAr, CancellationToken ct = default)
        {
            var dagarPerAr = 25;
            if (fodelseAr.HasValue)
            {
                var alder = date.Year - fodelseAr.Value;
                dagarPerAr = alder switch
                {
                    >= 50 => 32,
                    >= 40 => 31,
                    _ => 25
                };
            }

            return Task.FromResult(new VacationRules
            {
                DagarPerAr = dagarPerAr,
                SammaloneregelProcent = 0.80m,
                SemestertillaggProcent = 0.43m,
                VariabelLonSemesterProcent = 12.0m,
                MaxSparadeDagar = 5,
                TotalMaxSparade = 40,
                IntjanandeArStart = new DateOnly(date.Year - 1, 4, 1),
                IntjanandeArSlut = new DateOnly(date.Year, 3, 31)
            });
        }

        public Task<SickPayRules> GetSickPayRulesAsync(CollectiveAgreementType agreement, DateOnly date, CancellationToken ct = default)
        {
            return Task.FromResult(new SickPayRules
            {
                KarensavdragProcent = 20m,
                SjuklonDag2Till14Procent = 80m,
                FKAnmalanEfterDag = 14,
                MaxSjuklonedagar = 14,
                LakarintygEfterDag = 7
            });
        }

        public Task<JourRegler> GetJourReglerAsync(CollectiveAgreementType agreement, DateOnly date, CancellationToken ct = default)
        {
            return Task.FromResult(new JourRegler
            {
                PassivTimlonFaktor = 0.40m,
                AktivTimlonFaktor = 1.5m
            });
        }

        public Task<BeredskapsRegler> GetBeredskapsReglerAsync(CollectiveAgreementType agreement, DateOnly date, CancellationToken ct = default)
        {
            return Task.FromResult(new BeredskapsRegler
            {
                PassivTimlonFaktor = 0.20m
            });
        }

        public Task<ForaldraloneRegler> GetForaldraloneReglerAsync(CollectiveAgreementType agreement, DateOnly date, CancellationToken ct = default)
        {
            return Task.FromResult(new ForaldraloneRegler
            {
                DagarMedUtfyllnad = 180,
                UtfyllnadProcent = 0.10m
            });
        }

        public decimal GetOBRate(CollectiveAgreement avtal, OBCategory category, DateOnly date)
        {
            if (category == OBCategory.Ingen) return 0m;
            return avtal.HamtaOBSats(category, date);
        }

        public OvertimeRules GetOvertimeRules(CollectiveAgreement avtal)
        {
            return new OvertimeRules
            {
                EnkelOvertidFaktor = 0.8m,
                KvalificeradOvertidFaktor = 1.4m,
                MaxOvertidPerVecka = 48m,
                MaxOvertidPerManad = 50m,
                MaxOvertidPerAr = 200m,
                KomptidFaktor = 1.5m
            };
        }

        public VacationRules GetVacationRules(CollectiveAgreement avtal, DateOnly date, int? fodelseAr = null)
        {
            return new VacationRules
            {
                DagarPerAr = 25,
                SammaloneregelProcent = 0.80m,
                SemestertillaggProcent = 0.43m,
                VariabelLonSemesterProcent = 12.0m,
                MaxSparadeDagar = 5,
                TotalMaxSparade = 40,
                IntjanandeArStart = new DateOnly(date.Year - 1, 4, 1),
                IntjanandeArSlut = new DateOnly(date.Year, 3, 31)
            };
        }
    }

    private sealed class MockCoreHRModule : ICoreHRModule
    {
        public EmployeeDto? Employee { get; set; }
        public EmploymentDto? Employment { get; set; }

        public Task<EmployeeDto?> GetEmployeeAsync(EmployeeId id, CancellationToken ct = default)
            => Task.FromResult(Employee);

        public Task<EmploymentDto?> GetActiveEmploymentAsync(EmployeeId id, DateOnly date, CancellationToken ct = default)
            => Task.FromResult(Employment);

        public Task<IReadOnlyList<EmploymentDto>> GetActiveEmploymentsAsync(EmployeeId id, DateOnly date, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmploymentDto>>(Employment is not null ? [Employment] : []);

        public Task<IReadOnlyList<EmployeeDto>> GetEmployeesByUnitAsync(OrganizationId unitId, DateOnly date, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeeDto>>(Employee is not null ? [Employee] : []);

        public Task<OrganizationUnitDto?> GetOrganizationUnitAsync(OrganizationId id, CancellationToken ct = default)
            => Task.FromResult<OrganizationUnitDto?>(null);
    }

    #endregion
}
