using MudBlazor.Services;
using Microsoft.AspNetCore.Localization;
using RegionHR.Infrastructure;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Web.Health;
using RegionHR.Web.Hubs;
using RegionHR.Web.Middleware;
using RegionHR.Web.Services;
using RegionHR.Web.Components;

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
var useInMemory = false;
try
{
    using var testConn = new Npgsql.NpgsqlConnection(connectionString);
    testConn.Open();
    testConn.Close();
}
catch
{
    useInMemory = true;
    Console.WriteLine("PostgreSQL unavailable — using InMemory database with seed data.");
}
builder.Services.AddInfrastructure(connectionString, useInMemory);

// SignalR
builder.Services.AddSignalR();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgresql");

// Application services
builder.Services.AddScoped<AnstallningService>();
builder.Services.AddScoped<ArendeService>();
builder.Services.AddScoped<SelfServiceApiClient>();
builder.Services.AddScoped<UserRoleService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<StamplingService>();
builder.Services.AddScoped<FlexService>();

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

app.MapHealthChecks("/health");

app.Run();
