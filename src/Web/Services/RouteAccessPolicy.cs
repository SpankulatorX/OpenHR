namespace RegionHR.Web.Services;

/// <summary>
/// Central single-source-of-truth för rutt → tillåtna roller. Låter oss URL-skydda
/// alla ~190 sidor utan att röra varje sidfil: <see cref="RegionHR.Web.Components.Layout.AdminLayout"/>
/// slår upp aktuell path här vid varje navigation och renderar en behörighetspanel
/// i stället för sidans innehåll om rollen saknas.
///
/// Policyn speglar den rollstyrning som <c>NavMenu.razor</c> redan gör kosmetiskt
/// (IsChef/IsHR) så att direkt-URL beter sig som menyn: en Anställd når aldrig
/// lön, audit, GDPR eller admin; en Chef når team/schema/rapporter men inte lön.
/// Roller i systemet: "Admin", "HR", "Chef", "Anställd".
///
/// Policyn är fail-closed: en rutt som inte matchar någon regel nekas för ALLA
/// roller. Varje ny sida måste därför registreras i <see cref="Rules"/> (eller
/// specialfallen i <see cref="AllowedRolesFor"/>) för att bli nåbar.
/// </summary>
public static class RouteAccessPolicy
{
    // Rollknippen (superset-relation: Admin ⊇ HR ⊇ Chef ⊇ Anställd i behörighet).
    public const string Admin = "Admin";
    public const string HR = "HR";
    public const string Chef = "Chef";
    public const string Anstalld = "Anställd";

    private static readonly string[] HrAdmin = { HR, Admin };
    private static readonly string[] ChefHrAdmin = { Chef, HR, Admin };
    private static readonly string[] AllaInloggade = { Anstalld, Chef, HR, Admin };
    private static readonly string[] Ingen = Array.Empty<string>();

    /// <summary>
    /// Öppna rutter (även oinloggade). Endast dessa nås utan session.
    /// <c>/auth</c> = federerad inloggnings-landning (OIDC-completion sker innan sessionen
    /// finns; sidan ligger dessutom på EmptyLayout utanför behörighetsvakten).
    /// <c>/utbildning/extern</c> = publik utbildningsportal för externa deltagare där en
    /// personlig token är bäraren (ingen inloggning) — policyn speglar det så att den
    /// förblir en sann single-source-of-truth.
    /// </summary>
    private static readonly string[] OpenRoutes = { "/login", "/trust", "/error", "/auth", "/utbildning/extern" };

    /// <summary>
    /// Prefix-regler. En regel matchar en path om path == prefix eller path börjar
    /// med prefix + "/" (segment-gräns, så "/lon" INTE matchar "/loneoversyn").
    /// Listan sorteras internt efter fallande prefixlängd → mest specifik regel vinner.
    /// </summary>
    private static readonly (string Prefix, string[] Roles)[] Rules = BuildRules();

    private static (string Prefix, string[] Roles)[] BuildRules()
    {
        var rules = new List<(string, string[])>
        {
            // ── Lön & ersättning (HR/Admin — lönekänsligt) ──
            ("/lon", HrAdmin),
            ("/loneoversyn", HrAdmin),

            // ── Granskning / GDPR / all administration (HR/Admin) ──
            ("/audit", HrAdmin),
            ("/gdpr", HrAdmin),
            ("/admin", HrAdmin),
            ("/integrationer", HrAdmin),
            ("/offboarding", HrAdmin),
            ("/helpdesk/agent", HrAdmin),
            ("/las", HrAdmin),
            ("/anpassat", HrAdmin),

            // ── Chefsfunktioner (Chef/HR/Admin) ──
            ("/chef", ChefHrAdmin),
            ("/godkannanden", ChefHrAdmin),
            ("/schema", ChefHrAdmin),
            ("/stampling", ChefHrAdmin),
            ("/tidrapporter", ChefHrAdmin),
            ("/arenden", ChefHrAdmin),
            ("/anstallda", ChefHrAdmin),
            ("/organisation", ChefHrAdmin),
            ("/positioner", ChefHrAdmin),
            ("/kompetens", ChefHrAdmin),
            ("/halsosam", ChefHrAdmin),
            ("/arbetsmiljo", ChefHrAdmin),
            ("/dokument/earkiv", HrAdmin),
            ("/dokument", ChefHrAdmin),
            ("/medarbetarsamtal", ChefHrAdmin),
            ("/journeys", ChefHrAdmin),
            ("/rekrytering", ChefHrAdmin),

            // Rapporter: operativa team-/enhetsrapporter är chef-nåbara (och enhets-scopade
            // i vyn). Men löne-/pekvitetsrapporter och lagstadgad SCB/SKR-statistik är
            // HR/Admin-domän — en chef ska aldrig nå organisationens löner via rapportvägen.
            ("/rapporter/lonetransparens", HrAdmin),
            ("/rapporter/lonekartering", HrAdmin),
            ("/rapporter/statistik", HrAdmin),
            ("/rapporter/scb", HrAdmin),
            ("/rapporter", ChefHrAdmin),
            ("/vms", ChefHrAdmin),

            // ── Självservice / alla inloggade ──
            ("/minsida", AllaInloggade),
            ("/ledighet", AllaInloggade),
            ("/notiser", AllaInloggade),
            ("/helpdesk", AllaInloggade),
            ("/karriar", AllaInloggade),
            ("/kompetens/endorsements", AllaInloggade),
            ("/formaner", AllaInloggade),
            ("/resor", AllaInloggade),
            // Utbildning är självservice för alla — MEN administrationen av externa
            // deltagare (access-tokens) är en kursadministrativ HR/Admin-funktion, inte
            // "personligt". (Den publika token-portalen /utbildning/extern är öppen ovan.)
            ("/utbildning/externa", HrAdmin),
            ("/utbildning", AllaInloggade),
        };
        // Mest specifik (längsta) prefix först.
        return rules.OrderByDescending(r => r.Item1.Length).ToArray();
    }

