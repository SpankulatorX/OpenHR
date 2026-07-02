using RegionHR.Core.Contracts;
using RegionHR.SharedKernel.Domain;
using RegionHR.Payroll.Domain;

namespace RegionHR.Payroll.Engine;

/// <summary>
/// Löneberäkningsmotor: brutto-till-netto-pipeline.
/// Hanterar svensk skattelagstiftning och kollektivavtal AB/HOK.
///
/// Pipeline:
/// 1. Grundlön (månadslön * sysselsättningsgrad)
/// 2. Tillägg (OB, övertid, jour)
/// 3. Frånvaroavdrag (sjuklön, semester)
/// 4. Bruttolön
/// 5. Skatteavdrag (skattetabell)
/// 6. Nettolöneavdrag (utmätning, fackavgift)
/// 7. Nettolön
/// 8. Arbetsgivaravgifter
/// 9. Pension (AKAP-KR)
/// 10. Semesterlöneskuld
/// </summary>
public sealed class PayrollCalculationEngine
{
    private readonly ITaxTableProvider _taxTableProvider;
    private readonly ICollectiveAgreementRulesEngine _rulesEngine;
    private readonly ICoreHRModule _coreHR;

    // Inkomstbasbelopp och prisbasbelopp per år
    private const decimal IBB_2025 = 80600m;
    private const decimal IBB_2026 = 83400m;
    private const decimal PBB_2025 = 58800m;
    private const decimal PBB_2026 = 59200m;

    // Arbetsgivaravgifter: satser och åldersregler ligger i domänen
    // RegionHR.Payroll.Domain.Arbetsgivaravgift (årsversionerat, verifierat mot Skatteverket 2026).

    // Semester per AB (sammalöneregeln)
    private const decimal SEMESTER_PROCENT_PER_DAG = 0.0080m;   // 0.80% per dag
    private const decimal SEMESTER_TILLAGG_PROCENT = 0.00605m;   // 0,605% per semesterdag av månadslön (AB §27 mom 15)
    private const int SEMESTER_DAGAR_PER_AR = 25;               // Enligt AB

    // Sjuklön
    private const decimal SJUKLON_PROCENT = 0.80m;              // 80% dag 2-14
    private const decimal KARENSAVDRAG_PROCENT = 0.20m;         // 20% av genomsnittlig veckoersättning

    // AKAP-KR Pension
    private const decimal PENSION_UNDER_GRANS = 0.06m;          // 6% under 7.5 IBB
    private const decimal PENSION_OVER_GRANS = 0.315m;          // 31.5% över 7.5 IBB
    private const decimal PENSION_GRANS_IBB = 7.5m;

    public PayrollCalculationEngine(
        ITaxTableProvider taxTableProvider,
        ICollectiveAgreementRulesEngine rulesEngine,
        ICoreHRModule coreHR)
    {
        _taxTableProvider = taxTableProvider;
        _rulesEngine = rulesEngine;
        _coreHR = coreHR;
    }

