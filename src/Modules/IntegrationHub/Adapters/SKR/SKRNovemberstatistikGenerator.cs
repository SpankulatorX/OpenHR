using System.Globalization;
using System.Text;

namespace RegionHR.IntegrationHub.Adapters.SKR;

/// <summary>
/// Genererar SKR:s novemberstatistik (integration #25) — den partsgemensamma
/// personalstatistik som Sveriges Kommuner och Regioner samlar in årligen med
/// mättidpunkt <b>1 november</b>. Statistiken används som underlag för
/// avtalsförhandlingar, personalplanering och uppföljning.
///
/// Struktur (verifierad på övergripande nivå mot SKR "Om novemberstatistiken",
/// https://skr.se): anställda klassas efter personalgrupp/AID (Arbetsidentifikation)
/// och redovisas aggregerat per personalgrupp × kön med antal anställda
/// (tillsvidare/visstid), <b>årsarbetare</b> (summan av sysselsättningsgraderna),
/// genomsnittlig sysselsättningsgrad, medelålder samt sjukfrånvaro. En totalrad
/// summerar hela regionen.
///
/// ÄRLIG MÄRKNING: Endast filen genereras. Skarp inlämning sker via SKR:s
/// insamlingsportal och kräver inloggning/behörighet; det steget är frånkopplat.
/// <see cref="OverforingStatus"/> stämplas i varje fil.
/// </summary>
public sealed class SKRNovemberstatistikGenerator
{
    public const string OverforingStatus = "EJ_INLAMNAD_KRAVER_SKR_INLOGGNING";
    public const string Kodning = "ISO-8859-1";

    private const char Sep = ';';
    private const string Nl = "\r\n";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Aggregerar individrader per personalgrupp × kön + total och bygger filen.
    /// Mättidpunkten är alltid 1 november för angivet år.
    /// </summary>
    public SKRNovemberstatistikResultat Generera(SKRNovemberstatistikInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var grupper = input.Individer
            .GroupBy(i => (
                Grupp: string.IsNullOrWhiteSpace(i.Personalgrupp) ? "Ospecificerad" : i.Personalgrupp.Trim(),
                Kon: SKRKon.Normalisera(i.Kon)))
            .Select(g => Aggregera(g.Key.Grupp, g.Key.Kon, g.ToList()))
            .OrderBy(g => g.Personalgrupp, StringComparer.Ordinal)
            .ThenBy(g => g.Kon, StringComparer.Ordinal)
            .ToList();

        var totalt = Aggregera("TOTALT", "A", input.Individer);

        var innehall = ByggFil(input, grupper, totalt);
        var filnamn = $"SKR_NOVEMBERSTAT_{Rensa(input.Organisationsnummer)}_{input.Ar:D4}.csv";

        return new SKRNovemberstatistikResultat(
            Filnamn: filnamn,
            Innehall: innehall,
            Mattidpunkt: input.Mattidpunkt,
            Grupper: grupper,
            Totalt: totalt,
            Overforingsstatus: OverforingStatus);
    }

    private static SKRNovemberGrupp Aggregera(string grupp, string kon, IReadOnlyList<SKRNovemberIndivid> rader)
    {
        var antal = rader.Count;
        var tillsvidare = rader.Count(r => r.ArTillsvidare);
        var arsarbetare = rader.Sum(r => Math.Clamp(r.Sysselsattningsgrad, 0m, 100m) / 100m);

        var medelSyss = antal > 0
            ? Math.Round(rader.Average(r => r.Sysselsattningsgrad), 1, MidpointRounding.AwayFromZero)
            : 0m;
        var medelalder = antal > 0
            ? Math.Round(rader.Average(r => (decimal)r.Alder), 1, MidpointRounding.AwayFromZero)
            : 0m;

        var franvaroVarden = rader
            .Where(r => r.Franvaroprocent is not null)
            .Select(r => r.Franvaroprocent!.Value)
            .ToList();
        decimal? franvaro = franvaroVarden.Count > 0
            ? Math.Round(franvaroVarden.Average(), 2, MidpointRounding.AwayFromZero)
            : null;

        return new SKRNovemberGrupp(
            Personalgrupp: grupp,
            Kon: kon,
            AntalAnstallda: antal,
            AntalTillsvidare: tillsvidare,
            AntalVisstid: antal - tillsvidare,
            Arsarbetare: Math.Round(arsarbetare, 2, MidpointRounding.AwayFromZero),
            MedelSysselsattningsgrad: medelSyss,
            Medelalder: medelalder,
            Franvaroprocent: franvaro);
    }

