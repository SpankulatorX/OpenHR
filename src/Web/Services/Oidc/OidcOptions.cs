namespace RegionHR.Web.Services.Oidc;

/// <summary>
/// Konfiguration för federerad inloggning mot Microsoft Entra ID (OpenID Connect).
///
/// <para>
/// STATUS: Config-ready men <b>avstängd som standard</b>. Så länge
/// <see cref="Enabled"/> är <c>false</c> (eller <see cref="TenantId"/>/<see cref="ClientId"/>
/// saknas) kör OpenHR sin vanliga demo-inloggning (BankID/SITHS-simulering speglad till
/// ClaimsPrincipal) helt oförändrad. Regionen aktiverar riktig Entra-federation genom att
/// fylla i denna sektion i <c>appsettings.json</c> (eller via miljövariabler /
/// user-secrets) — ingen kod behöver ändras.
/// </para>
///
/// <para>
/// Riktig BankID kräver ett betalt avtal och byggs inte här. Entra ID / Microsoft-konto
/// federeras däremot via gratis-paketet <c>Microsoft.Identity.Web</c>.
/// </para>
/// </summary>
public sealed class OidcOptions
{
    /// <summary>Namnet på konfigurationssektionen (<c>appsettings.json</c> → "Oidc").</summary>
    public const string SectionName = "Oidc";

    /// <summary>
    /// Huvudbrytare. <c>false</c> (default) ⇒ demo-inloggning som förut, "Logga in med
    /// organisationskonto"-knappen visas inte. <c>true</c> + ifylld tenant ⇒ Entra-federation.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Entra-instans. Publik moln-standard; ändras endast för sovereign clouds.</summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>Directory (tenant) ID för regionens Entra-katalog. Tomt ⇒ ej konfigurerad.</summary>
    public string TenantId { get; set; } = "";

    /// <summary>Application (client) ID för OpenHR-appregistreringen. Tomt ⇒ ej konfigurerad.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// Client secret. Krävs för confidential-client-flödet (auth code). Kan lämnas tomt om
    /// regionen använder certifikat eller managed identity — hanteras då i infrastrukturen.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>Callback-path som Entra postar auktoriseringssvaret till. Måste matcha appregistreringen.</summary>
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>Callback-path efter utloggning.</summary>
    public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc";

    // ── Claim-typer (justerbara per tenant-utformning) ──

    /// <summary>Claim som bär användarens visningsnamn.</summary>
    public string NameClaimType { get; set; } = "name";

    /// <summary>Claim som bär användarens e-post / UPN (för koppling mot Employee.Epost).</summary>
    public string EmailClaimType { get; set; } = "preferred_username";

    /// <summary>Claim som bär gruppmedlemskap (Entra-grupp-ID:n eller namn).</summary>
    public string GroupsClaimType { get; set; } = "groups";

    /// <summary>Claim som bär app-roller (från appregistreringens "App roles").</summary>
    public string RolesClaimType { get; set; } = "roles";

    /// <summary>
    /// Valfri claim som bär en OpenHR-enhets-GUID direkt (t.ex. en Entra-extension-attribut).
    /// Används bara om Employee-matchningen inte kunde lösa enhet. Tom ⇒ hoppas över.
    /// </summary>
    public string? UnitClaimType { get; set; }

    /// <summary>
    /// Mappning Entra-grupp (ID eller namn) → OpenHR-roll. Nyckel = gruppens objektid eller
    /// visningsnamn (skiftlägesokänsligt); värde = en av "Admin", "HR", "Chef", "Anställd".
    /// </summary>
    public Dictionary<string, string> GroupRoleMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Mappning Entra-approll → OpenHR-roll. Nyckel = approllens värde; värde = OpenHR-roll.
    /// (En approll vars värde redan är exakt en OpenHR-roll behöver ingen post — den godtas direkt.)
    /// </summary>
    public Dictionary<string, string> AppRoleMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Roll som tilldelas en federerad användare vars grupper/approller inte matchar någon
    /// mappning. Default "Anställd" — minsta privilegium, aldrig mer än självservice.
    /// </summary>
    public string DefaultRole { get; set; } = "Anställd";

    /// <summary>Fullständig authority-URL (Instance + TenantId) för Microsoft.Identity.Web.</summary>
    public string Authority => $"{Instance.TrimEnd('/')}/{TenantId}";

    /// <summary>
    /// Är federationen faktiskt påslagen OCH minimalt ifylld? Endast då aktiveras Entra-knappen
    /// och Program.cs kopplar in <c>AddMicrosoftIdentityWebApp</c>. Annars kör demo-login.
    /// </summary>
    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(TenantId)
        && !string.IsNullOrWhiteSpace(ClientId);
}
