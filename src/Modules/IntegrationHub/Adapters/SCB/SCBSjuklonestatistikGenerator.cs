using System.Globalization;
using System.Text;

namespace RegionHR.IntegrationHub.Adapters.SCB;

/// <summary>
/// Genererar SCB:s sjuklönestatistik (integration #20, delmängd) på aggregerad
/// nivå — motsvarar SCB:s undersökning "Konjunkturstatistik över sjuklöner" (KSju),
/// som mäter arbetsgivarens kostnader och frånvaro under sjuklöneperioden
/// (sjukdag 1–14 enligt Lag (1991:1047) om sjuklön).
///
/// Struktur (övergripande): per kön redovisas antal anställda, antal med utbetald
/// sjuklön, summa sjukdagar i sjuklöneperioden, sjukfrånvaro i procent av möjliga
/// arbetsdagar samt summa utbetald sjuklön. En totalrad summerar hela populationen.
///
/// ÄRLIG MÄRKNING: Endast filen genereras. Skarp inlämning till SCB kräver
/// inloggning i uppgiftslämnartjänsten och är frånkopplad; <see cref="OverforingStatus"/>
/// stämplas i varje fil.
/// </summary>
public sealed class SCBSjuklonestatistikGenerator
{
    /// <summary>Sjuklöneperiodens längd i kalenderdagar (dag 1–14).</summary>
    public const int SjuklonePeriodDagar = 14;

    public const string OverforingStatus = "EJ_INLAMNAD_KRAVER_SCB_INLOGGNING";
    public const string Kodning = "ISO-8859-1";

    private const char Sep = ';';
    private const string Nl = "\r\n";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Aggregerar sjuklönerader per kön + total och bygger filen. Returnerar alltid
    /// ett resultat; tom indata ger nollställda grupper.
    /// </summary>
    public SCBSjuklonestatistikResultat Generera(SCBSjuklonestatistikInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var grupper = input.Individer
            .GroupBy(i => SCBLonestatistikGenerator.NormaliseraKon(i.Kon))
            .Select(g => Aggregera(g.Key, g.ToList()))
            .OrderBy(g => g.Kon, StringComparer.Ordinal)
            .ToList();

        var totalt = Aggregera("A", input.Individer);

        var innehall = ByggFil(input, grupper, totalt);
        var filnamn = $"SCB_SJUKLONESTAT_{Rensa(input.Organisationsnummer)}_{input.Ar:D4}K{Math.Clamp(input.Kvartal, 1, 4)}.csv";

        return new SCBSjuklonestatistikResultat(
            Filnamn: filnamn,
            Innehall: innehall,
            Grupper: grupper,
            Totalt: totalt,
            Overforingsstatus: OverforingStatus);
    }

    private static SCBSjuklonGrupp Aggregera(string kon, IReadOnlyList<SCBSjuklonIndivid> rader)
    {
        var antal = rader.Count;
        var medSjuklon = rader.Count(r => r.SjukdagarISjuklonePeriod > 0);
        var summaSjukdagar = rader.Sum(r => Math.Clamp(r.SjukdagarISjuklonePeriod, 0, SjuklonePeriodDagar));
        var summaMojliga = rader.Sum(r => Math.Max(0m, r.MojligaArbetsdagar));
        var summaSjuklon = rader.Sum(r => Math.Max(0m, r.UtbetaldSjuklon));

        var procent = summaMojliga > 0m
            ? Math.Round(summaSjukdagar / summaMojliga * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        return new SCBSjuklonGrupp(
            Kon: kon,
            AntalAnstallda: antal,
            AntalMedSjuklon: medSjuklon,
            SummaSjukdagar: summaSjukdagar,
            SjukfranvaroProcent: procent,
            SummaUtbetaldSjuklon: Math.Round(summaSjuklon, 2, MidpointRounding.AwayFromZero));
    }

    private static string ByggFil(
        SCBSjuklonestatistikInput input,
        IReadOnlyList<SCBSjuklonGrupp> grupper,
        SCBSjuklonGrupp totalt)
    {
        var rader = new List<string>
        {
            "#TYP=SCB-SJUKLONESTATISTIK-KSJU",
            $"#UPPGIFTSLAMNARE={input.Organisationsnamn} ({input.Organisationsnummer})",
            $"#PERIOD={input.Ar:D4} kvartal {Math.Clamp(input.Kvartal, 1, 4)}",
            $"#GENERERAD={DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", Inv)}",
            $"#KODNING={Kodning}",
            "#DECIMALTECKEN=.",
            "#FALTAVSKILJARE=;",
            $"#SJUKLONEPERIOD_DAGAR={SjuklonePeriodDagar.ToString(Inv)}",
            $"#STATUS={OverforingStatus}",
            string.Join(Sep,
                "Kon", "AntalAnstallda", "AntalMedSjuklon", "SummaSjukdagar",
                "SjukfranvaroProcent", "SummaUtbetaldSjuklon"),
        };

