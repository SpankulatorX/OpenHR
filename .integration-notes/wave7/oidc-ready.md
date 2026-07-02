# Wave 7 — Slice `oidc-ready`: Entra ID / OIDC-inloggning (config-ready)

## Sammanfattning
Federerad inloggning mot **Microsoft Entra ID** via gratis-paketet `Microsoft.Identity.Web`,
byggd **config-klar och AVSTÄNGD som standard**. Så länge `Oidc:Enabled=false` (default, i
`appsettings.json`) — eller `TenantId`/`ClientId` saknas — kör OpenHR sin vanliga demo-inloggning
(BankID/SITHS-simulering speglad till ClaimsPrincipal) **helt oförändrad**. Fyller regionen i
sektionen och sätter `Enabled=true` aktiveras knappen "Logga in med organisationskonto" på
`/login`, som federerar mot Entra och speglar in identiteten i den befintliga `AuthService` →
`OpenHrAuthStateProvider`.

**Riktig BankID är INTE byggt** (kräver betalt avtal) — endast Entra/OIDC, som är gratis via
`Microsoft.Identity.Web`.

## Nya filer
- `src/Web/Services/Oidc/OidcOptions.cs` — konfig-POCO + `IsConfigured`-toggle + `Authority`.
- `src/Web/Services/Oidc/EntraClaimsMapper.cs` — **claim → OpenHR-roll**-mappning (ren/testbar).
- `src/Web/Services/Oidc/OidcAccountLinker.cs` — kopplar Entra-identitet → Employee/enhet via DB.
- `src/Web/Components/Pages/Auth/OidcComplete.razor` — completion-sida efter Entra-callback.
- Tester: `tests/Web.Tests/Oidc/EntraClaimsMapperTests.cs`, `OidcAccountLinkerTests.cs`, `OidcOptionsTests.cs`.

## Ändrade filer (inom slice)
- `src/Web/Components/Pages/Auth/Login.razor` — lade till Entra-knappen i "Välj metod"-steget,
  visas **endast** när `Oidc.Value.IsConfigured`. Resten av Login orörd. Demo-login intakt.
- `src/Web/appsettings.json` — ny `"Oidc"`-sektion, `Enabled=false` (avstängd default).

## Rollmappning (Entra → OpenHR) — DOKUMENTERAD
Mappas av `EntraClaimsMapper`. OpenHR-roller: **Admin > HR > Chef > Anställd** (fallande privilegium).
1. Varje Entra-**grupp** slås upp i `Oidc:GroupRoleMappings` (nyckel = gruppens objekt-id ELLER
   visningsnamn, skiftlägesokänsligt; värde = OpenHR-roll).
2. Varje **app-roll** slås upp i `Oidc:AppRoleMappings`. En app-roll vars värde *redan* är exakt en
   OpenHR-roll ("Admin"/"HR"/"Chef"/"Anställd") godtas direkt utan mappningspost.
3. Vinnaren = den **högst privilegierade** matchande rollen (så HR+Chef → HR; Admin+HR → Admin).
4. Ingen matchning ⇒ `Oidc:DefaultRole` (default **"Anställd"** = minsta privilegium; ogiltigt
   värde faller tillbaka på "Anställd").

Namn/e-post/oid läses ur `name` / `preferred_username` / `oid` (claim-typerna är konfigurerbara).
Enhet (`unit_id`) löses primärt genom att matcha Employee via **e-post** (annars för+efternamn) och
ta dennes aktiva anställnings enhet — exakt som demo-login. En valfri `Oidc:UnitClaimType` kan bära
en enhets-GUID som fallback.

Exempel-`appsettings`-mappning (regionen fyller i sina egna grupp-id:n):
```json
"Oidc": {
  "Enabled": true,
  "TenantId": "<tenant-guid>",
  "ClientId": "<app-guid>",
  "ClientSecret": "<secret>",
  "GroupRoleMappings": {
    "<admin-grupp-guid>": "Admin",
    "<hr-grupp-guid>": "HR",
    "<chef-grupp-guid>": "Chef"
  },
  "AppRoleMappings": { "OpenHR.Payroll.Admin": "HR" }
}
```

---

## INTEGRATION — snuttar till förbjudna filer

### package_refs
`Directory.Packages.props` (ny rad under `<!-- Auth -->`):
```xml
<PackageVersion Include="Microsoft.Identity.Web" Version="3.15.1" />
```
`src/Web/RegionHR.Web.csproj` (ny rad i första `<ItemGroup>`):
```xml
<PackageReference Include="Microsoft.Identity.Web" />
```
> `3.15.1` är mogna 3.x-linjen med explicit `net9.0`-target och exakt den `AddMicrosoftIdentityWebApp`-API
> som snutten nedan använder. Aktuell major-linje `4.12.0` fungerar också (net9.0-target, samma API) om
> "senaste stabila" föredras. **Endast FOSS/gratis** (MIT).

