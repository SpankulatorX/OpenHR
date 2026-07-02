using RegionHR.SharedKernel.Abstractions;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Agreements.Domain;

/// <summary>
/// En lokal avtalsavvikelse / lokal förmån knuten till EN organisationsenhet.
///
/// Detta är ett override-lager OVANPÅ det centrala kollektivavtalet: de centrala
/// satsklasserna (<see cref="ABOTillaggSatser"/>, <see cref="ABOvertidSatser"/>,
/// <see cref="ABSemesterRegler"/>) och <see cref="CollectiveAgreement"/> är oförändrade.
/// Lönemotorn läser fortsatt centrala satser och kan därefter konsultera
/// <see cref="LokalAvvikelseResolver"/> för att justera det EFFEKTIVA värdet för en
/// enhet under en giltighetsperiod.
///
/// Entiteten hålls avsiktligt platt (endast skalära fält, ingen samling → ingen jsonb)
/// eftersom persistensen använder EnsureCreated och fritext lagras som text-kolumn.
/// </summary>
public sealed class LokalAvtalsAvvikelse
{
    public Guid Id { get; private set; }

    /// <summary>Organisationsenheten som avvikelsen gäller för.</summary>
    public OrganizationId EnhetId { get; private set; }

    /// <summary>
    /// Valfri koppling till det centrala avtal som avvikelsen bygger ovanpå.
    /// Null = avvikelsen gäller oavsett vilket centralt avtal enheten har.
    /// </summary>
    public CollectiveAgreementId? AvtalsId { get; private set; }

    public LokalAvvikelseTyp Typ { get; private set; }

    /// <summary>
    /// Endast relevant för <see cref="LokalAvvikelseTyp.ObPaslag"/>: vilken O-tilläggskategori
    /// påslaget avser. Null = gäller alla OB-kategorier.
    /// </summary>
    public OBCategory? ObKategori { get; private set; }

    public string Namn { get; private set; } = string.Empty;

    /// <summary>Fritext som beskriver avvikelsen (text-kolumn, ej jsonb).</summary>
    public string Beskrivning { get; private set; } = string.Empty;

    public LokalBerakningsTyp BerakningsTyp { get; private set; }

    public LokalBeloppsEnhet Enhet { get; private set; }

    /// <summary>Beloppet/procenten som tillämpas enligt <see cref="BerakningsTyp"/>.</summary>
    public decimal Varde { get; private set; }

    public DateOnly GiltigFran { get; private set; }
    public DateOnly? GiltigTill { get; private set; }

    /// <summary>Inaktiverade avvikelser ignoreras av <see cref="GallerVid"/> och resolvern.</summary>
    public bool Aktiv { get; private set; } = true;

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public string? UpdatedBy { get; private set; }

    private LokalAvtalsAvvikelse() { } // EF Core

    /// <summary>
    /// Skapar en lokal avvikelse. Validerar koherens mellan beräkningstyp och beloppsenhet
    /// samt att en ev. slutgiltighet inte ligger före startgiltigheten.
    /// </summary>
    public static LokalAvtalsAvvikelse Skapa(
        OrganizationId enhetId,
        LokalAvvikelseTyp typ,
        string namn,
        LokalBerakningsTyp berakningsTyp,
        LokalBeloppsEnhet enhet,
        decimal varde,
        DateOnly giltigFran,
        DateOnly? giltigTill = null,
        OBCategory? obKategori = null,
        CollectiveAgreementId? avtalsId = null,
        string? beskrivning = null,
        string? createdBy = null)
    {
        if (string.IsNullOrWhiteSpace(namn))
            throw new DomainException("LOKAL_AVVIKELSE_NAMN_SAKNAS", "Lokal avvikelse måste ha ett namn.");

        ValideraPeriod(giltigFran, giltigTill);
        ValideraBerakning(berakningsTyp, enhet);

        return new LokalAvtalsAvvikelse
        {
            Id = Guid.NewGuid(),
            EnhetId = enhetId,
            Typ = typ,
            Namn = namn.Trim(),
            BerakningsTyp = berakningsTyp,
            Enhet = enhet,
            Varde = varde,
            GiltigFran = giltigFran,
            GiltigTill = giltigTill,
            ObKategori = typ == LokalAvvikelseTyp.ObPaslag ? obKategori : null,
            AvtalsId = avtalsId,
            Beskrivning = beskrivning?.Trim() ?? string.Empty,
            Aktiv = true,
            CreatedBy = createdBy
        };
    }

