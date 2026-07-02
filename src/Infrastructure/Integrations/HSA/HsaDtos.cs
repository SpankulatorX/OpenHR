namespace RegionHR.Infrastructure.Integrations.HSA;

/// <summary>
/// Typ av HSA-objekt i katalogträdet.
/// </summary>
public enum HsaUnitKind
{
    Organisation,
    Vardenhet,
    Funktion
}

/// <summary>
/// En organisatorisk enhet i HSA-katalogen (Hälso- och sjukvårdens adressregister).
/// Motsvarar objektklassen hsaOrganizationalUnit / vårdenhet i Ineras katalog.
/// </summary>
public sealed record HsaUnit(
    string HsaId,
    string Namn,
    string? OverordnadHsaId,
    string? Kostnadsstalle,
    string? Ort,
    HsaUnitKind Kind);

/// <summary>
/// En person (medarbetare) i HSA-katalogen. Motsvarar objektklassen hsaPerson.
/// </summary>
public sealed record HsaPerson(
    string HsaId,
    string Fornamn,
    string Efternamn,
    string? Titel,
    string? EnhetHsaId);

/// <summary>
/// Anslutningsstatus för HSA-adaptern. <see cref="IsSandbox"/> är <c>true</c> för
/// demo-/sandbox-implementationen som inte gör någon skarp Inera-anslutning.
/// </summary>
public sealed record HsaConnectionStatus(
    bool IsSandbox,
    bool IsReachable,
    string Description,
    DateTimeOffset CheckedAt);

/// <summary>
/// Resultat av en katalogsynk mot HSA (enheter och personer får HSA-id kopplat).
/// </summary>
public sealed record HsaSyncResult(
    bool Success,
    bool IsSandbox,
    int EnheterTotalt,
    int EnheterUppdaterade,
    int PersonerTotalt,
    int PersonerUppdaterade,
    IReadOnlyList<string> Meddelanden,
    string? Fel = null);
