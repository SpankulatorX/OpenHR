namespace RegionHR.Infrastructure.Integrations.HSA;

/// <summary>
/// DEMO/SANDBOX-implementation av <see cref="IHsaCatalogAdapter"/>.
/// Gör INGEN skarp anslutning mot HSA/Inera — den genererar deterministiska demo-HSA-id
/// lokalt och serverar ett fiktivt organisationsträd. Används för att demonstrera
/// katalogsynk-flödet utan avtal/certifikat.
/// </summary>
/// <remarks>
/// Skarp anslutning kräver: Inera-avtal, SITHS-funktionscertifikat samt HSA WS-/LDAP-endpoint.
/// Byt ut denna klass mot en riktig adapter och registrera den i DI när det är på plats.
/// </remarks>
public sealed class SandboxHsaCatalogAdapter : IHsaCatalogAdapter
{
    /// <summary>Fiktiv HSA-rot (Region Örebro läns orgnr) — endast för demo-id.</summary>
    public const string DemoHsaRoot = "SE2321000164";

    public string SystemName => "HSA-katalogen (Inera)";

    public bool IsSandbox => true;

    public Task<HsaConnectionStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var status = new HsaConnectionStatus(
            IsSandbox: true,
            IsReachable: true,
            Description: "Demo — ej skarp HSA-anslutning. Genererar deterministiska demo-HSA-id lokalt. "
                       + "Skarp koppling kräver Inera-avtal, SITHS-funktionscertifikat och HSA WS/LDAP-endpoint.",
            CheckedAt: DateTimeOffset.UtcNow);
        return Task.FromResult(status);
    }

    public Task<HsaUnit?> SlaUppEnhetAsync(string sokterm, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sokterm))
            return Task.FromResult<HsaUnit?>(null);

        var namn = sokterm.Trim();
        var unit = new HsaUnit(
            HsaId: GeneraDemoHsaId("E", namn),
            Namn: namn,
            OverordnadHsaId: null,
            Kostnadsstalle: null,
            Ort: "Örebro",
            Kind: HsaUnitKind.Vardenhet);
        return Task.FromResult<HsaUnit?>(unit);
    }

    public Task<HsaPerson?> SlaUppPersonAsync(string sokterm, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sokterm))
            return Task.FromResult<HsaPerson?>(null);

        var person = new HsaPerson(
            HsaId: GeneraDemoHsaId("P", sokterm.Trim()),
            Fornamn: string.Empty,
            Efternamn: string.Empty,
            Titel: null,
            EnhetHsaId: null);
        return Task.FromResult<HsaPerson?>(person);
    }

    public Task<IReadOnlyList<HsaUnit>> HamtaOrganisationstradAsync(CancellationToken ct = default)
    {
        // Fiktivt demo-träd som liknar en regions vårdorganisation.
        var region = new HsaUnit(GeneraDemoHsaId("E", "Region Örebro län"), "Region Örebro län", null, "10", "Örebro", HsaUnitKind.Organisation);
        var uso = new HsaUnit(GeneraDemoHsaId("E", "Universitetssjukhuset Örebro"), "Universitetssjukhuset Örebro", region.HsaId, "2010", "Örebro", HsaUnitKind.Organisation);
        var vc = new HsaUnit(GeneraDemoHsaId("E", "Vårdcentralen Lindesberg"), "Vårdcentralen Lindesberg", region.HsaId, "3020", "Lindesberg", HsaUnitKind.Vardenhet);
        var akut = new HsaUnit(GeneraDemoHsaId("E", "Akutmottagningen USÖ"), "Akutmottagningen USÖ", uso.HsaId, "2011", "Örebro", HsaUnitKind.Vardenhet);

        IReadOnlyList<HsaUnit> tree = [region, uso, vc, akut];
        return Task.FromResult(tree);
    }

    /// <summary>
    /// Genererar ett deterministiskt demo-HSA-id från en nyckel (FNV-1a → hex).
    /// Samma indata ger alltid samma id — stabilt över körningar, till skillnad från GetHashCode.
    /// </summary>
    internal static string GeneraDemoHsaId(string typPrefix, string nyckel)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var c in nyckel)
        {
            hash ^= c;
            hash *= prime;
        }

        return $"{DemoHsaRoot}-{typPrefix}{hash:X8}";
    }
}
