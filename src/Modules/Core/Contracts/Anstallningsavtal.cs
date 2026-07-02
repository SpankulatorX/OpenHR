using RegionHR.SharedKernel.Domain;

namespace RegionHR.Core.Contracts;

/// <summary>Ett avsnitt i den skriftliga anställningsinformationen (rubrik + text).</summary>
public record AvtalsAvsnitt(string Rubrik, string Text);

/// <summary>
/// Underlag för att generera anställningsavtal / skriftlig information om
/// anställningsvillkor enligt 6 c § lagen (1982:80) om anställningsskydd (LAS),
/// i lydelse fr.o.m. 2022-06-29 (implementering av EU:s arbetsvillkorsdirektiv 2019/1152).
/// </summary>
public record AnstallningsavtalUppgifter(
    string ArbetsgivareNamn,
    string ArbetsgivareAdress,
    string ArbetstagareNamn,
    string ArbetstagarePersonnummerMaskerat,
    string EnhetNamn,
    string Arbetsplats,
    string? Befattningstitel,
    EmploymentType Anstallningsform,
    decimal Manadslon,
    decimal Sysselsattningsgrad,
    DateOnly Tilltradesdag,
    DateOnly? Slutdatum,
    CollectiveAgreementType Kollektivavtal,
    string? KollektivavtalNamn,
    string? ArbetstagareAdress = null,
    decimal VeckoarbetstidHeltid = 38.25m,
    int LonutbetalningDag = 25);

/// <summary>
/// Genererar den lagstadgade skriftliga informationen enligt LAS 6 c §.
/// Systemet är experten: alla obligatoriska punkter fylls i automatiskt utifrån
/// anställningens data, användaren behöver inte känna till lagkraven.
/// </summary>
public static class AnstallningsavtalGenerator
{
    /// <summary>
    /// Lagstadgad uppsägningstid från arbetsgivaren enligt LAS 11 § (SFS 1982:80).
    /// Kollektivavtal (t.ex. AB/HÖK) kan innehålla längre uppsägningstider.
    /// </summary>
    /// <param name="anstallningstidHelaAr">Sammanlagd anställningstid hos arbetsgivaren, i hela år.</param>
    public static int LagstadgadUppsagningstidManader(int anstallningstidHelaAr) => anstallningstidHelaAr switch
    {
        >= 10 => 6,
        >= 8 => 5,
        >= 6 => 4,
        >= 4 => 3,
        >= 2 => 2,
        _ => 1
    };

    private static string FormenText(EmploymentType form) => form switch
    {
        EmploymentType.Tillsvidare => "Tillsvidareanställning",
        EmploymentType.Vikariat => "Vikariat (tidsbegränsad anställning)",
        EmploymentType.Provanstallning => "Provanställning",
        EmploymentType.SAVA => "Särskild visstidsanställning",
        EmploymentType.Sasongsanstallning => "Säsongsanställning",
        EmploymentType.Timavlonad => "Timavlönad (intermittent) anställning",
        _ => form.ToString()
    };