    /// <summary>Uppdaterar redigerbara fält och stämplar UpdatedAt/UpdatedBy.</summary>
    public void Uppdatera(
        OrganizationId enhetId,
        LokalAvvikelseTyp typ,
        string namn,
        LokalBerakningsTyp berakningsTyp,
        LokalBeloppsEnhet enhet,
        decimal varde,
        DateOnly giltigFran,
        DateOnly? giltigTill,
        OBCategory? obKategori,
        CollectiveAgreementId? avtalsId,
        string? beskrivning,
        string? updatedBy = null)
    {
        if (string.IsNullOrWhiteSpace(namn))
            throw new DomainException("LOKAL_AVVIKELSE_NAMN_SAKNAS", "Lokal avvikelse måste ha ett namn.");

        ValideraPeriod(giltigFran, giltigTill);
        ValideraBerakning(berakningsTyp, enhet);

        EnhetId = enhetId;
        Typ = typ;
        Namn = namn.Trim();
        BerakningsTyp = berakningsTyp;
        Enhet = enhet;
        Varde = varde;
        GiltigFran = giltigFran;
        GiltigTill = giltigTill;
        ObKategori = typ == LokalAvvikelseTyp.ObPaslag ? obKategori : null;
        AvtalsId = avtalsId;
        Beskrivning = beskrivning?.Trim() ?? string.Empty;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void Inaktivera(string? updatedBy = null)
    {
        Aktiv = false;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void Aktivera(string? updatedBy = null)
    {
        Aktiv = true;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>Gäller avvikelsen ett givet datum (aktiv och inom giltighetsfönstret)?</summary>
    public bool GallerVid(DateOnly datum)
        => Aktiv && GiltigFran <= datum && (GiltigTill == null || GiltigTill >= datum);

    /// <summary>Gäller avvikelsen en given enhet ett givet datum?</summary>
    public bool GallerForEnhet(OrganizationId enhetId, DateOnly datum)
        => EnhetId == enhetId && GallerVid(datum);

    /// <summary>
    /// Tillämpar avvikelsen på ett centralt basvärde och returnerar det effektiva värdet.
    /// För fristående förmåner/tillägg (utan centralt basvärde) anropas med bas = 0.
    /// </summary>
    public decimal TillampaPa(decimal centraltBasVarde) => BerakningsTyp switch
    {
        LokalBerakningsTyp.FastBelopp => centraltBasVarde + Varde,
        LokalBerakningsTyp.ProcentPaslag => centraltBasVarde * (1m + Varde / 100m),
        LokalBerakningsTyp.ErsattVarde => Varde,
        _ => centraltBasVarde
    };

    private static void ValideraPeriod(DateOnly giltigFran, DateOnly? giltigTill)
    {
        if (giltigTill.HasValue && giltigTill.Value < giltigFran)
            throw new DomainException(
                "LOKAL_AVVIKELSE_OGILTIG_PERIOD",
                "Giltig till får inte ligga före giltig från.");
    }

    private static void ValideraBerakning(LokalBerakningsTyp berakningsTyp, LokalBeloppsEnhet enhet)
    {
        // Procent-enhet hör ihop med procentpåslag och inget annat.
        if (berakningsTyp == LokalBerakningsTyp.ProcentPaslag && enhet != LokalBeloppsEnhet.Procent)
            throw new DomainException(
                "LOKAL_AVVIKELSE_PROCENT_KRAVER_PROCENTENHET",
                "Procentpåslag måste använda beloppsenheten Procent.");

        if (berakningsTyp != LokalBerakningsTyp.ProcentPaslag && enhet == LokalBeloppsEnhet.Procent)
            throw new DomainException(
                "LOKAL_AVVIKELSE_PROCENTENHET_KRAVER_PROCENT",
                "Beloppsenheten Procent kan bara användas med beräkningstypen Procentpåslag.");
    }
}