    /// <summary>
    /// Beräkna lön för en anställd för given period.
    /// </summary>
    public async Task<PayrollResult> CalculateAsync(
        PayrollRunId runId,
        EmployeeId employeeId,
        EmploymentId employmentId,
        int year,
        int month,
        PayrollInput input,
        CancellationToken ct = default)
    {
        var employee = await _coreHR.GetEmployeeAsync(employeeId, ct)
            ?? throw new InvalidOperationException($"Anställd {employeeId} hittades inte");

        var employment = await _coreHR.GetActiveEmploymentAsync(employeeId, new DateOnly(year, month, 1), ct)
            ?? throw new InvalidOperationException($"Ingen aktiv anställning för {employeeId}");

        var result = PayrollResult.Skapa(
            runId, employeeId, employmentId, year, month,
            Money.SEK(employment.Manadslon), employment.Sysselsattningsgrad,
            employment.Kollektivavtal);

        // Steg 1: Grundlön
        var grundlon = BeraknaGrundlon(employment.Manadslon, employment.Sysselsattningsgrad, input.ArbetadeDagar, input.ArbetsdagarIManadens);
        result.LaggTillRad(new PayrollResultLine
        {
            LoneartKod = "1100", Benamning = "Månadslön",
            Antal = 1, Sats = Money.SEK(employment.Manadslon),
            Belopp = grundlon, Skattekategori = TaxCategory.Skattepliktig,
            ArSemestergrundande = true, ArPensionsgrundande = true,
            Kostnadsstalle = input.Kostnadsstalle
        });

        // Steg 2: OB-tillägg
        var obTillagg = Money.Zero;
        if (input.OBTimmar.Count > 0)
        {
            obTillagg = await BeraknaOBTillaggAsync(employment.Kollektivavtal, input.OBTimmar, ct);
            result.OBTillagg = obTillagg;
            foreach (var ob in input.OBTimmar)
            {
                var sats = await _rulesEngine.GetOBRateAsync(employment.Kollektivavtal, ob.Kategori, new DateOnly(year, month, 1), ct);
                result.LaggTillRad(new PayrollResultLine
                {
                    LoneartKod = "1310", Benamning = $"OB-tillägg {ob.Kategori}",
                    Antal = ob.Timmar, Sats = Money.SEK(sats),
                    Belopp = Money.SEK(ob.Timmar * sats),
                    Skattekategori = TaxCategory.Skattepliktig,
                    ArSemestergrundande = true, ArPensionsgrundande = true
                });
            }
        }

        // Steg 2b: Jour
        var jourTillagg = Money.Zero;
        if (input.JourTimmar > 0)
        {
            var timlon = employment.Manadslon / (38.25m * 52m / 12m);
            var jourRegler = await _rulesEngine.GetJourReglerAsync(employment.Kollektivavtal, new DateOnly(year, month, 1), ct);
            var jourSats = timlon * jourRegler.PassivTimlonFaktor;
            jourTillagg = Money.SEK(input.JourTimmar * jourSats);
            result.JourTillagg = jourTillagg;
            result.LaggTillRad(new PayrollResultLine
            {
                LoneartKod = "1500", Benamning = "Jour",
                Antal = input.JourTimmar, Sats = Money.SEK(jourSats),
                Belopp = jourTillagg, Skattekategori = TaxCategory.Skattepliktig,
                ArSemestergrundande = true, ArPensionsgrundande = true,
                Kostnadsstalle = input.Kostnadsstalle
            });
        }

        // Steg 2c: Beredskap
        var beredskapsTillagg = Money.Zero;
        if (input.BeredskapsTimmar > 0)
        {
            var timlon = employment.Manadslon / (38.25m * 52m / 12m);
            var beredskapsRegler = await _rulesEngine.GetBeredskapsReglerAsync(employment.Kollektivavtal, new DateOnly(year, month, 1), ct);
            var beredskapsSats = timlon * beredskapsRegler.PassivTimlonFaktor;
            beredskapsTillagg = Money.SEK(input.BeredskapsTimmar * beredskapsSats);
            result.BeredskapsTillagg = beredskapsTillagg;
            result.LaggTillRad(new PayrollResultLine
            {
                LoneartKod = "1510", Benamning = "Beredskap",
                Antal = input.BeredskapsTimmar, Sats = Money.SEK(beredskapsSats),
                Belopp = beredskapsTillagg, Skattekategori = TaxCategory.Skattepliktig,
                ArSemestergrundande = true, ArPensionsgrundande = true,
                Kostnadsstalle = input.Kostnadsstalle
            });
        }

        // Steg 3: Övertid
        var overtid = Money.Zero;
        if (input.OvertidTimmar > 0)
        {
            var timlon = employment.Manadslon / (38.25m * 52m / 12m);
            // AB 25: Enkel övertid = 180% total (tillägg 80%), Kvalificerad = 240% total (tillägg 140%)
            // Grundlönen täcker bas 100%, så tillägget är 0.8x resp 1.4x timlön
            var overtidsSats = input.KvalificeradOvertid ? timlon * 1.4m : timlon * 0.8m;
            overtid = Money.SEK(input.OvertidTimmar * overtidsSats);
            result.Overtidstillagg = overtid;
            result.LaggTillRad(new PayrollResultLine
            {
                LoneartKod = input.KvalificeradOvertid ? "1420" : "1410",
                Benamning = input.KvalificeradOvertid ? "Kvalificerad övertid" : "Enkel övertid",
                Antal = input.OvertidTimmar, Sats = Money.SEK(overtidsSats),
                Belopp = overtid, Skattekategori = TaxCategory.Skattepliktig,
                ArSemestergrundande = false, ArPensionsgrundande = true
            });
        }

        // Steg 4: Sjuklön / Karensavdrag
        if (input.SjukdagarMedLon > 0)
        {
            var veckolon = Money.SEK(employment.Manadslon * employment.Sysselsattningsgrad / 100m * 12m / 52m);
            // Karensavdrag = 20% av genomsnittlig veckosjuklön (som är 80% av veckolön)
            // Per Sjuklönelagen 6§: karensavdrag = veckolön * 80% * 20% = 16% av veckolön
            var karensavdrag = veckolon * SJUKLON_PROCENT * KARENSAVDRAG_PROCENT;
            result.Karensavdrag = karensavdrag;
            result.LaggTillRad(new PayrollResultLine
            {
                LoneartKod = "3001", Benamning = "Karensavdrag",
                Antal = 1, Sats = karensavdrag, Belopp = Money.Zero - karensavdrag,
                Skattekategori = TaxCategory.Skattepliktig, ArAvdrag = true
            });

            var daglon = Money.SEK(employment.Manadslon * employment.Sysselsattningsgrad / 100m / 21m);
            var sjuklon = daglon * SJUKLON_PROCENT * input.SjukdagarMedLon;
            result.Sjuklon = sjuklon;
            result.LaggTillRad(new PayrollResultLine
            {
                LoneartKod = "3010", Benamning = "Sjuklön dag 2-14",
                Antal = input.SjukdagarMedLon, Sats = daglon * SJUKLON_PROCENT,
                Belopp = sjuklon, Skattekategori = TaxCategory.Skattepliktig,
                ArSemestergrundande = true, ArPensionsgrundande = true
            });
        }

        // Steg 4b: Föräldralöneutfyllnad per AB
        var foraldraloneUtfyllnad = Money.Zero;
        if (input.ForaldraledigaDagar > 0)
        {
            var foraldraRegler = await _rulesEngine.GetForaldraloneReglerAsync(employment.Kollektivavtal, new DateOnly(year, month, 1), ct);
            // Begränsa till maximalt antal dagar med utfyllnad
            var dagarMedUtfyllnad = Math.Min(input.ForaldraledigaDagar, foraldraRegler.DagarMedUtfyllnad);
            var daglon = Money.SEK(employment.Manadslon * employment.Sysselsattningsgrad / 100m / 21m);
            foraldraloneUtfyllnad = daglon * foraldraRegler.UtfyllnadProcent * dagarMedUtfyllnad;
            result.ForaldraloneUtfyllnad = foraldraloneUtfyllnad;
            result.LaggTillRad(new PayrollResultLine
            {
                LoneartKod = "3100", Benamning = "Föräldralöneutfyllnad",
                Antal = dagarMedUtfyllnad, Sats = (daglon * foraldraRegler.UtfyllnadProcent).RoundToOren(),
                Belopp = foraldraloneUtfyllnad, Skattekategori = TaxCategory.Skattepliktig,
                ArSemestergrundande = true, ArPensionsgrundande = true,
                Kostnadsstalle = input.Kostnadsstalle
            });
        }

        // Steg 5: Semesteravdrag (om semester tagen)
        if (input.SemesterdagarUttagna > 0)
        {
            var semesterAvdrag = Money.SEK(employment.Manadslon * employment.Sysselsattningsgrad / 100m * SEMESTER_PROCENT_PER_DAG * input.SemesterdagarUttagna);
            result.LaggTillRad(new PayrollResultLine
            {
                LoneartKod = "2710", Benamning = "Semesterlöneavdrag",
                Antal = input.SemesterdagarUttagna, Sats = Money.SEK(employment.Manadslon * SEMESTER_PROCENT_PER_DAG),
                Belopp = Money.Zero - semesterAvdrag, Skattekategori = TaxCategory.Skattepliktig, ArAvdrag = true
            });
            // Semesterlön (utbetalas som sammalön under AB)
            var semesterlon = semesterAvdrag;
            var semestertillagg = Money.SEK(employment.Manadslon * employment.Sysselsattningsgrad / 100m * SEMESTER_TILLAGG_PROCENT * input.SemesterdagarUttagna);
            result.Semesterlon = semesterlon;
            result.Semestertillagg = semestertillagg;
            result.SemesterdagarUttagna = input.SemesterdagarUttagna;
            result.LaggTillRad(new PayrollResultLine
            {
                LoneartKod = "2700", Benamning = "Semesterlön",
                Antal = input.SemesterdagarUttagna, Sats = Money.SEK(employment.Manadslon * SEMESTER_PROCENT_PER_DAG),
                Belopp = semesterlon, Skattekategori = TaxCategory.Skattepliktig,
                ArPensionsgrundande = true
            });
            result.LaggTillRad(new PayrollResultLine
            {
                LoneartKod = "2720", Benamning = "Semestertillägg",
                Antal = input.SemesterdagarUttagna,
                Sats = Money.SEK(employment.Manadslon * SEMESTER_TILLAGG_PROCENT),
                Belopp = semestertillagg, Skattekategori = TaxCategory.Skattepliktig,
                ArPensionsgrundande = true
            });
        }

        // Steg 6: Beräkna brutto
        result.Brutto = grundlon + obTillagg + jourTillagg + beredskapsTillagg + overtid
                        + result.Sjuklon + result.Semestertillagg + foraldraloneUtfyllnad - result.Karensavdrag;
        result.SkattepliktBrutto = result.Brutto; // Förenklad, alla delar skattepliktiga i detta steg

        // Steg 7: Skatteberäkning enligt Skatteverkets skattetabell.
        // Systemet är experten: saknas skatteuppgifter på den anställde faller vi tillbaka
        // på kommunens tabell (härledd ur kommunalskattesatsen/kommunnamnet) och kolumn 1,
        // så att preliminärskatten aldrig blir 0 i drift.
        var tabellnummer = employee.Skattetabell ?? HarledTabellnummer(employee, year);
        var skattekolumn = employee.Skattekolumn ?? 1;

        var taxTable = await _taxTableProvider.GetTableAsync(year, tabellnummer, skattekolumn, ct);
        if (taxTable is not null)
        {
            result.Skatt = taxTable.BeraknaManadenSkatt(result.SkattepliktBrutto);
        }

        // Jämkningsjustering
        if (employee.HarJamkning && employee.JamkningBelopp.HasValue)
        {
            result.Skatt = result.Skatt - Money.SEK(employee.JamkningBelopp.Value);
            if (result.Skatt < Money.Zero)
                result.Skatt = Money.Zero;
        }

        // Steg 8: Nettolöneavdrag
        result.Loneutmatning = input.Loneutmatning;
        result.Fackavgift = input.Fackavgift;

        // Steg 9: Netto
        result.Netto = result.Brutto - result.Skatt - result.Loneutmatning - result.Fackavgift - result.OvrigaAvdrag;

        // Steg 10: Arbetsgivaravgifter (satsen beror på födelseår + inkomstår/månad)
        result.ArbetsgivaravgiftSats = BeraknaArbetsgivaravgiftSats(employee, year, month);
        result.Arbetsgivaravgifter = BeraknaArbetsgivaravgiftBelopp(result.Brutto, employee, year, month).RoundToKronor();

        // Steg 11: Pension AKAP-KR
        result.Pensionsgrundande = Money.SEK(result.Rader
            .Where(r => r.ArPensionsgrundande && !r.LoneartKod.StartsWith("3001"))
            .Sum(r => r.Belopp.Amount));
        result.Pensionsavgift = BeraknaPension(result.Pensionsgrundande, year);

        // Steg 12: Semesterintjänande
        result.SemesterdagarIntjanade = BeraknaSemesterIntjanande(input.ArbetadeDagar, input.ArbetsdagarIManadens);

        return result;
    }

