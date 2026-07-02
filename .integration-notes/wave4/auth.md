# Wave 4 — Riktig behörighetskontroll (auth)

Branch: `fix-rfi-gaps`. Mål: göra demo-inloggningen till en riktig autentiserad session,
URL-skydda alla sidor per roll centralt (utan att röra ~190 sidfiler), och enhets-scopa
chefsvyerna. Bygg görs av integratören (ingen dotnet på denna maskin → verifierat manuellt).

## Skapade filer
- `src/Web/Services/OpenHrAuthStateProvider.cs` — custom `AuthenticationStateProvider`
  som speglar `AuthService` → `ClaimsPrincipal`. Claims: `Name`, `Role` (ClaimTypes.Role),
  `employee_id`, `unit_id` (+ kompat-claim `EmployeeId` som befintlig sida TotalRewards läser).
  Lyssnar på `AuthService.AuthChanged` och kör `NotifyAuthenticationStateChanged`. `IDisposable`.
- `src/Web/Services/RouteAccessPolicy.cs` — statisk single-source-of-truth: `IsAllowed(path, role)`,
  `IsOpen(path)`, `AllowedRolesFor(path)`. Prefix-regler med segmentgräns (så `/lon` ≠ `/loneoversyn`),
  mest specifik prefix vinner, plus special-regler för känsliga person-underåtgärder.
- `src/Web/Services/UnitScopeService.cs` — scoped tjänst: `IsUnitScoped` (Chef),
  `GetCurrentUnitIdAsync()`, `GetEmployeeIdsInScopeAsync()` (null = ingen scoping/visa alla),
  `CanViewEmployeeAsync(guid)`. Härleder enhet via EmployeeId → `AktivAnstallning(idag).EnhetId`.
- `src/Web/Components/Shared/AccessDenied.razor` — "🔒 Du saknar behörighet"-panel (återanvänds).
- `src/Web/Components/RedirectToLogin.razor` — sekundärt skyddsnät (navigerar till /login).
- `tests/Web.Tests/RouteAccessPolicyTests.cs` — 14 xUnit-testmetoder (theories) för rutt+roll.

## Ändrade filer
- `src/Web/Services/AuthService.cs` — NYA: `Guid? UnitId`, `event Action? AuthChanged`.
  `LoginAsync` fick 4:e valfria param `Guid? unitId = null` (bakåtkompatibelt). `InitializeAsync`/
  `LogoutAsync` läser/rensar `auth_unit_id` och kör `AuthChanged`. ALLA befintliga medlemmar
  (UserName/Role/EmployeeId/IsHR/IsChef/IsAdmin/HasRole/IsLoggedIn/IsInitialized/IsDarkMode) intakta.
- `src/Web/Program.cs` — se nedan.
- `src/Web/Components/Routes.razor` — se nedan.
- `src/Web/Components/Layout/AdminLayout.razor` — central enforcement (se nedan).
- `src/Web/Components/Pages/Auth/Login.razor` — slår upp `AktivAnstallning(idag).EnhetId.Value`
  (via `.Include(e => e.Anstallningar)`) och skickar `unitId` till `LoginAsync`. Demo-login intakt.
- Enhets-scoping i chefsvyerna: `Chef/Index.razor`, `Chef/Bemanning.razor`,
  `Chef/Franvarokalender.razor`, `Godkannanden/Index.razor` (injicerar `UnitScopeService`).
  `Chef/MittTeam.razor` var REDAN scopad (inline, samma logik) — lämnad orörd.

## Exakt vad som ändrades i Program.cs
Ny `using Microsoft.AspNetCore.Components.Authorization;`. Direkt efter `AddMudServices()`:
```csharp
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, OpenHrAuthStateProvider>();
```
I "Application services"-blocket, efter `AddScoped<AuthService>()`:
```csharp
builder.Services.AddScoped<UnitScopeService>();
```
Inget annat i Program.cs rördes (ingen JwtBearer-auth-middleware behövs för Blazor Server;
`app.UseAuthentication/UseAuthorization` lades medvetet INTE till — vi kör ingen endpoint-auth,
bara komponent-auth via AuthenticationStateProvider).

## Exakt vad som ändrades i Routes.razor
`RouteView` → `AuthorizeRouteView` (behåller `DefaultLayout="typeof(Layout.AdminLayout)"`), med
`<NotAuthorized>` (oinloggad → `<RedirectToLogin/>`, annars `<AccessDenied/>`) och `<Authorizing>`
(spinner). CascadingAuthenticationState behövs INTE i markup — den tillhandahålls av
`AddCascadingAuthenticationState()`. Lade `@using Microsoft.AspNetCore.Components.Authorization`.

## Central enforcement (AdminLayout.razor)
`@implements IDisposable` + `@using RegionHR.Web.Services`. Prenumererar på `Nav.LocationChanged`.
I den inloggade renderingsgrenen: `@if (IsCurrentRouteAllowed()) { @Body } else { <AccessDenied/> }`
— sidans innehåll (och därmed dess DB-inladdning) renderas ALDRIG vid rollmiss. Oinloggad →
`Nav.NavigateTo("/login")` (i OnAfterRender firstRender + LocationChanged). Dark mode/spinner intakt.

