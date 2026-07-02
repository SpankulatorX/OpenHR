using System.Globalization;
using System.Text;

namespace RegionHR.IntegrationHub.Adapters.SCB;

/// <summary>
/// Genererar SCB:s lönestrukturstatistik för regioner (integration #20) på
/// <b>aggregerad</b> nivå. Statistiken beskriver lönenivå, lönestruktur och
/// sysselsättning för anställda i regionen fördelat på yrke (SSYK 2012 / AID),
/// kön och sysselsättningsgrad. Mättidpunkt är september/november; regionernas
/// löneuppgifter samlas partsgemensamt in per den 1 november och SCB
/// (Medlingsinstitutet) producerar den officiella lönestrukturstatistiken.
///
/// Struktur (verifierad på övergripande nivå mot SCB Lönestrukturstatistik,
/// regioner, https://www.scb.se): per yrke × kön redovisas antal, genomsnittlig
/// <b>heltidsuppräknad</b> grundlön samt lönespridning (percentilerna P10, P25,
/// median, P75, P90). Individlönen räknas upp till heltid som
/// <c>månadslön / (sysselsättningsgrad / 100)</c> innan aggregering, precis som
/// SCB gör för att jämförbara löner ska kunna redovisas oavsett tjänstgöringsgrad.
///
/// ÄRLIG MÄRKNING: Generatorn producerar ENDAST statistikfilen. Skarp inlämning
/// sker via SCB:s uppgiftslämnartjänst och kräver inloggning/behörighet; det
/// steget är avsiktligt frånkopplat. <see cref="OverforingStatus"/> stämplas i
/// varje fil (fält <c>#STATUS</c>).
/// </summary>
public sealed class SCBLonestatistikGenerator
{
    /// <summary>Status som stämplas i varje fil — filen är ett underlag, ej inlämnat.</summary>
    public const string OverforingStatus = "EJ_INLAMNAD_KRAVER_SCB_INLOGGNING";

    /// <summary>Filens teckenkodning (ISO-8859-1/Latin-1, svensk myndighetsstandard).</summary>
    public const string Kodning = "ISO-8859-1";

    private const char Sep = ';';
    private const string Nl = "\r\n";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Aggregerar individrader till lönestrukturstatistik per yrke × kön och
    /// bygger den semikolon-separerade filen. Returnerar alltid ett resultat;
    /// vid tom indata är gruppmängden tom och filen innehåller endast metadata.
    /// </summary>
    public SCBLonestatistikResultat Generera(SCBLonestatistikInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Endast individer med positiv sysselsättningsgrad kan heltidsuppräknas.
        var giltiga = input.Individer
            .Where(i => i.Sysselsattningsgrad > 0m && i.Manadslon >= 0m)
            .ToList();

        var grupper = giltiga
            .GroupBy(i => (Yrkeskod: (i.Yrkeskod ?? string.Empty).Trim(), Kon: NormaliseraKon(i.Kon)))
            .Select(g =>
            {
                var heltidsloner = g
                    .Select(i => i.Manadslon / (i.Sysselsattningsgrad / 100m))
                    .OrderBy(x => x)
                    .ToList();

                var yrkesbenamning = g
                    .Select(i => i.Yrkesbenamning?.Trim())
                    .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

                var arbetstider = g
                    .Where(i => i.OverenskommenArbetstidPerVecka is > 0m)
                    .Select(i => i.OverenskommenArbetstidPerVecka!.Value)
                    .ToList();

                return new SCBLoneGrupp(
                    Yrkeskod: g.Key.Yrkeskod.Length == 0 ? "OKÄND" : g.Key.Yrkeskod,
                    Yrkesbenamning: string.IsNullOrWhiteSpace(yrkesbenamning)
                        ? (g.Key.Yrkeskod.Length == 0 ? "Ospecificerad" : g.Key.Yrkeskod)
                        : yrkesbenamning!,
                    Kon: g.Key.Kon,
                    Antal: heltidsloner.Count,
                    MedelHeltidslon: Math.Round(heltidsloner.Average(), 0, MidpointRounding.AwayFromZero),
                    P10: Percentil(heltidsloner, 10m),
                    P25: Percentil(heltidsloner, 25m),
                    Median: Percentil(heltidsloner, 50m),
                    P75: Percentil(heltidsloner, 75m),
                    P90: Percentil(heltidsloner, 90m),
                    MedelSysselsattningsgrad: Math.Round(g.Average(i => i.Sysselsattningsgrad), 1, MidpointRounding.AwayFromZero),
                    MedelArbetstidPerVecka: arbetstider.Count == 0
                        ? (decimal?)null
                        : Math.Round(arbetstider.Average(), 1, MidpointRounding.AwayFromZero));
            })
            .OrderBy(g => g.Yrkeskod, StringComparer.Ordinal)
            .ThenBy(g => g.Kon, StringComparer.Ordinal)
            .ToList();

        var loneandel = KvinnorsLoneandel(giltiga);

        var innehall = ByggFil(input, grupper, giltiga.Count, loneandel);
        var filnamn = $"SCB_LONESTAT_{Rensa(input.Organisationsnummer)}_{input.Ar:D4}{input.Manad:D2}.csv";

        return new SCBLonestatistikResultat(
            Filnamn: filnamn,
            Innehall: innehall,
            Grupper: grupper,
            AntalIndivider: giltiga.Count,
            KvinnorLoneandelProcent: loneandel,
            Overforingsstatus: OverforingStatus);
    }

