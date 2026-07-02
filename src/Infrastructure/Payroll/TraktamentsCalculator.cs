namespace RegionHR.Infrastructure.Payroll;

/// <summary>
/// Beräknar traktamente (dag/natt), måltidsavdrag och bilersättning enligt Skatteverkets regler.
/// Satserna är <b>årsversionerade per inkomstår</b> — ändra aldrig en "magisk konstant" i logiken,
/// lägg i stället in ett nytt <see cref="TraktamenteSatser"/>-år i <see cref="Satser"/>.
///
/// Primärkälla (verifierad): Skatteverket <b>SKV 354 "Traktamenten och andra kostnadsersättningar",
/// utgåva 36</b>, utgiven december 2025, gäller inkomstår 2026.
/// - Helt maximibelopp inrikes 2026 = 300 kr, halvt = 150 kr, nattraktamente = 150 kr
///   (0,5 % av prisbasbeloppet 59 200 kr = 296 kr, avrundat till närmaste tiotal → 300 kr).
/// - Måltidsavdrag INRIKES (andel av dagens maximibelopp): frukost 20 % (60 kr), lunch 35 % (105 kr),
///   middag 35 % (105 kr). Vid helt fri kost minskas traktamentet med 90 % → endast 10 % (30 kr)
///   betalas skattefritt för småutgifter (SKV 354 utg. 36, sid 4, "Minskning för kost").
/// - Måltidsavdrag UTRIKES (andel av landets normalbelopp): frukost 15 %, lunch 35 %, middag 35 %,
///   helt fri kost 85 % (SKV 354 utg. 36, avsnitt "Tjänsteresa utomlands").
/// - Skattefri bilersättning egen bil = 25 kr/mil, förmånsbil = 12 kr/mil, förmånsbil helt el = 9,50 kr/mil.
///
/// Utlands-normalbeloppen (per land) fastställs årligen i Skatteverkets allmänna råd
/// ("Normalbelopp för ökade levnadskostnader i utlandet"). Den fullständiga listan (~200 länder)
/// publiceras även som CSV/API på skatteverket.se/utlandstraktamente och bör på sikt laddas
/// därifrån. Tabellen i <see cref="UtrikesNormalbelopp"/> täcker ~50 av de vanligaste
/// resmålen för inkomstår 2026 och är <b>urvalsverifierad</b>: de belopp som ingår är avstämda
/// mot Skatteverkets normalbelopp 2026 via två oberoende sammanställningar (Björn Lundén samt
/// foretagande.se, hämtade 2026-07) som samstämmigt återger Skatteverkets värden. Länder som
/// saknas i tabellen faller tillbaka på det dokumenterade default-normalbeloppet
/// (<see cref="UtrikesDefaultNormalbelopp"/>, "Övriga länder och områden").
/// </summary>
public class TraktamentsCalculator
{
    /// <summary>Senast kända (och fullt verifierade) inkomstår. Framtida år faller tillbaka hit.</summary>
    public const int SenastKandaAr = 2026;

    // Årsversionerade satser. Endast 2026 är fullt verifierad mot SKV 354 utg. 36.
    // Tidigare år: helt/halvt maximibelopp är kända (prisbasbelopp + SKV; 2024=2025=290, 2023=260),
    // övriga andelar är de sedan länge gällande reglerna ("10 % kvar för småutgifter" inrikes,
    // 15/35/35/85 utrikes).
    private static readonly IReadOnlyDictionary<int, TraktamenteSatser> Satser = new Dictionary<int, TraktamenteSatser>
    {
        // inkomstår, heltInrikes, halvtInrikes, nattInrikes, milEgen, milFormans, milFormansEl,
        //   avdragFrukostInr, avdragLunchInr, avdragMiddagInr, avdragFrukostUtr, avdragLunchUtr, avdragMiddagUtr
        [2023] = new(2023, 260m, 130m, 130m, 25m, 12m, 9.50m, 0.20m, 0.35m, 0.35m, 0.15m, 0.35m, 0.35m),
        [2024] = new(2024, 290m, 145m, 145m, 25m, 12m, 9.50m, 0.20m, 0.35m, 0.35m, 0.15m, 0.35m, 0.35m),
        [2025] = new(2025, 290m, 145m, 145m, 25m, 12m, 9.50m, 0.20m, 0.35m, 0.35m, 0.15m, 0.35m, 0.35m),
        [2026] = new(2026, 300m, 150m, 150m, 25m, 12m, 9.50m, 0.20m, 0.35m, 0.35m, 0.15m, 0.35m, 0.35m),
    };