`IsCurrentRouteAllowed()` = `RouteAccessPolicy.IsAllowed("/" + Nav.ToBaseRelativePath(Nav.Uri), Auth.Role)`.

## Rutt → roll-policy (i klartext)
Roller: Admin ⊇ HR ⊇ Chef ⊇ Anställd (i behörighet). Policyn SPEGLAR NavMenu:s rollstyrning
(IsHR/IsChef) så att direkt-URL beter sig som menyn.

- ÖPPET (även oinloggad): `/login`, `/trust`, `/error`
- HR/Admin: `/lon/*`, `/loneoversyn`, `/audit`, `/gdpr`, `/admin/*`, `/integrationer/*`,
  `/offboarding/*`, `/helpdesk/agent`
- HR/Admin (känsliga person-/löneunderåtgärder, ej i NavMenu): `/anstallda/ny`,
  `/anstallda/{id}/anstallning`, `/anstallda/{id}/lonehistorik`
- Chef/HR/Admin: `/chef/*`, `/godkannanden`, `/schema/*`, `/stampling`, `/tidrapporter/*`,
  `/arenden/*`, `/anstallda` (+ övriga detaljer), `/organisation`, `/positioner`, `/kompetens`
  (utom endorsements), `/halsosam/*`, `/arbetsmiljo`, `/dokument/*`, `/medarbetarsamtal/*`,
  `/journeys/*`, `/rekrytering/*`, `/rapporter/*`, `/vms/*`
- Alla inloggade: `/` (dashboard), `/minsida/*`, `/ledighet*`, `/notiser*`, `/helpdesk`,
  `/karriar/*`, `/kompetens/endorsements`, `/formaner/*`, `/resor`, `/utbildning*`
- DEFAULT (okänd rutt): fail-safe = kräver minst inloggning (alla roller, ej oinloggad)

Krav uppfyllt: en Anställd når ALDRIG `/audit`, `/gdpr`, `/admin/*`, `/lon/*` (samt lönehistorik).

## Nya DI-registreringar
`AddAuthorizationCore`, `AddCascadingAuthenticationState`,
`AddScoped<AuthenticationStateProvider, OpenHrAuthStateProvider>`, `AddScoped<UnitScopeService>`.

## Org-scoping (chef ser bara sitt team)
`UnitScopeService.GetEmployeeIdsInScopeAsync()` → set av anställd-Guid på chefens aktiva enhet
(null för HR/Admin eller chef utan enhet → visa alla, oförändrat beteende). Filtrering sker
i minne efter materialisering (undviker EF-value-converter-översättning för `.Value`):
- `Chef/Index.razor`: pendingLeave (före Take 20), `_teamAntal`, `_franvaroIdag`.
- `Chef/Bemanning.razor`: dagens pass.
- `Chef/Franvarokalender.razor`: veckans frånvaro.
- `Godkannanden/Index.razor`: väntande ärenden (Chef → egen enhet; HR/Admin → alla; ändrad underrubrik).

## Designval / motivering
- Policyn speglar NavMenu (appens egen rollmodell) i stället för uppgiftens exempel-lista där
  de krockade (t.ex. NavMenu ger Chef `/rekrytering`, `/rapporter`, `/integrationer/hsa`).
  Skäl: revisionsfynd #4 säger att fixen = låta URL följa den rollstyrning menyn REDAN visar;
  att vara strängare än menyn skulle ge "åtkomst nekad" på synliga menylänkar. Anställd blockeras
  lika hårt oavsett tolkning. Icke-menylänkade känsliga underåtgärder (`/anstallda/ny`,
  `.../anstallning`, `.../lonehistorik`) hålls dock HR/Admin enligt uppgiften.
- Ingen global `[Authorize]` (t.ex. via _Imports): ProtectedSessionStorage kan inte läsas under
  prerender → en inloggad användare skulle felaktigt ses som anonym och omdirigeras. Därför är
  AdminLayout (efter interaktiv init) den PRIMÄRA porten; AuthorizeRouteView är plumbing +
  sekundärt nät (aktivt först om en sida markeras `[Authorize]`).

## Kvarvarande begränsningar / byggrisker
- Demo-auth: identitet härleds fortfarande via namnmatchning (ingen riktig e-legitimation) — oförändrat.
- Enhets-scoping är EN nivå (chefens egen enhet). Underenheter (OverordnadEnhetId) inkluderas EJ
  (undviker EF-översättningsproblem på strongly-typed nullable ID) — enligt "annars egen enhet".
- `/trust` (EmptyLayout, publik marknads-/trust-sida) och `/login` är avsiktligt öppna och
  omfattas inte av AdminLayout-porten (de använder inte AdminLayout).
- Byggrisk (kan ej kompilera lokalt, ingen dotnet): verifiera att
  `AddCascadingAuthenticationState()`/`AddAuthorizationCore()` finns via web-SDK:ns implicit
  `Microsoft.Extensions.DependencyInjection`-using (bör stämma i net9.0). Om TotalRewards tidigare
  saknade en registrerad `AuthenticationStateProvider` var den sidan trasig — nu fixad.
