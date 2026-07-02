using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace RegionHR.Web.Services.Oidc;

/// <summary>
/// Den identitet OpenHR läser ut ur en federerad Entra-inloggning, redan översatt till
/// systemets egna begrepp. <see cref="Role"/> är alltid en giltig OpenHR-roll.
/// </summary>
/// <param name="DisplayName">Visningsnamn (från "name"-claim, annars e-post, annars fallback).</param>
/// <param name="Email">E-post/UPN — används för att koppla mot <c>Employee.Epost</c>. Kan vara null.</param>
/// <param name="ObjectId">Entra-objektid (oid) — stabil användaridentifierare. Kan vara null.</param>
/// <param name="Role">Mappad OpenHR-roll: "Admin", "HR", "Chef" eller "Anställd".</param>
/// <param name="Groups">Råa gruppmedlemskap ur token (för spårbarhet/loggning).</param>
/// <param name="AppRoles">Råa app-roller ur token (för spårbarhet/loggning).</param>
public sealed record EntraIdentity(
    string DisplayName,
    string? Email,
    string? ObjectId,
    string Role,
    IReadOnlyList<string> Groups,
    IReadOnlyList<string> AppRoles);

/// <summary>
/// Översätter en federerad Entra-<see cref="ClaimsPrincipal"/> till en <see cref="EntraIdentity"/>
/// med en giltig OpenHR-roll. Ren, sido­effektsfri och enhetstestbar — ingen DB, ingen HTTP.
///
/// <para><b>Rollmappning (dokumenterad):</b></para>
/// <list type="number">
///   <item>Samla kandidatroller: varje Entra-grupp slås upp i
///   <see cref="OidcOptions.GroupRoleMappings"/>, varje app-roll i
///   <see cref="OidcOptions.AppRoleMappings"/>. En app-roll vars värde <i>redan</i> är exakt en
///   OpenHR-roll ("Admin"/"HR"/"Chef"/"Anställd") godtas direkt.</item>
///   <item>Filtrera bort allt som inte är en giltig OpenHR-roll.</item>
///   <item>Vinnaren är den <b>högst privilegierade</b> kandidaten
///   (Admin &gt; HR &gt; Chef &gt; Anställd) — så en användare i både HR- och Chef-gruppen blir HR.</item>
///   <item>Finns ingen kandidat ⇒ <see cref="OidcOptions.DefaultRole"/> (validerad, annars "Anställd").
///   Minsta privilegium: en okänd federerad användare får aldrig mer än självservice.</item>
/// </list>
/// </summary>
public sealed class EntraClaimsMapper
{
    // Fallande privilegieordning. Index 0 = mest privilegierad.
    private static readonly string[] RolePrecedence = { "Admin", "HR", "Chef", "Anställd" };

    private readonly OidcOptions _options;

    // Skiftlägesokänsliga kopior av mappningarna. Byggs om här eftersom konfigurationsbindningen
    // (Configure<OidcOptions>) ersätter ordböckerna med skiftläges-KÄNSLIGa instanser — vi vill att
    // grupp-/rollnycklar matchar oavsett skiftläge (Entra grupp-id:n/namn varierar).
    private readonly Dictionary<string, string> _groupRoleMappings;
    private readonly Dictionary<string, string> _appRoleMappings;

    public EntraClaimsMapper(IOptions<OidcOptions> options)
    {
        _options = options.Value;
        _groupRoleMappings = BuildCaseInsensitive(_options.GroupRoleMappings);
        _appRoleMappings = BuildCaseInsensitive(_options.AppRoleMappings);
    }

    private static Dictionary<string, string> BuildCaseInsensitive(Dictionary<string, string>? source)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (source is null) return result;
        foreach (var (key, value) in source)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                result[key.Trim()] = value;
        }
        return result;
    }

    /// <summary>
    /// Bestämmer OpenHR-rollen för en uppsättning grupper + app-roller enligt den dokumenterade
    /// mappningen ovan. Returnerar alltid en giltig roll (fallback = default/"Anställd").
    /// </summary>
    public string MapRole(IEnumerable<string> groups, IEnumerable<string> appRoles)
    {
        var candidates = new List<string>();

        foreach (var group in groups)
        {
            if (!string.IsNullOrWhiteSpace(group)
                && _groupRoleMappings.TryGetValue(group.Trim(), out var mapped))
            {
                candidates.Add(mapped);
            }
        }

        foreach (var appRole in appRoles)
        {
            if (string.IsNullOrWhiteSpace(appRole)) continue;
            var value = appRole.Trim();

            if (_appRoleMappings.TryGetValue(value, out var mapped))
                candidates.Add(mapped);
            else if (IsValidRole(value, out var canonical))
                // App-rollens värde är redan en OpenHR-roll (ev. med annan skiftläge).
                candidates.Add(canonical);
        }

        var best = HighestPrivilege(candidates);
        if (best is not null)
            return best;

        // Ingen matchning: fall tillbaka på (validerad) default.
        return IsValidRole(_options.DefaultRole, out var def) ? def : "Anställd";
    }

    /// <summary>
    /// Läser ut en <see cref="EntraIdentity"/> ur en federerad principal. Robust mot att claim-typer
    /// varierar mellan tenants (faller tillbaka på vanliga standard-claim-typer).
    /// </summary>
    public EntraIdentity Map(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var groups = CollectValues(principal, _options.GroupsClaimType);

        // App-roller kan ligga både i konfigurerad claim och i standard-rollclaim.
        var appRoles = CollectValues(principal, _options.RolesClaimType);
        appRoles.AddRange(CollectValues(principal, ClaimTypes.Role));
        appRoles = appRoles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var email = FirstNonEmpty(principal,
            _options.EmailClaimType, ClaimTypes.Upn, ClaimTypes.Email, "email", "upn", "preferred_username");

        var name = FirstNonEmpty(principal,
            _options.NameClaimType, "name", ClaimTypes.Name, ClaimTypes.GivenName)
            ?? email
            ?? "Entra-användare";

        var objectId = FirstNonEmpty(principal,
            "oid", "http://schemas.microsoft.com/identity/claims/objectidentifier", ClaimTypes.NameIdentifier, "sub");

        var role = MapRole(groups, appRoles);

        return new EntraIdentity(name, email, objectId, role, groups, appRoles);
    }

    private static List<string> CollectValues(ClaimsPrincipal principal, string claimType)
        => principal.FindAll(claimType)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

    private static string? FirstNonEmpty(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var type in claimTypes)
        {
            var value = principal.FindFirst(type)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    private static string? HighestPrivilege(IEnumerable<string> candidates)
    {
        string? best = null;
        var bestRank = int.MaxValue;
        foreach (var candidate in candidates)
        {
            if (!IsValidRole(candidate, out var canonical)) continue;
            var rank = Array.IndexOf(RolePrecedence, canonical);
            if (rank >= 0 && rank < bestRank)
            {
                bestRank = rank;
                best = canonical;
            }
        }
        return best;
    }

    /// <summary>
    /// Är <paramref name="value"/> en giltig OpenHR-roll (skiftlägesokänsligt)? Ger tillbaka den
    /// kanoniska stavningen ("HR", "Anställd", …) i <paramref name="canonical"/>.
    /// </summary>
    private static bool IsValidRole(string? value, out string canonical)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            foreach (var role in RolePrecedence)
            {
                if (string.Equals(role, value.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    canonical = role;
                    return true;
                }
            }
        }
        canonical = "Anställd";
        return false;
    }
}