    /// <summary>
    /// Utlands-normalbelopp/hel dag i SEK per land och inkomstår, avstämt mot Skatteverkets
    /// normalbelopp 2026 (se klassdok för källor). Nycklar lagras med svenskt visningsnamn men
    /// slås upp skiftlägesokänsligt (<see cref="StringComparer.OrdinalIgnoreCase"/>). Land som
    /// saknas → <see cref="UtrikesDefaultNormalbelopp"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<string, decimal>> UtrikesNormalbelopp =
        new Dictionary<int, IReadOnlyDictionary<string, decimal>>
        {
            // Inkomstår 2026 — Skatteverkets normalbelopp (SKV allmänna råd, hel dag exkl. logi).
            // Belopp urvalsverifierade mot två oberoende sammanställningar (se klassdok).
            [2026] = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                // Norden
                ["Norge"] = 1054m, ["Danmark"] = 1226m, ["Finland"] = 968m, ["Island"] = 1125m,
                // Västeuropa
                ["Tyskland"] = 760m, ["Frankrike"] = 850m, ["Storbritannien"] = 901m,
                ["Spanien"] = 635m, ["Italien"] = 802m, ["Belgien"] = 897m,
                ["Nederländerna"] = 745m, ["Schweiz"] = 1332m, ["Österrike"] = 730m,
                ["Portugal"] = 616m, ["Irland"] = 1038m, ["Luxemburg"] = 953m,
                // Central- och Östeuropa
                ["Polen"] = 597m, ["Tjeckien"] = 695m, ["Ungern"] = 679m, ["Kroatien"] = 550m,
                ["Slovenien"] = 574m, ["Slovakien"] = 737m, ["Rumänien"] = 414m, ["Bulgarien"] = 459m,
                ["Estland"] = 683m, ["Lettland"] = 763m, ["Litauen"] = 635m, ["Ryssland"] = 661m,
                // Nord- och Sydamerika
                ["USA"] = 1049m, ["Kanada"] = 895m, ["Mexiko"] = 562m, ["Brasilien"] = 376m,
                ["Argentina"] = 388m,
                // Asien och Mellanöstern
                ["Japan"] = 386m, ["Kina"] = 581m, ["Indien"] = 300m, ["Sydkorea"] = 517m,
                ["Singapore"] = 845m, ["Hongkong"] = 895m, ["Thailand"] = 521m, ["Vietnam"] = 328m,
                ["Indonesien"] = 418m, ["Malaysia"] = 319m, ["Filippinerna"] = 466m,
                ["Förenade Arabemiraten"] = 883m, ["Israel"] = 956m, ["Turkiet"] = 366m,
                // Afrika och Oceanien
                ["Egypten"] = 300m, ["Marocko"] = 507m, ["Sydafrika"] = 349m,
                ["Australien"] = 727m, ["Nya Zeeland"] = 525m,
            },
        };

    /// <summary>"Övriga länder och områden" — dokumenterad default när land saknas i tabellen.</summary>
    public const decimal UtrikesDefaultNormalbelopp = 493m;

    /// <summary>Hämtar satser för angivet inkomstår med fallback till närmaste tidigare kända år (annars äldsta).</summary>
    public static TraktamenteSatser SatserForAr(int inkomstAr)
    {
        if (Satser.TryGetValue(inkomstAr, out var exact))
            return exact;

        var ar = Satser.Keys.OrderBy(k => k).ToList();
        if (inkomstAr < ar[0]) return Satser[ar[0]];
        var narmaste = ar.Where(k => k <= inkomstAr).Max();
        return Satser[narmaste];
    }

    /// <summary>Utlands-normalbelopp för land/år (se klassdok för källor). Okänt land → default.</summary>
    public static decimal GetUtrikesNormalbelopp(string land, int inkomstAr)
    {
        var tabell = UtrikesNormalbelopp[ResolveNormalbeloppArKey(inkomstAr)];
        // Skiftlägesokänslig uppslagning (comparer = OrdinalIgnoreCase); Trim tål mellanslag i UI-val.
        return tabell.TryGetValue((land ?? string.Empty).Trim(), out var belopp)
            ? belopp
            : UtrikesDefaultNormalbelopp;
    }

    /// <summary>
    /// Länder (svenska visningsnamn) som har ett fastställt normalbelopp för angivet inkomstår,
    /// sorterade alfabetiskt. Avsett för landsval i UI. Övriga länder använder
    /// <see cref="UtrikesDefaultNormalbelopp"/> och behöver inte listas.
    /// </summary>
    public static IReadOnlyList<string> UtrikesLander(int inkomstAr)
        => UtrikesNormalbelopp[ResolveNormalbeloppArKey(inkomstAr)]
            .Keys
            .OrderBy(k => k, StringComparer.Create(new System.Globalization.CultureInfo("sv-SE"), ignoreCase: false))
            .ToList();

    /// <summary>Väljer tabellår med fallback till närmaste tidigare år (annars äldsta kända).</summary>
    private static int ResolveNormalbeloppArKey(int inkomstAr)
    {
        if (UtrikesNormalbelopp.ContainsKey(inkomstAr)) return inkomstAr;
        var arNycklar = UtrikesNormalbelopp.Keys.OrderBy(k => k).ToList();
        return inkomstAr < arNycklar[0] ? arNycklar[0] : arNycklar.Where(k => k <= inkomstAr).Max();
    }

    /// <summary>
    /// Inrikes traktamente. Bakåtkompatibel signatur (avresa, hemkomst, hotell) — de fria måltiderna
    /// är valfria och ger måltidsavdrag. Inkomstår bestäms av avresedatumet.
    /// </summary>
    public TraktamentsBerakning BeraknaInrikes(
        DateTime avresa, DateTime hemkomst, bool hotell,
        int friaFrukostar = 0, int friaLuncher = 0, int friaMiddagar = 0)
    {
        var s = SatserForAr(avresa.Year);
        var timmar = (hemkomst - avresa).TotalHours;
        decimal dagtraktamente = 0;
        decimal nattillagg = 0;
        string beskrivning;

        if (timmar < 4) { beskrivning = "Resa under 4 timmar — inget traktamente"; }
        else if (timmar < 10) { dagtraktamente = s.HalvtMaximibeloppInrikes; beskrivning = $"Halvdag (4-10 timmar), {s.InkomstAr}"; }
        else { dagtraktamente = s.HeltMaximibeloppInrikes; beskrivning = $"Heldag (>10 timmar), {s.InkomstAr}"; }

        // Nattraktamente utgår bara om arbetsgivaren inte betalar logi (t.ex. övernattning hos bekant).
        if (!hotell && timmar >= 20) { nattillagg = s.NattraktamenteInrikes; }

        var antalDagar = Math.Max(1, (int)Math.Ceiling(timmar / 24));
        var totalDagtraktamente = dagtraktamente * antalDagar;
        var totalNatt = nattillagg * Math.Max(0, antalDagar - 1);

        var maltidsavdrag = BeraknaMaltidsavdragInrikes(friaFrukostar, friaLuncher, friaMiddagar, s.InkomstAr);
        if (maltidsavdrag > 0)
            beskrivning += $" - måltidsavdrag -{maltidsavdrag:N0} kr";

        var totalt = Math.Max(0m, totalDagtraktamente + totalNatt - maltidsavdrag);
        return new(totalDagtraktamente, totalNatt, totalt, antalDagar, beskrivning, maltidsavdrag);
    }

    /// <summary>
    /// Utrikes traktamente per landets normalbelopp. Bakåtkompatibel signatur (land, avresa, hemkomst);
    /// fria måltider valfria. Normalbelopp är provisoriska — se klassdok.
    /// </summary>
    public TraktamentsBerakning BeraknaUtrikes(
        string land, DateTime avresa, DateTime hemkomst,
        int friaFrukostar = 0, int friaLuncher = 0, int friaMiddagar = 0)
    {
        var inkomstAr = avresa.Year;
        var normalbelopp = GetUtrikesNormalbelopp(land, inkomstAr);
        var timmar = (hemkomst - avresa).TotalHours;
        var antalDagar = Math.Max(1, (int)Math.Ceiling(timmar / 24));
        var totalDag = normalbelopp * antalDagar;

        var maltidsavdrag = BeraknaMaltidsavdragUtrikes(normalbelopp, friaFrukostar, friaLuncher, friaMiddagar, inkomstAr);
        var beskrivning = $"Utrikes ({land}) {inkomstAr}: {normalbelopp:N0} kr/dag";
        if (maltidsavdrag > 0)
            beskrivning += $" - måltidsavdrag -{maltidsavdrag:N0} kr";

        var totalt = Math.Max(0m, totalDag - maltidsavdrag);
        return new(totalDag, 0m, totalt, antalDagar, beskrivning, maltidsavdrag);
    }

    /// <summary>
    /// Måltidsavdrag inrikes i kronor. Per fri måltid dras andel av <b>helt</b> maximibelopp:
    /// frukost 20 % (60 kr), lunch 35 % (105 kr), middag 35 % (105 kr) för 2026.
    /// </summary>
    public decimal BeraknaMaltidsavdragInrikes(int frukostar, int luncher, int middagar, int inkomstAr)
    {
        var s = SatserForAr(inkomstAr);
        var frukostAvdrag = Math.Round(s.HeltMaximibeloppInrikes * s.AvdragFrukostInrikes, 0, MidpointRounding.AwayFromZero);
        var lunchAvdrag = Math.Round(s.HeltMaximibeloppInrikes * s.AvdragLunchInrikes, 0, MidpointRounding.AwayFromZero);
        var middagAvdrag = Math.Round(s.HeltMaximibeloppInrikes * s.AvdragMiddagInrikes, 0, MidpointRounding.AwayFromZero);
        return Math.Max(0, frukostar) * frukostAvdrag
             + Math.Max(0, luncher) * lunchAvdrag
             + Math.Max(0, middagar) * middagAvdrag;
    }

    /// <summary>
    /// Måltidsavdrag utrikes i kronor. Per fri måltid dras andel av landets normalbelopp:
    /// frukost 15 %, lunch 35 %, middag 35 % (helt fri kost = 85 %).
    /// </summary>
    public decimal BeraknaMaltidsavdragUtrikes(decimal normalbelopp, int frukostar, int luncher, int middagar, int inkomstAr)
    {
        var s = SatserForAr(inkomstAr);
        var frukostAvdrag = Math.Round(normalbelopp * s.AvdragFrukostUtrikes, 0, MidpointRounding.AwayFromZero);
        var lunchAvdrag = Math.Round(normalbelopp * s.AvdragLunchUtrikes, 0, MidpointRounding.AwayFromZero);
        var middagAvdrag = Math.Round(normalbelopp * s.AvdragMiddagUtrikes, 0, MidpointRounding.AwayFromZero);
        return Math.Max(0, frukostar) * frukostAvdrag
             + Math.Max(0, luncher) * lunchAvdrag
             + Math.Max(0, middagar) * middagAvdrag;
    }

    /// <summary>Skattefri bilersättning i kronor för angivet antal mil, biltyp och inkomstår.</summary>
    public decimal BeraknaMilersattning(decimal mil, int inkomstAr, BilTyp typ = BilTyp.EgenBil)
    {
        var s = SatserForAr(inkomstAr);
        var sats = typ switch
        {
            BilTyp.EgenBil => s.MilersattningEgenBil,
            BilTyp.Formansbil => s.MilersattningFormansbil,
            BilTyp.FormansbilEl => s.MilersattningFormansbilEl,
            _ => s.MilersattningEgenBil,
        };
        return Math.Max(0m, mil) * sats;
    }
}