    private static string ByggFil(
        SCBLonestatistikInput input,
        IReadOnlyList<SCBLoneGrupp> grupper,
        int antalIndivider,
        decimal? loneandel)
    {
        var rader = new List<string>
        {
            "#TYP=SCB-LONESTRUKTURSTATISTIK-REGION",
            $"#UPPGIFTSLAMNARE={input.Organisationsnamn} ({input.Organisationsnummer})",
            $"#PERIOD={input.Ar:D4}-{input.Manad:D2}",
            $"#MATTIDPUNKT={new DateOnly(input.Ar, Math.Clamp(input.Manad, 1, 12), 1):yyyy-MM-dd}",
            $"#GENERERAD={DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", Inv)}",
            $"#KODNING={Kodning}",
            "#DECIMALTECKEN=.",
            "#FALTAVSKILJARE=;",
            $"#ANTAL_INDIVIDER={antalIndivider.ToString(Inv)}",
            $"#ANTAL_GRUPPER={grupper.Count.ToString(Inv)}",
            $"#KVINNOR_LONEANDEL_PROCENT={(loneandel is { } a ? a.ToString("F1", Inv) : "")}",
            $"#STATUS={OverforingStatus}",
            string.Join(Sep,
                "Yrkeskod", "Yrkesbenamning", "Kon", "Antal", "MedelHeltidslon",
                "P10", "P25", "Median", "P75", "P90", "MedelSysselsattningsgrad", "MedelArbetstidPerVecka"),
        };

        foreach (var g in grupper)
        {
            rader.Add(string.Join(Sep,
                g.Yrkeskod,
                g.Yrkesbenamning,
                g.Kon,
                g.Antal.ToString(Inv),
                g.MedelHeltidslon.ToString("F0", Inv),
                g.P10.ToString("F0", Inv),
                g.P25.ToString("F0", Inv),
                g.Median.ToString("F0", Inv),
                g.P75.ToString("F0", Inv),
                g.P90.ToString("F0", Inv),
                g.MedelSysselsattningsgrad.ToString("F1", Inv),
                g.MedelArbetstidPerVecka is { } t ? t.ToString("F1", Inv) : string.Empty));
        }

        return string.Join(Nl, rader) + Nl;
    }

    /// <summary>
    /// Kvinnors genomsnittliga heltidsuppräknade lön som andel (%) av mäns.
    /// Null om något av könen saknar individer.
    /// </summary>
    private static decimal? KvinnorsLoneandel(IReadOnlyList<SCBLoneIndivid> individer)
    {
        var man = individer.Where(i => NormaliseraKon(i.Kon) == "M").ToList();
        var kvinnor = individer.Where(i => NormaliseraKon(i.Kon) == "K").ToList();
        if (man.Count == 0 || kvinnor.Count == 0) return null;

        var manMedel = man.Average(i => i.Manadslon / (i.Sysselsattningsgrad / 100m));
        var kvinnorMedel = kvinnor.Average(i => i.Manadslon / (i.Sysselsattningsgrad / 100m));
        if (manMedel == 0m) return null;

        return Math.Round(kvinnorMedel / manMedel * 100m, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Percentil (0–100) ur en <b>stigande sorterad</b> lista med linjär
    /// interpolation mellan närliggande rang (samma metod som Excel PERCENTILE.INC).
    /// Resultatet avrundas till hela kronor.
    /// </summary>
    internal static decimal Percentil(IReadOnlyList<decimal> sorterade, decimal p)
    {
        if (sorterade.Count == 0) return 0m;
        if (sorterade.Count == 1) return Math.Round(sorterade[0], 0, MidpointRounding.AwayFromZero);

        var rang = p / 100m * (sorterade.Count - 1);
        var lag = (int)Math.Floor(rang);
        var hog = (int)Math.Ceiling(rang);
        var varde = lag == hog
            ? sorterade[lag]
            : sorterade[lag] + (sorterade[hog] - sorterade[lag]) * (rang - lag);

        return Math.Round(varde, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Normaliserar könsangivelse till "K" (kvinna) eller "M" (man).</summary>
    internal static string NormaliseraKon(string? kon)
    {
        if (string.IsNullOrWhiteSpace(kon)) return "M";
        var k = kon.Trim();
        return k[0] is 'K' or 'k' or '2' ? "K" : "M";
    }

    private static string Rensa(string s) =>
        new(s.Where(char.IsLetterOrDigit).ToArray());
}

/// <summary>Uppgiftslämnare + individrader för SCB-lönestatistiken.</summary>
public sealed class SCBLonestatistikInput
{
    /// <summary>Statistikår (mätåret).</summary>
    public int Ar { get; set; }

    /// <summary>Mätmånad (normalt 11 = november).</summary>
    public int Manad { get; set; } = 11;

    public string Organisationsnamn { get; set; } = string.Empty;
    public string Organisationsnummer { get; set; } = string.Empty;
    public List<SCBLoneIndivid> Individer { get; set; } = [];
}

/// <summary>En anställds löneuppgift vid mättidpunkten (före aggregering).</summary>
public sealed class SCBLoneIndivid
{
    /// <summary>SSYK 2012- eller AID-kod för yrket.</summary>
    public string Yrkeskod { get; set; } = string.Empty;

    /// <summary>Läsbar yrkesbenämning (befattningstitel).</summary>
    public string Yrkesbenamning { get; set; } = string.Empty;

    /// <summary>Kön: "K"/"M" eller "Kvinna"/"Man".</summary>
    public string Kon { get; set; } = string.Empty;

    /// <summary>Faktisk månadslön (grundlön) för tjänstgöringsgraden.</summary>
    public decimal Manadslon { get; set; }

    /// <summary>Sysselsättningsgrad i procent (0–100).</summary>
    public decimal Sysselsattningsgrad { get; set; }

    /// <summary>Överenskommen arbetstid, timmar/vecka (valfritt).</summary>
    public decimal? OverenskommenArbetstidPerVecka { get; set; }
}

/// <summary>Aggregerad rad: en yrke × kön-cell i lönestrukturstatistiken.</summary>
public sealed record SCBLoneGrupp(
    string Yrkeskod,
    string Yrkesbenamning,
    string Kon,
    int Antal,
    decimal MedelHeltidslon,
    decimal P10,
    decimal P25,
    decimal Median,
    decimal P75,
    decimal P90,
    decimal MedelSysselsattningsgrad,
    decimal? MedelArbetstidPerVecka);

/// <summary>Resultatet: filen + de aggregerade grupperna för förhandsvisning/test.</summary>
public sealed record SCBLonestatistikResultat(
    string Filnamn,
    string Innehall,
    IReadOnlyList<SCBLoneGrupp> Grupper,
    int AntalIndivider,
    decimal? KvinnorLoneandelProcent,
    string Overforingsstatus)
{
    /// <summary>Filinnehållet kodat i ISO-8859-1 (Latin-1) för nedladdning/överföring.</summary>
    public byte[] Bytes => Encoding.Latin1.GetBytes(Innehall);
}