    private static string ByggFil(
        SKRNovemberstatistikInput input,
        IReadOnlyList<SKRNovemberGrupp> grupper,
        SKRNovemberGrupp totalt)
    {
        var rader = new List<string>
        {
            "#TYP=SKR-NOVEMBERSTATISTIK",
            $"#UPPGIFTSLAMNARE={input.Organisationsnamn} ({input.Organisationsnummer})",
            $"#AR={input.Ar:D4}",
            $"#MATTIDPUNKT={input.Mattidpunkt:yyyy-MM-dd}",
            $"#GENERERAD={DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", Inv)}",
            $"#KODNING={Kodning}",
            "#DECIMALTECKEN=.",
            "#FALTAVSKILJARE=;",
            $"#STATUS={OverforingStatus}",
            string.Join(Sep,
                "Personalgrupp", "Kon", "AntalAnstallda", "AntalTillsvidare", "AntalVisstid",
                "Arsarbetare", "MedelSysselsattningsgrad", "Medelalder", "Franvaroprocent"),
        };

        foreach (var g in grupper)
            rader.Add(FormateraRad(g));
        rader.Add(FormateraRad(totalt));

        return string.Join(Nl, rader) + Nl;
    }

    private static string FormateraRad(SKRNovemberGrupp g) => string.Join(Sep,
        g.Personalgrupp,
        g.Kon,
        g.AntalAnstallda.ToString(Inv),
        g.AntalTillsvidare.ToString(Inv),
        g.AntalVisstid.ToString(Inv),
        g.Arsarbetare.ToString("F2", Inv),
        g.MedelSysselsattningsgrad.ToString("F1", Inv),
        g.Medelalder.ToString("F1", Inv),
        g.Franvaroprocent is { } f ? f.ToString("F2", Inv) : string.Empty);

    private static string Rensa(string s) =>
        new(s.Where(char.IsLetterOrDigit).ToArray());
}

/// <summary>Könsnormalisering för SKR-statistiken.</summary>
internal static class SKRKon
{
    /// <summary>
    /// Normaliserar könsangivelse till "K" (kvinna) eller "M" (man).
    /// Tom eller okänd angivelse ger "Okänt" och bildar en egen grupp i stället för
    /// att snedvrida statistiken genom att felaktigt räknas som man.
    /// </summary>
    public static string Normalisera(string? kon)
    {
        if (string.IsNullOrWhiteSpace(kon)) return "Okänt";
        return kon.Trim()[0] switch
        {
            'K' or 'k' or '2' => "K",
            'M' or 'm' or '1' => "M",
            _ => "Okänt"
        };
    }
}

/// <summary>Uppgiftslämnare + individrader för novemberstatistiken.</summary>
public sealed class SKRNovemberstatistikInput
{
    /// <summary>Statistikår. Mättidpunkten är 1 november detta år.</summary>
    public int Ar { get; set; }

    public string Organisationsnamn { get; set; } = string.Empty;
    public string Organisationsnummer { get; set; } = string.Empty;
    public List<SKRNovemberIndivid> Individer { get; set; } = [];

    /// <summary>Mättidpunkt = 1 november för statistikåret.</summary>
    public DateOnly Mattidpunkt => new(Ar, 11, 1);
}

/// <summary>En anställds uppgift vid mättidpunkten (före aggregering).</summary>
public sealed class SKRNovemberIndivid
{
    /// <summary>Personalgrupp/AID-benämning (t.ex. "Sjuksköterska", "Undersköterska").</summary>
    public string Personalgrupp { get; set; } = string.Empty;

    /// <summary>Kön: "K"/"M" eller "Kvinna"/"Man".</summary>
    public string Kon { get; set; } = string.Empty;

    /// <summary>Sant för tillsvidareanställning, annars visstid.</summary>
    public bool ArTillsvidare { get; set; }

    /// <summary>Sysselsättningsgrad i procent (0–100).</summary>
    public decimal Sysselsattningsgrad { get; set; }

    /// <summary>Ålder i hela år vid mättidpunkten.</summary>
    public int Alder { get; set; }

    /// <summary>Sjukfrånvaro i procent av ordinarie arbetstid (valfritt).</summary>
    public decimal? Franvaroprocent { get; set; }
}

/// <summary>Aggregerad rad: personalgrupp × kön (eller totalen).</summary>
public sealed record SKRNovemberGrupp(
    string Personalgrupp,
    string Kon,
    int AntalAnstallda,
    int AntalTillsvidare,
    int AntalVisstid,
    decimal Arsarbetare,
    decimal MedelSysselsattningsgrad,
    decimal Medelalder,
    decimal? Franvaroprocent);

/// <summary>Resultatet: filen + grupperna + totalraden.</summary>
public sealed record SKRNovemberstatistikResultat(
    string Filnamn,
    string Innehall,
    DateOnly Mattidpunkt,
    IReadOnlyList<SKRNovemberGrupp> Grupper,
    SKRNovemberGrupp Totalt,
    string Overforingsstatus)
{
    /// <summary>Filinnehållet kodat i ISO-8859-1 (Latin-1) för nedladdning/överföring.</summary>
    public byte[] Bytes => Encoding.Latin1.GetBytes(Innehall);
}
