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
builder.Services.AddScoped<UnitScopeService>();
builder.Services.AddScoped<StamplingService>();
builder.Services.AddScoped<FlexService>();
builder.Services.AddScoped<RegionHR.Web.Services.RekryteringService>();
builder.Services.AddScoped<LedighetService>();

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

// Seed database (auto-migrate + seed on startup)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<RegionHRDbContext>();
        await db.Database.EnsureCreatedAsync();
        await SeedData.SeedAsync(db);
    }
    catch (Exception ex) { var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>(); logger.LogError(ex, "Database seed failed"); }
}

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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<NotificationHub>("/hubs/notifications");

// Liveness: processen lever (kör inga checks → 200 så länge appen svarar).
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
// Readiness: DB nås + kärnschema finns (InMemory rapporteras som ohälsosam).
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();