    /// <summary>
    /// Är rutten öppen för alla, även oinloggade?
    /// </summary>
    public static bool IsOpen(string path)
    {
        var p = Normalize(path);
        return OpenRoutes.Contains(p) || OpenRoutes.Any(o => p.StartsWith(o + "/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Får en användare med rollen <paramref name="role"/> nå <paramref name="path"/>?
    /// role == null betyder oinloggad → endast öppna rutter tillåts.
    /// </summary>
    public static bool IsAllowed(string path, string? role)
    {
        var p = Normalize(path);

        // Öppna rutter: alltid.
        if (OpenRoutes.Contains(p) || OpenRoutes.Any(o => p.StartsWith(o + "/", StringComparison.Ordinal)))
            return true;

        // Oinloggad når inget annat än öppna rutter.
        if (string.IsNullOrEmpty(role))
            return false;

        var allowed = AllowedRolesFor(p);
        return allowed.Contains(role);
    }

    /// <summary>
    /// Tillåtna roller för en path. Exponeras för test och felmeddelanden.
    /// </summary>
    public static IReadOnlyCollection<string> AllowedRolesFor(string path)
    {
        var p = Normalize(path);

        // Dashboard.
        if (p == "/")
            return AllaInloggade;

        // Känsliga person-/löne-underåtgärder som NavMenu inte exponerar för Chef:
        // skapa anställd, ändra anställning, lönehistorik → HR/Admin.
        if (p == "/anstallda/ny"
            || (p.StartsWith("/anstallda/", StringComparison.Ordinal)
                && (p.EndsWith("/anstallning", StringComparison.Ordinal)
                    || p.EndsWith("/lonehistorik", StringComparison.Ordinal))))
        {
            return HrAdmin;
        }

        foreach (var (prefix, roles) in Rules)
        {
            if (p == prefix || p.StartsWith(prefix + "/", StringComparison.Ordinal))
                return roles;
        }

        // Fail-closed: okänd rutt NEKAS för alla tills en regel uttryckligen
        // tillåter den. En ny sida måste alltså registreras i Rules ovan för
        // att bli nåbar — hellre ett synligt "saknar behörighet" än att en
        // oskyddad rutt läcker till fel roll.
        return Ingen;
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        var p = path;

        // Strippa ev. absolut URL / base — behåll bara path-delen.
        var schemeIdx = p.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx >= 0)
        {
            var slash = p.IndexOf('/', schemeIdx + 3);
            p = slash >= 0 ? p[slash..] : "/";
        }

        // Strippa query och fragment.
        var q = p.IndexOfAny(new[] { '?', '#' });
        if (q >= 0) p = p[..q];

        if (p.Length == 0) return "/";
        if (p[0] != '/') p = "/" + p;

        // Ta bort avslutande slash (utom rot).
        if (p.Length > 1 && p[^1] == '/') p = p.TrimEnd('/');
        if (p.Length == 0) p = "/";

        return p.ToLowerInvariant();
    }
}