    private static Money BeraknaGrundlon(decimal manadslon, decimal sysselsattningsgrad, int arbetadeDagar, int arbetsdagarIManaden)
    {
        var fullLon = manadslon * sysselsattningsgrad / 100m;
        if (arbetadeDagar >= arbetsdagarIManaden)
            return Money.SEK(fullLon);

        // Proportionera vid del av månad
        return Money.SEK(fullLon * arbetadeDagar / arbetsdagarIManaden);
    }

    private async Task<Money> BeraknaOBTillaggAsync(
        CollectiveAgreementType avtal, List<OBInput> obTimmar, CancellationToken ct)
    {
        var total = 0m;
        foreach (var ob in obTimmar)
        {
            var rate = await _rulesEngine.GetOBRateAsync(avtal, ob.Kategori, DateOnly.FromDateTime(DateTime.Today), ct);
            total += ob.Timmar * rate;
        }
        return Money.SEK(total);
    }

    /// <summary>
    /// Beräkna arbetsgivaravgiftssats för en anställd. Satsen härleds ur födelseåret
    /// (maskerat personnummer) och gällande inkomstår/månad via domänen
    /// <see cref="Arbetsgivaravgift"/>. Saknas personnummer används full avgift.
    /// </summary>
    internal static decimal BeraknaArbetsgivaravgiftSats(EmployeeDto employee, int year, int month)
    {
        var fodelseAr = ExtractFodelseAr(employee.PersonnummerMaskerat);
        return fodelseAr is null
            ? Arbetsgivaravgift.FullSats(year)
            : Arbetsgivaravgift.Sats(fodelseAr.Value, year, month);
    }