/// <summary>Biltyp för skattefri bilersättning (SKV 354).</summary>
public enum BilTyp
{
    /// <summary>Egen bil — 25 kr/mil (2026).</summary>
    EgenBil,
    /// <summary>Förmånsbil (ej helt el) — 12 kr/mil (2026).</summary>
    Formansbil,
    /// <summary>Förmånsbil som drivs helt med el — 9,50 kr/mil (2026).</summary>
    FormansbilEl,
}

/// <summary>
/// Årsversionerade traktamentssatser. Andelar (0–1) för måltidsavdrag, belopp i SEK.
/// </summary>
public sealed record TraktamenteSatser(
    int InkomstAr,
    decimal HeltMaximibeloppInrikes,
    decimal HalvtMaximibeloppInrikes,
    decimal NattraktamenteInrikes,
    decimal MilersattningEgenBil,
    decimal MilersattningFormansbil,
    decimal MilersattningFormansbilEl,
    decimal AvdragFrukostInrikes,
    decimal AvdragLunchInrikes,
    decimal AvdragMiddagInrikes,
    decimal AvdragFrukostUtrikes,
    decimal AvdragLunchUtrikes,
    decimal AvdragMiddagUtrikes);

/// <summary>
/// Resultat av en traktamentsberäkning. Positionella fält bevarade för bakåtkompatibilitet;
/// <see cref="Maltidsavdrag"/> tillagd sist (default 0).
/// </summary>
public record TraktamentsBerakning(
    decimal Dagtraktamente,
    decimal Natttillagg,
    decimal Totalt,
    int AntalDagar,
    string Beskrivning,
    decimal Maltidsavdrag = 0m);
