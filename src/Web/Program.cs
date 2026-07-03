using MudBlazor.Services;
using Microsoft.AspNetCore.Localization;
using RegionHR.Infrastructure;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Infrastructure.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using RegionHR.Web.Hubs;
using RegionHR.Web.Middleware;
using RegionHR.Web.Services;
using RegionHR.Web.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;

// Npgsql 6+ mappar timestamptz strikt till UTC. Domänen sätter på många ställen
// DateTime med Kind=Unspecified (utvecklat mot InMemory som saknar denna kontroll).
// Legacy-läget accepterar Unspecified/Local för timestamptz — annars kastar varje
// sådan write (seed OCH användarskapade poster) i drift. Måste sättas före första
// Npgsql-datakälla byggs.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logging
builder.Logging.AddJsonConsole(options =>
{
    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

// Autentisering/behörighet (demo-inloggning speglad till ClaimsPrincipal).
// OpenHrAuthStateProvider speglar AuthService → auth-infrastrukturen fungerar
// (CascadingAuthenticationState / AuthorizeRouteView / AuthorizeView / [Authorize]).
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, OpenHrAuthStateProvider>();

// Localization (i18n)
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { "sv", "sv-SE", "en" };
    options.SetDefaultCulture("sv");
    options.AddSupportedCultures(supportedCultures);
    options.AddSupportedUICultures(supportedCultures);
});

// Swedish culture for datepickers
var svCulture = new System.Globalization.CultureInfo("sv-SE");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = svCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = svCulture;

// Infrastructure (EF Core, repositories, module contracts)
// Try PostgreSQL first; if unavailable, fall back to InMemory for development
var connectionString = builder.Configuration.GetConnectionString("RegionHR")
    ?? "Host=localhost;Port=54322;Database=postgres;Username=postgres;Password=postgres";
// Startup-vakt: endast Development får falla tillbaka på InMemory. Alla andra
// miljöer hård-failar hellre än att tyst servera en tom InMemory-databas
// (lönedata får aldrig hamna i flyktigt minne).
var useInMemory = false;
if (!StartupDatabaseGuard.CanReachPostgres(connectionString, out var dbError))
{
    if (StartupDatabaseGuard.AllowInMemoryFallback(builder.Environment.EnvironmentName))
    {
        useInMemory = true;
        Console.Error.WriteLine("============================================================");
        Console.Error.WriteLine("  VARNING: PostgreSQL är otillgänglig.");
        Console.Error.WriteLine("  Startar med InMemory-databas (endast Development).");
        Console.Error.WriteLine("  INGEN data persisteras — allt försvinner vid omstart.");
        Console.Error.WriteLine("============================================================");
    }
    else
    {
        var fatal = StartupDatabaseGuard.BuildFatalNoDatabaseException(
            builder.Environment.EnvironmentName, connectionString, dbError);
        Console.Error.WriteLine(fatal.Message);
        throw fatal;
    }
}
builder.Services.AddInfrastructure(connectionString, useInMemory);

// SignalR
builder.Services.AddSignalR();

// Health checks — DB-anslutning + kärnschema. Taggen "ready" gatar readiness.
string[] readinessTags = { "ready", "db" };
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: readinessTags);

// Application services
builder.Services.AddScoped<AnstallningService>();
builder.Services.AddScoped<ArendeService>();
builder.Services.AddScoped<SelfServiceApiClient>();
builder.Services.AddScoped<UserRoleService>();
builder.Services.AddScoped<AuthService>();
// Granskningslogg: AuditInterceptor (scoped, se Infrastructure.DependencyInjection)
// stämplar poster med den faktiska användaren via ICurrentUser —
// HttpContext-claims vid OIDC, AuthService-fallback vid demo-login.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddScoped<UnitScopeService>();
builder.Services.AddScoped<StamplingService>();
builder.Services.AddScoped<FlexService>();
builder.Services.AddScoped<RegionHR.Web.Services.RekryteringService>();
builder.Services.AddScoped<LedighetService>();
builder.Services.AddScoped<LokalAvvikelseService>();
// Heroma-import (våg 7): parsad data → riktiga anställda
builder.Services.AddScoped<RegionHR.Migration.Services.IEmployeeImportSink, RegionHR.Web.Services.EmployeeImportSink>();
builder.Services.AddScoped<RegionHR.Migration.Services.MigrationImportService>();
// OIDC / Entra ID (config-ready — default avstängd, se appsettings "Oidc") — våg 7
builder.Services.AddScoped<RegionHR.Web.Services.Oidc.EntraClaimsMapper>();
builder.Services.AddScoped<RegionHR.Web.Services.Oidc.OidcAccountLinker>();

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

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(
        context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 10,
            }));
    options.RejectionStatusCode = 429;
});

var app = builder.Build();

// Skapa schema + (valfritt) demo-seed vid uppstart.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<RegionHRDbContext>();
        // OBS: EnsureCreatedAsync skapar schemat från NUVARANDE modell på en TOM databas
        // men uppdaterar ALDRIG en befintlig — efter modelländringar måste demo-DB:n
        // (pgdata-volymen) återskapas vid deploy. Migrations-mappen släpar efter
        // modellen, så Database.Migrate() är inte säkert att byta till förrän en
        // ny migration genererats (dotnet ef migrations add) och verifierats.
        await db.Database.EnsureCreatedAsync();

        // Demo-seed-vakt: en produktionsdatabas får aldrig fyllas med demodata av
        // misstag. Sätt SeedDemoData=true (env/appsettings) i demo-/utvecklingsmiljöer
        // — docker-compose.yml gör det för demon.
        if (app.Configuration.GetValue<bool>("SeedDemoData"))
        {
            await SeedData.SeedAsync(db);
        }
    }
    catch (Exception ex) { var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>(); logger.LogError(ex, "Database seed failed"); }
}

// Bakom cloudflared/reverse proxy: skriv om Connection.RemoteIpAddress från
// X-Forwarded-For INNAN rate-limitern (och loggningen) läser den — annars hamnar
// alla besökare i proxyns/tunnelns partition (en gemensam bucket → godtycklig 429).
// Betrodda proxyer = loopback (default) + privata nät (dockerbroar/cloudflared);
// klienter ute på internet kan inte spoofa XFF den vägen.
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8));
forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12));
forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("192.168.0.0"), 16));
app.UseForwardedHeaders(forwardedOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<SessionTimeoutMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseStaticFiles();
app.UseRateLimiter();
app.UseAntiforgery();
app.UseRequestLocalization();

if (oidcEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<NotificationHub>("/hubs/notifications");

if (oidcEnabled)
{
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

// Liveness: processen lever (kör inga checks → 200 så länge appen svarar).
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
// Readiness: DB nås + kärnschema finns (InMemory rapporteras som ohälsosam).
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();
