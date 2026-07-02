namespace RegionHR.Analytics.Domain.BiExport;

/// <summary>
/// Dimensionsmodellerad (stjärnschema) representation av personal-, löne- och
/// frånvarodata avsedd för externa BI-/DW-verktyg (Power BI, Diver, REDA DW).
///
/// Modellen är medvetet <b>fristående från domänmodellen</b> — den innehåller bara
/// platta primitiver (string/int/decimal) så att den kan serialiseras rakt av till
/// CSV/JSON utan att läcka interna typer. Byggaren
/// (<c>RegionHR.Infrastructure.Reporting.BiDwExportBuilder</c>) mappar domänentiteter
/// till dessa poster; serialiseraren
/// (<c>RegionHR.Analytics.Domain.BiExport.BiExportGenerator</c>) skriver ut dem.
///
/// Kornighet (grain):
///  • FaktaAnstallning — en rad per aktiv anställning vid snapshot-datumet.
///  • FaktaLon         — en rad per löneresultat (anställd × period).
///  • FaktaFranvaro    — en rad per frånvaropost (begäran).
/// </summary>
public sealed record BiStjarnschema(
    IReadOnlyList<BiDimTid> DimTid,
    IReadOnlyList<BiDimEnhet> DimEnhet,
    IReadOnlyList<BiDimBefattning> DimBefattning,
    IReadOnlyList<BiDimKon> DimKon,
    IReadOnlyList<BiDimAlder> DimAlder,
    IReadOnlyList<BiFaktaAnstallning> FaktaAnstallning,
    IReadOnlyList<BiFaktaLon> FaktaLon,
    IReadOnlyList<BiFaktaFranvaro> FaktaFranvaro,
    DateOnly SnapshotDatum,
    DateTime GenereradVid)
{
    /// <summary>Totalt antal faktarader över alla faktatabeller.</summary>
    public int AntalFaktarader => FaktaAnstallning.Count + FaktaLon.Count + FaktaFranvaro.Count;

    /// <summary>Totalt antal dimensionsrader över alla dimensionstabeller.</summary>
    public int AntalDimensionsrader =>
        DimTid.Count + DimEnhet.Count + DimBefattning.Count + DimKon.Count + DimAlder.Count;
}

// ── Dimensioner ─────────────────────────────────────────────────────────────

/// <summary>Tidsdimension. Nyckel: "YYYY-MM".</summary>
public sealed record BiDimTid(
    string TidId,
    int Ar,
    int Kvartal,
    int Manad,
    string ManadNamn);

/// <summary>Organisatorisk enhet (kostnadsställe). Nyckel: enhetens Guid som sträng.</summary>
public sealed record BiDimEnhet(
    string EnhetId,
    string Namn,
    string Kostnadsstalle,
    string Typ,
    string? OverordnadEnhetId,
    string? HsaId);

/// <summary>Befattningsdimension. Nyckel: normaliserad befattningstitel.</summary>
public sealed record BiDimBefattning(
    string BefattningId,
    string Titel,
    string? BESTAKod,
    string? AIDKod);

/// <summary>Kön (juridiskt, härlett ur personnummer). Nyckel: "M"/"K"/"U".</summary>
public sealed record BiDimKon(
    string KonId,
    string Beteckning);

/// <summary>Åldersintervall. Nyckel: intervalletiketten, t.ex. "30-39".</summary>
public sealed record BiDimAlder(
    string AlderId,
    string Intervall,
    int MinAlder,
    int MaxAlder);

// ── Fakta ───────────────────────────────────────────────────────────────────

/// <summary>Faktatabell: anställningar (grain = en aktiv anställning vid snapshot).</summary>
public sealed record BiFaktaAnstallning(
    string TidId,
    string EnhetId,
    string BefattningId,
    string KonId,
    string AlderId,
    int AntalAnstallningar,
    decimal Sysselsattningsgrad,
    decimal Fte,
    decimal ManadslonSEK,
    int ArTillsvidare,
    int ArTidsbegransad);

/// <summary>Faktatabell: lönekostnad (grain = ett löneresultat, anställd × period).</summary>
public sealed record BiFaktaLon(
    string TidId,
    string EnhetId,
    string KonId,
    decimal BruttoSEK,
    decimal SkattSEK,
    decimal NettoSEK,
    decimal ArbetsgivaravgifterSEK,
    decimal PensionsavgiftSEK,
    decimal TotalArbetskraftskostnadSEK);

/// <summary>Faktatabell: frånvaro (grain = en frånvaropost/begäran).</summary>
public sealed record BiFaktaFranvaro(
    string TidId,
    string EnhetId,
    string KonId,
    string FranvaroTyp,
    int AntalDagar,
    int AntalFall);
