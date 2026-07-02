namespace RegionHR.HalsoSAM.Domain;

/// <summary>
/// Årsversionerad parametertabell för den svenska rehabiliteringskedjan.
/// Milstolparna räknas ALLTID från sjukfallets faktiska dag 1 (första sjukdagen /
/// sjukanmälan / läkarintygets startdatum) — inte från när rehabärendet råkade skapas
/// i systemet.
///
/// Rättslig grund (verifierat 2026-07, Försäkringskassan + Socialförsäkringsbalken):
///   • Rehabiliteringskedjan, bedömning av arbetsförmåga vid 90/180/365 dagar:
///     Socialförsäkringsbalken (2010:110) 27 kap. 46–49 §§.
///       - Dag 1–90:   arbetsförmågan prövas mot ordinarie arbete hos arbetsgivaren.
///       - Dag 91–180: prövas mot annat arbete hos arbetsgivaren.
///       - Dag 181–365 och därefter: prövas mot normalt förekommande arbete på hela
///         arbetsmarknaden.
///   • Sjuklöneperioden är 14 dagar; arbetsgivaren anmäler till Försäkringskassan
///     från dag 15: Lag (1991:1047) om sjuklön 7 §, 12 §, 28 §.
///   • Läkarintyg krävs från dag 8: Lag (1991:1047) om sjuklön 8 §.
///   • Arbetsgivaren ska ha upprättat en plan för återgång i arbete senast dag 30
///     om sjukfrånvaron antas pågå längre än 60 dagar: Socialförsäkringsbalken
///     (2010:110) 30 kap. 6 §.
/// </summary>
public static class Rehabkedja
{
    /// <summary>Version av parametertabellen (svenskt regelverk detta år).</summary>
    public const int Version = 2026;

    /// <summary>
    /// Milstolparna (antal dagar från sjukfallets dag 1) som ska följas upp aktivt.
    /// Dessa driver <see cref="RehabCase.Uppfoljning14Dagar"/> m.fl.
    /// </summary>
    public static readonly IReadOnlyList<Milstolpe> Milstolpar =
    [
        new(14, "Sjuklöneperioden slut — anmälan till Försäkringskassan (dag 15)",
            "Lag (1991:1047) om sjuklön 12 §"),
        new(90, "Försäkringskassan prövar arbetsförmågan mot ordinarie arbete",
            "Socialförsäkringsbalken 27 kap. 47 §"),
        new(180, "Försäkringskassan prövar mot normalt förekommande arbete på arbetsmarknaden",
            "Socialförsäkringsbalken 27 kap. 48 §"),
        new(365, "Förlängd bedömning mot hela arbetsmarknaden",
            "Socialförsäkringsbalken 27 kap. 49 §"),
    ];

    /// <summary>Läkarintyg krävs från denna sjukdag.</summary>
    public const int LakarintygFranDag = 8;

    /// <summary>Anmälan till Försäkringskassan krävs från denna sjukdag.</summary>
    public const int ForsakringskassanAnmalanFranDag = 15;

    /// <summary>
    /// Arbetsgivaren ska senast denna dag ha en plan för återgång i arbete,
    /// om sjukfrånvaron antas pågå längre än <see cref="PlanAntasPagaLangreAnDagar"/> dagar.
    /// </summary>
    public const int PlanForAtergangSenastDag = 30;

    /// <summary>Tröskel för när plan för återgång i arbete blir lagkrav.</summary>
    public const int PlanAntasPagaLangreAnDagar = 60;

    // --- Trösklar för automatisk rehab-triggning ---

    /// <summary>
    /// Sammanhängande sjukdagar som automatiskt startar ett rehabärende.
    /// Sätts vid sjuklöneperiodens slut (14 dagar) då ärendet lämnar arbetsgivaren
    /// och Försäkringskassan tar vid.
    /// </summary>
    public const int AutoTriggerSammanhangandeDagar = 14;

    /// <summary>Antal sjuktillfällen inom fönstret som triggar rehabutredning.</summary>
    public const int AutoTriggerAntalTillfallen = 6;

    /// <summary>Fönster (månader) för räkning av upprepade sjuktillfällen.</summary>
    public const int AutoTriggerTillfallenFonsterManader = 12;

    /// <summary>En milstolpe i rehabiliteringskedjan.</summary>
    /// <param name="DagNr">Antal dagar från sjukfallets dag 1.</param>
    /// <param name="Beskrivning">Vad som händer/ska göras vid milstolpen.</param>
    /// <param name="Lagrum">Rättslig grund.</param>
    public sealed record Milstolpe(int DagNr, string Beskrivning, string Lagrum);
}
