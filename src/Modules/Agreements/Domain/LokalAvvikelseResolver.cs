using RegionHR.SharedKernel.Domain;

namespace RegionHR.Agreements.Domain;

/// <summary>
/// Ren, in-memory-resolver för lokala avtalsavvikelser. Innehåller ingen persistens och
/// kan därför konsulteras av vilket lager som helst — inklusive lönemotorn — så länge
/// anroparen har laddat de relevanta <see cref="LokalAvtalsAvvikelse"/>-posterna.
///
/// KÄRNFRÅGAN som resolvern besvarar: "gäller en lokal avvikelse för enhet X vid datum Y,
/// och vad blir i så fall det effektiva värdet ovanpå den centrala satsen?"
///
/// Precedens när flera OB-avvikelser gäller samtidigt för samma enhet/kategori:
///   1. <see cref="LokalBerakningsTyp.ErsattVarde"/> — ersätter basvärdet (senaste GiltigFran vinner).
///   2. <see cref="LokalBerakningsTyp.ProcentPaslag"/> — kompounderas i GiltigFran-ordning.
///   3. <see cref="LokalBerakningsTyp.FastBelopp"/> — summeras i GiltigFran-ordning.
/// Ordningen är avsiktligt deterministisk så att lön blir reproducerbar.
/// </summary>
public static class LokalAvvikelseResolver
{
    /// <summary>
    /// Alla avvikelser som gäller en enhet vid ett datum. Om <paramref name="avtalsId"/>
    /// anges filtreras även bort avvikelser som är låsta till ett ANNAT centralt avtal
    /// (avvikelser utan avtalskoppling gäller alltid).
    /// </summary>
    public static IReadOnlyList<LokalAvtalsAvvikelse> GallandeAvvikelser(
        IEnumerable<LokalAvtalsAvvikelse> alla,
        OrganizationId enhetId,
        DateOnly datum,
        CollectiveAgreementId? avtalsId = null)
    {
        ArgumentNullException.ThrowIfNull(alla);

        return alla
            .Where(a => a.GallerForEnhet(enhetId, datum))
            .Where(a => MatchandeAvtal(a, avtalsId))
            .OrderBy(a => a.GiltigFran)
            .ToList();
    }

    /// <summary>Finns någon gällande avvikelse av en viss typ för enheten vid datumet?</summary>
    public static bool FinnsAvvikelse(
        IEnumerable<LokalAvtalsAvvikelse> alla,
        OrganizationId enhetId,
        DateOnly datum,
        LokalAvvikelseTyp typ,
        CollectiveAgreementId? avtalsId = null)
        => GallandeAvvikelser(alla, enhetId, datum, avtalsId).Any(a => a.Typ == typ);

    /// <summary>
    /// Effektiv O-tilläggssats för en enhet efter att lokala OB-påslag lagts ovanpå den
    /// centrala satsen. Är inga lokala OB-påslag i kraft returneras <paramref name="centralObSats"/>
    /// oförändrad — det centrala avtalet är alltid utgångspunkten.
    /// </summary>
    public static decimal EffektivObSats(
        decimal centralObSats,
        IEnumerable<LokalAvtalsAvvikelse> alla,
        OrganizationId enhetId,
        OBCategory kategori,
        DateOnly datum,
        CollectiveAgreementId? avtalsId = null)
    {
        var relevanta = GallandeAvvikelser(alla, enhetId, datum, avtalsId)
            .Where(a => a.Typ == LokalAvvikelseTyp.ObPaslag)
            .Where(a => a.ObKategori is null || a.ObKategori == kategori)
            .ToList();

        if (relevanta.Count == 0)
            return centralObSats;

        var bas = centralObSats;

        // 1. Ersättning (senaste GiltigFran vinner om flera).
        var ersatt = relevanta
            .Where(a => a.BerakningsTyp == LokalBerakningsTyp.ErsattVarde)
            .OrderByDescending(a => a.GiltigFran)
            .FirstOrDefault();
        if (ersatt is not null)
            bas = ersatt.Varde;

        // 2. Procentpåslag kompounderas.
        foreach (var a in relevanta.Where(a => a.BerakningsTyp == LokalBerakningsTyp.ProcentPaslag))
            bas = a.TillampaPa(bas);

        // 3. Fasta belopp summeras.
        foreach (var a in relevanta.Where(a => a.BerakningsTyp == LokalBerakningsTyp.FastBelopp))
            bas = a.TillampaPa(bas);

        return bas;
    }

    private static bool MatchandeAvtal(LokalAvtalsAvvikelse a, CollectiveAgreementId? avtalsId)
        => a.AvtalsId is null || avtalsId is null || a.AvtalsId == avtalsId;
}