    /// <summary>
    /// Beräkna faktiskt arbetsgivaravgiftsbelopp. Hanterar ungdomsnedsättningens
    /// lönetak (reducerad sats upp till 25 000 kr, full sats därutöver) via domänen.
    /// </summary>
    internal static Money BeraknaArbetsgivaravgiftBelopp(Money brutto, EmployeeDto employee, int year, int month)
    {
        var fodelseAr = ExtractFodelseAr(employee.PersonnummerMaskerat);
        return fodelseAr is null
            ? brutto * Arbetsgivaravgift.FullSats(year)
            : Arbetsgivaravgift.Belopp(brutto, fodelseAr.Value, year, month);
    }

    /// <summary>
    /// Härled Skatteverkets tabellnummer när den anställde saknar explicit skattetabell.
    /// Använder i första hand kommunalskattesatsen (avrundad till hel procent), annars
    /// kommunens sats ur <see cref="KommunSkattesatser"/> (default Örebro → tabell 34).
    /// </summary>
    private static int HarledTabellnummer(EmployeeDto employee, int year)
    {
        if (employee.KommunalSkattesats is > 0m)
            return (int)Math.Round(employee.KommunalSkattesats.Value, MidpointRounding.AwayFromZero);

        return KommunSkattesatser.Tabellnummer(employee.Kommun, year);
    }