    /// <summary>
    /// Bygger de obligatoriska avsnitten enligt LAS 6 c §. Anropas av PDF-/dokumentgeneratorn
    /// (t.ex. Infrastructure PdfGenerator.AnstallningsavtalPdf) för att fylla avtalet.
    /// </summary>
    public static IReadOnlyList<AvtalsAvsnitt> Skapa6cInformation(AnstallningsavtalUppgifter u)
    {
        var avsnitt = new List<AvtalsAvsnitt>();

        // 1. Parterna, tillträdesdag och arbetsplats
        avsnitt.Add(new AvtalsAvsnitt(
            "Parter, tillträdesdag och arbetsplats",
            $"Arbetsgivare: {u.ArbetsgivareNamn}, {u.ArbetsgivareAdress}.\n" +
            $"Arbetstagare: {u.ArbetstagareNamn} ({u.ArbetstagarePersonnummerMaskerat})" +
            (u.ArbetstagareAdress is null ? "." : $", {u.ArbetstagareAdress}.") + "\n" +
            $"Tillträdesdag: {u.Tilltradesdag:yyyy-MM-dd}.\n" +
            $"Arbetsplats/enhet: {u.EnhetNamn}, {u.Arbetsplats}."));

        // 2. Arbetsuppgifter / yrkesbenämning
        avsnitt.Add(new AvtalsAvsnitt(
            "Arbetsuppgifter och yrkesbenämning",
            $"Befattning/yrkesbenämning: {u.Befattningstitel ?? "Fastställs vid tillträde"}. " +
            "Arbetsuppgifterna framgår av befattningen och kan komma att anpassas inom ramen för anställningen."));

        // 3. Anställningsform (med formspecifik obligatorisk uppgift)
        var formText = $"Anställningsform: {FormenText(u.Anstallningsform)}. ";
        if (u.Anstallningsform == EmploymentType.Tillsvidare)
        {
            formText += "Anställningen gäller tills vidare. Uppsägningstid gäller enligt LAS 11 § " +
                        "(minst en månad, därefter trappas den upp med anställningstiden) samt tillämpligt kollektivavtal.";
        }
        else if (u.Anstallningsform == EmploymentType.Provanstallning)
        {
            var provslut = u.Slutdatum?.ToString("yyyy-MM-dd") ?? "ej angivet";
            formText += $"Prövotiden löper t.o.m. {provslut} och får enligt LAS 6 § vara i högst " +
                        $"{RegionHR.Core.Domain.Employment.MaxProvanstallningManader} månader. Om anställningen inte avbryts " +
                        "vid prövotidens utgång övergår den i en tillsvidareanställning.";
        }
        else
        {
            var slut = u.Slutdatum?.ToString("yyyy-MM-dd") ?? "ej angivet";
            formText += $"Anställningen är tidsbegränsad och upphör {slut} utan föregående uppsägning, " +
                        "om inget annat avtalas.";
        }
        avsnitt.Add(new AvtalsAvsnitt("Anställningsform och anställningens varaktighet", formText));

        // 4. Begynnelselön och löneutbetalning
        avsnitt.Add(new AvtalsAvsnitt(
            "Lön och löneutbetalning",
            $"Begynnelselön: {u.Manadslon:N0} kr/mån vid heltid (sysselsättningsgrad {u.Sysselsattningsgrad:0.##} %). " +
            $"Lön betalas ut den {u.LonutbetalningDag}:e varje månad."));

        // 5. Arbetstid samt övertid/mertid
        avsnitt.Add(new AvtalsAvsnitt(
            "Arbetstid samt övertid och mertid",
            $"Ordinarie arbetstid för heltid är {u.VeckoarbetstidHeltid:0.##} timmar/vecka. " +
            $"Vid sysselsättningsgrad {u.Sysselsattningsgrad:0.##} % gäller motsvarande andel. " +
            "Övertid och mertid samt ersättning för detta regleras i tillämpligt kollektivavtal."));

        // 6. Semester
        avsnitt.Add(new AvtalsAvsnitt(
            "Semester",
            "Semester utgår enligt semesterlagen (1977:480) och tillämpligt kollektivavtal " +
            "(antal semesterdagar kan öka med ålder enligt avtalet)."));

        // 7. Kollektivavtal
        avsnitt.Add(new AvtalsAvsnitt(
            "Kollektivavtal",
            u.Kollektivavtal == CollectiveAgreementType.None
                ? "Anställningen omfattas inte av något kollektivavtal."
                : $"Anställningen omfattas av kollektivavtal {u.KollektivavtalNamn ?? u.Kollektivavtal.ToString()} " +
                  "med tillhörande centrala och lokala överenskommelser."));

        // 8. Social trygghet och försäkringar
        avsnitt.Add(new AvtalsAvsnitt(
            "Social trygghet och försäkringar",
            "Arbetsgivaren betalar lagstadgade arbetsgivaravgifter. Sjuklön, pension och avtalsförsäkringar " +
            "(t.ex. TGL, TFA, AGS-KL, KAP-KL/AKAP-KR) följer av lag och tillämpligt kollektivavtal."));

        // 9. Hur anställningen avslutas
        avsnitt.Add(new AvtalsAvsnitt(
            "Uppsägning och anställningens upphörande",
            "Uppsägning och anställningens upphörande regleras av LAS och tillämpligt kollektivavtal. " +
            $"Lagstadgad uppsägningstid från arbetsgivaren är {LagstadgadUppsagningstidManader(0)}–" +
            $"{LagstadgadUppsagningstidManader(10)} månader beroende på anställningstid (LAS 11 §)."));

        return avsnitt;
    }

    /// <summary>Samma information som löpande text (för enkel inbäddning i dokument/PDF).</summary>
    public static string Skapa6cText(AnstallningsavtalUppgifter u) =>
        string.Join("\n\n", Skapa6cInformation(u).Select(a => $"{a.Rubrik}\n{a.Text}"));
}
