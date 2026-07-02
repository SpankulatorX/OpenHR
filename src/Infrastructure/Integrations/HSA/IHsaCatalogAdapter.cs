namespace RegionHR.Infrastructure.Integrations.HSA;

/// <summary>
/// Adapter mot HSA-katalogen (Hälso- och sjukvårdens adressregister, Inera).
/// Slår upp och synkar vårdens organisationsträd samt HSA-id för enheter och personer.
/// </summary>
/// <remarks>
/// En skarp implementation kräver ett Inera-avtal, ett SITHS-funktionscertifikat och
/// åtkomst till HSA:s WS-/LDAP-endpoint (t.ex. HSA WS Collaboration Engine över TLS,
/// eller LDAPS mot katalogen). Den medföljande <see cref="SandboxHsaCatalogAdapter"/>
/// är en DEMO som genererar deterministiska HSA-id lokalt och aldrig anropar Inera.
/// </remarks>
public interface IHsaCatalogAdapter
{
    /// <summary>Namn på integrationen (för UI och loggning).</summary>
    string SystemName { get; }

    /// <summary>
    /// <c>true</c> om detta är en demo-/sandbox-adapter utan skarp Inera-anslutning.
    /// </summary>
    bool IsSandbox { get; }

    /// <summary>Hämtar aktuell anslutningsstatus (inkl. sandbox-flagga).</summary>
    Task<HsaConnectionStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Slår upp en vårdenhet i HSA utifrån HSA-id, kostnadsställe eller namn.
    /// Returnerar <c>null</c> om ingen träff finns.
    /// </summary>
    Task<HsaUnit?> SlaUppEnhetAsync(string sokterm, CancellationToken ct = default);

    /// <summary>
    /// Slår upp en person i HSA utifrån HSA-id eller personnummer.
    /// Returnerar <c>null</c> om ingen träff finns.
    /// </summary>
    Task<HsaPerson?> SlaUppPersonAsync(string sokterm, CancellationToken ct = default);

    /// <summary>
    /// Hämtar hela (demo-)organisationsträdet från HSA för förhandsgranskning.
    /// </summary>
    Task<IReadOnlyList<HsaUnit>> HamtaOrganisationstradAsync(CancellationToken ct = default);
}