        foreach (var g in grupper)
            rader.Add(FormateraRad(g));
        rader.Add(FormateraRad(totalt));

        return string.Join(Nl, rader) + Nl;
    }

    private static string FormateraRad(SCBSjuklonGrupp g) => string.Join(Sep,
        g.Kon,
        g.AntalAnstallda.ToString(Inv),
        g.AntalMedSjuklon.ToString(Inv),
        g.SummaSjukdagar.ToString(Inv),
        g.SjukfranvaroProcent.ToString("F2", Inv),
        g.SummaUtbetaldSjuklon.ToString("F2", Inv));

    private static string Rensa(string s) =>
        new(s.Where(char.IsLetterOrDigit).ToArray());
}

/// <summary>Uppgiftslämnare + sjuklönerader för en period (kvartal).</summary>
public sealed class SCBSjuklonestatistikInput
{
    public int Ar { get; set; }

    /// <summary>Kvartal 1–4 (KSju redovisas kvartalsvis).</summary>
    public int Kvartal { get; set; } = 1;

    public string Organisationsnamn { get; set; } = string.Empty;
    public string Organisationsnummer { get; set; } = string.Empty;
    public List<SCBSjuklonIndivid> Individer { get; set; } = [];
}

/// <summary>En anställds sjuklöneuppgift för perioden (före aggregering).</summary>
public sealed class SCBSjuklonIndivid
{
    /// <summary>Kön: "K"/"M" eller "Kvinna"/"Man".</summary>
    public string Kon { get; set; } = string.Empty;

    /// <summary>Antal sjukdagar inom sjuklöneperioden (dag 1–14) under perioden.</summary>
    public int SjukdagarISjuklonePeriod { get; set; }

    /// <summary>Utbetald sjuklön (kr) under perioden.</summary>
    public decimal UtbetaldSjuklon { get; set; }

    /// <summary>Möjliga (ordinarie) arbetsdagar för individen under perioden.</summary>
    public decimal MojligaArbetsdagar { get; set; }
}

/// <summary>Aggregerad sjuklönerad (en könscell eller totalen).</summary>
public sealed record SCBSjuklonGrupp(
    string Kon,
    int AntalAnstallda,
    int AntalMedSjuklon,
    int SummaSjukdagar,
    decimal SjukfranvaroProcent,
    decimal SummaUtbetaldSjuklon);

/// <summary>Resultatet: filen + grupperna + totalraden.</summary>
public sealed record SCBSjuklonestatistikResultat(
    string Filnamn,
    string Innehall,
    IReadOnlyList<SCBSjuklonGrupp> Grupper,
    SCBSjuklonGrupp Totalt,
    string Overforingsstatus)
{
    /// <summary>Filinnehållet kodat i ISO-8859-1 (Latin-1) för nedladdning/överföring.</summary>
    public byte[] Bytes => Encoding.Latin1.GetBytes(Innehall);
}
