namespace RegionHR.IntegrationHub.Framework;

/// <summary>
/// Persistensabstraktion för körningsloggen. Håller ramverket (och därmed
/// jobbrunner) fritt från EF Core — den EF-backade implementationen bor i
/// infrastrukturlagret. Testerna använder en enkel minnesimplementation.
/// </summary>
public interface IIntegrationRunLogStore
{
    /// <summary>Sparar utfallet av en körning.</summary>
    Task SparaAsync(IntegrationKorningsResultat resultat, CancellationToken ct = default);

    /// <summary>Hämtar den senaste körningen per integration (för översiktsvyn).</summary>
    Task<IReadOnlyList<IntegrationKorningsResultat>> HamtaSenastePerIntegrationAsync(CancellationToken ct = default);

    /// <summary>Hämtar körningshistorik för en integration (nyast först).</summary>
    Task<IReadOnlyList<IntegrationKorningsResultat>> HamtaHistorikAsync(
        string integrationKey, int antal = 20, CancellationToken ct = default);
}

/// <summary>
/// Utfallet av en integrationskörning — den immutabla posten som skrivs till
/// run-loggen. Speglas 1:1 av EF-entiteten <c>IntegrationRunLog</c> i
/// infrastrukturlagret.
/// </summary>
public sealed record IntegrationKorningsResultat(
    Guid Id,
    string IntegrationKey,
    string IntegrationNamn,
    DateTime StartadUtc,
    DateTime AvslutadUtc,
    IntegrationRunStatus Status,
    int AntalPoster,
    string? Filnamn,
    string? Plats,
    string TransportNamn,
    bool SkarpTransport,
    string UtlostAv,
    string? Fel,
    string? Anmarkning)
{
    /// <summary>Körtid i millisekunder.</summary>
    public long VaraktighetMs => (long)(AvslutadUtc - StartadUtc).TotalMilliseconds;
}
