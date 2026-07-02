using RegionHR.Agreements.Domain;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Payroll;

/// <summary>
/// Förenklad hjälpmotor för AB-beräkningar (OB, vila, övertid, semester).
///
/// @deprecated Detta är en LEGACY-hjälpare. Den auktoritativa implementationen är:
///   - <see cref="RegionHR.Payroll.Domain.CollectiveAgreementRulesEngine"/> (OB, övertid, semester per avtal),
///   - <see cref="RegionHR.Payroll.Domain.SvenskaHelgdagar"/> (helgdagar och storhelg),
///   - <see cref="RegionHR.Agreements.Domain.ABOTillaggSatser"/> / <see cref="RegionHR.Agreements.Domain.ABSemesterRegler"/>
///     / <see cref="RegionHR.Agreements.Domain.ABOvertidSatser"/> (kanoniska AB-satser, årsversionerade).
///
/// Behålls eftersom typen är DI-registrerad (kan brytas om den tas bort). Den delegerar
/// numera till de kanoniska AB-tabellerna så att den ger korrekta, årsversionerade värden
/// i stället för de tidigare felaktiga magiska konstanterna.
/// </summary>
public class KollektivavtalEngine
{
    // AB (Allmänna bestämmelser) för kommun/region.

    /// <summary>
    /// O-tillägg för ett arbetspass enligt AB § 21. Kategori och sats bestäms av passets
    /// STARTtidpunkt via den kanoniska helgdags- och O-tilläggslogiken (påsk, midsommar, jul
    /// och nyår ingår i storhelg — den tidigare buggen där påsk saknades är åtgärdad).
    /// </summary>
    public OBTillagg BeraknaOB(DateTime start, DateTime slut)
    {
        var timmar = (decimal)(slut - start).TotalHours;
        var datum = DateOnly.FromDateTime(start);
        var tid = TimeOnly.FromDateTime(start);

        // Kanonisk kategoribestämning (storhelg > helg > natt > kväll > ingen).
        var kategori = SvenskaHelgdagar.BeraknaOBKategori(datum, tid);
        // Kanonisk, årsversionerad sats (med natthöjning för storhelg/helg kl. 22–06).
        var sats = ABOTillaggSatser.SatsForTimme(kategori, datum, tid);

        var ob = timmar * sats;
        var typ = kategori switch
        {
            OBCategory.Storhelg => "Storhelg",
            OBCategory.Helg => "Helg",
            OBCategory.VardagNatt => "Natt",
            OBCategory.VardagKvall => "Kvall",
            _ => "Dag (inget OB)"
        };

        return new OBTillagg(Math.Round(ob, 0), typ, timmar);
    }

    /// <summary>Kontroll av dygnsvila enligt AB/ATL. 11h huvudregel (vård kan gå ner till 9h).</summary>
    public ViloRegler KontrolleraVila(DateTime slutForraPass, DateTime startNastaPass)
    {
        var vilotid = (startNastaPass - slutForraPass).TotalHours;
        var minVila = 11.0; // AB kräver 11h, sjukvård kan gå ner till 9h
        var ok = vilotid >= minVila;
        return new ViloRegler(ok, vilotid, minVila);
    }

    /// <summary>
    /// Total övertidskompensation som faktor × timmar enligt AB § 20 mom. 3.
    /// Enkel övertid 180%, kvalificerad (natt/helg) 240%.
    /// </summary>
    public decimal BeraknaOvertid(decimal timmar, bool kvalificerad)
    {
        return kvalificerad
            ? timmar * ABOvertidSatser.KvalificeradOvertidProcent
            : timmar * ABOvertidSatser.EnkelOvertidProcent;
    }

    /// <summary>
    /// Semesterrätt enligt AB § 27. Årsrätten trappas med ålder (25/31/32).
    /// Antalet BETALDA dagar proportioneras mot hur många månader arbetstagaren varit
    /// anställd under intjänandeåret (§ 27 mom. 6 / SemL § 7) — tidigare ignorerades detta.
    /// Ingående sparade dagar tas som parameter i stället för att hårdkodas till 0.
    /// </summary>
    /// <param name="alder">Ålder som uppnås under intjänandeåret.</param>
    /// <param name="anstallningsmanader">Antal månader anställd under intjänandeåret (0–12).</param>
    /// <param name="ingaendeSparadeDagar">Sparade dagar från tidigare år (default 0).</param>
    public SemesterRatt BeraknaSemester(int alder, int anstallningsmanader, int ingaendeSparadeDagar = 0)
    {
        var arsratt = ABSemesterRegler.ArligSemesterratt(alder);
        var betalda = ABSemesterRegler.BetaldaDagar(arsratt, anstallningsmanader);
        return new SemesterRatt(arsratt, betalda, ingaendeSparadeDagar, ABSemesterRegler.MaxSparadeAr);
    }
}

public record OBTillagg(decimal Belopp, string Typ, decimal Timmar);
public record ViloRegler(bool Godkand, double VilotidTimmar, double MinVilotid);

/// <summary>
/// Semesterrätt: årlig rätt (25/31/32), antal betalda dagar (proportionerat mot anställningstid),
/// ingående sparade dagar samt max antal år man får spara.
/// </summary>
public record SemesterRatt(int ArligaDagar, int BetaldaDagar, int SparadeDagar, int MaxSparAr);