### di_registrations
Lägg i `Program.cs` bland övriga `builder.Services.AddScoped<...>()` (kring rad 94–104):
```csharp
// OIDC / Entra ID (config-ready — default avstängd, se appsettings "Oidc").
builder.Services.Configure<RegionHR.Web.Services.Oidc.OidcOptions>(
    builder.Configuration.GetSection(RegionHR.Web.Services.Oidc.OidcOptions.SectionName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<RegionHR.Web.Services.Oidc.EntraClaimsMapper>();
builder.Services.AddScoped<RegionHR.Web.Services.Oidc.OidcAccountLinker>();
```

### program_cs_changes
1. Toppen (usings):
```csharp
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
```
2. **Villkorad Entra-federation** — lägg direkt EFTER di_registrations ovan (efter att
   `AddScoped<AuthenticationStateProvider, OpenHrAuthStateProvider>` redan körts; rör den INTE):
```csharp
// Koppla in Entra ENDAST om sektionen är påslagen + minimalt ifylld. Annars: demo-login som förut.
var oidcSection = builder.Configuration.GetSection(RegionHR.Web.Services.Oidc.OidcOptions.SectionName);
var oidcEnabled = oidcSection.GetValue<bool>("Enabled")
    && !string.IsNullOrWhiteSpace(oidcSection["TenantId"])
    && !string.IsNullOrWhiteSpace(oidcSection["ClientId"]);
if (oidcEnabled)
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration, RegionHR.Web.Services.Oidc.OidcOptions.SectionName);
}
```
> `AddMicrosoftIdentityWebApp` läser `Instance`/`TenantId`/`ClientId`/`ClientSecret`/`CallbackPath`/
> `SignedOutCallbackPath` direkt ur "Oidc"-sektionen (nycklarna matchar `OidcOptions`).
> Detta rör INTE `OpenHrAuthStateProvider` — Blazors AuthorizeView fortsätter använda den; Entra-cookien
> populerar bara `HttpContext.User` som completion-sidan läser av. Ingen konflikt.

3. **Middleware** — lägg efter `app.UseRequestLocalization();` och FÖRE `app.MapRazorComponents<App>()`:
```csharp
if (oidcEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
```
4. **Endpoints** — lägg efter `app.MapHub<NotificationHub>(...)` (endast när påslaget):
```csharp
if (oidcEnabled)
{
    // Startar OIDC-utmaningen; efter federationen landar användaren på completion-sidan.
    app.MapGet("/auth/oidc/challenge", (HttpContext http) =>
        Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/auth/oidc/complete" },
            new[] { OpenIdConnectDefaults.AuthenticationScheme })).AllowAnonymous();

    app.MapGet("/auth/oidc/signout", (HttpContext http) =>
        Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/login" },
            new[] { Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                    OpenIdConnectDefaults.AuthenticationScheme })).AllowAnonymous();
}
```

### route_policy
`RouteAccessPolicy.cs` (i min katalog men lämnad orörd för att undvika kollision med andra slices):
lägg `"/auth"` i `OpenRoutes` så pre-login-rundturen är öppen:
```csharp
private static readonly string[] OpenRoutes = { "/login", "/trust", "/error", "/auth" };
```
> **Ej funktionellt kritiskt**: `OidcComplete.razor` använder `EmptyLayout` och kringgår därmed
> `AdminLayout`:s rutt-vakt redan. Tillägget är korrekt/defensivt om vakten någon gång breddas.
> `/auth/*` är en ÖPPEN rutt (ingen roll krävs) — den nås innan AuthService-login finns.

### dbsets / nav_entries / signature_changes / seed_snippets
Inga. Inga nya entiteter, inga nav-poster, inga ändrade publika signaturer, ingen seed.

---

## Så fungerar flödet (när påslaget)
1. `/login` visar "Logga in med organisationskonto" (bara om `IsConfigured`).
2. Knappen full-page-navigerar till `/auth/oidc/challenge` → `Microsoft.Identity.Web` utmanar Entra.
3. Entra autentiserar, postar tillbaka till `/signin-oidc` (cookie sätts), redirect → `/auth/oidc/complete`.
4. `OidcComplete.razor` läser `HttpContext.User` under prerender, persisterar identiteten över
   prerender→interaktiv-gränsen (`PersistentComponentState`), och i den interaktiva kretsen (där
   ProtectedSessionStorage/JS-interop funkar) mappar roll (`EntraClaimsMapper`), löser Employee/enhet
   (`OidcAccountLinker`) och kallar `AuthService.LoginAsync(...)` — precis som demo-login. Navigerar till `/`.

## Demo-login: ORÖRD default
Utan konfiguration resolvar `IOptions<OidcOptions>` till default (`Enabled=false`) → knappen döljs,
inga Entra-scheman registreras, inga /auth-endpoints finns. Exakt samma beteende som före denna slice.