    /// <summary>
    /// Extrahera födelseår från maskerat personnummer (YYYYMMDD-****).
    /// </summary>
    private static int? ExtractFodelseAr(string? personnummerMaskerat)
    {
        if (string.IsNullOrWhiteSpace(personnummerMaskerat))
            return null;

        // Format: YYYYMMDD-****
        if (personnummerMaskerat.Length >= 4 && int.TryParse(personnummerMaskerat[..4], out var year))
            return year;

        return null;
    }

    private static Money BeraknaPension(Money pensionsgrundande, int year)
    {
        var ibb = GetIBB(year);
        var grans = PENSION_GRANS_IBB * ibb;
        var arslon = pensionsgrundande.Amount * 12m;

        if (arslon <= grans)
        {
            return (pensionsgrundande * PENSION_UNDER_GRANS).RoundToOren();
        }

        var underGrans = Money.SEK(grans / 12m) * PENSION_UNDER_GRANS;
        var overGrans = Money.SEK((pensionsgrundande.Amount - grans / 12m)) * PENSION_OVER_GRANS;
        return (underGrans + overGrans).RoundToOren();
    }

    /// <summary>
    /// Hämta inkomstbasbelopp för angivet år.
    /// </summary>
    internal static decimal GetIBB(int year) => year switch
    {
        <= 2025 => IBB_2025,
        2026 => IBB_2026,
        _ => IBB_2026 // Framtida år: använd senast kända, bör uppdateras årligen
    };

    /// <summary>
    /// Hämta prisbasbelopp för angivet år.
    /// </summary>
    internal static decimal GetPBB(int year) => year switch
    {
        <= 2025 => PBB_2025,
        2026 => PBB_2026,
        _ => PBB_2026
    };

    private static int BeraknaSemesterIntjanande(int arbetadeDagar, int arbetsdagarIManaden)
    {
        // Förenklad: proportionellt mot 25 dagar per år
        return (int)Math.Round(SEMESTER_DAGAR_PER_AR / 12.0 * arbetadeDagar / arbetsdagarIManaden);
    }
}

/// <summary>
/// Input till löneberäkningen per anställd och period.
/// </summary>
public sealed class PayrollInput
{
    public int ArbetadeDagar { get; set; }
    public int ArbetsdagarIManadens { get; set; } = 21;
    public List<OBInput> OBTimmar { get; set; } = [];
    public decimal OvertidTimmar { get; set; }
    public bool KvalificeradOvertid { get; set; }
    public int SjukdagarMedLon { get; set; }
    public int SemesterdagarUttagna { get; set; }
    public Money Loneutmatning { get; set; } = Money.Zero;
    public Money Fackavgift { get; set; } = Money.Zero;
    public string? Kostnadsstalle { get; set; }
    public decimal JourTimmar { get; set; }
    public decimal BeredskapsTimmar { get; set; }
    public int ForaldraledigaDagar { get; set; }
}

public sealed class OBInput
{
    public OBCategory Kategori { get; set; }
    public decimal Timmar { get; set; }
}
