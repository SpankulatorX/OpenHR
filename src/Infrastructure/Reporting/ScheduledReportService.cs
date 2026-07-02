using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Infrastructure.Export;
using RegionHR.Infrastructure.Notifications;
using RegionHR.Reporting.Domain;
using RegionHR.Reporting.Engine;

namespace RegionHR.Infrastructure.Reporting;

/// <summary>
/// Bakgrundstjänst som kör schemalagda rapporter. Kollar varje timme efter
/// rapportdefinitioner som är schemalagda (ArSchemalagd + CronExpression) och som
/// förfallit sedan sin senaste körning, exekverar dem mot databasen via
/// <see cref="ReportExecutionService"/>, exporterar resultatet, mejlar mottagaren och
/// registrerar en <see cref="ReportExecution"/>. Nästa körning markeras dels via
/// exekveringshistoriken, dels på ev. kopplad <see cref="ScheduledReport"/>-rad
/// (MarkeraSomKord räknar om NastaKorning).
/// </summary>
public class ScheduledReportService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledReportService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public ScheduledReportService(IServiceScopeFactory scopeFactory, ILogger<ScheduledReportService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckScheduledReports(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in scheduled report service");
            }
            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CheckScheduledReports(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegionHRDbContext>();
        var execService = scope.ServiceProvider.GetRequiredService<ReportExecutionService>();
        var export = scope.ServiceProvider.GetRequiredService<ExportService>();
        var email = scope.ServiceProvider.GetRequiredService<EmailNotificationSender>();

        List<ReportDefinition> definitions;
        List<ScheduledReport> scheduled;
        try
        {
            definitions = await db.ReportDefinitions
                .Where(r => r.ArSchemalagd && r.CronExpression != null)
                .ToListAsync(ct);
            scheduled = await db.ScheduledReports.ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read scheduled reports (DB may not be available)");
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var def in definitions)
        {
            var sched = scheduled.FirstOrDefault(s => s.ReportTemplateId == def.Id);

            var lastExec = await db.ReportExecutions
                .Where(e => e.ReportDefinitionId == def.Id)
                .OrderByDescending(e => e.StartadVid)
                .FirstOrDefaultAsync(ct);

            if (!ArForfallen(def, sched, lastExec, now)) continue;

            await KorRapportAsync(db, execService, export, email, def, sched, ct);
        }
    }

    /// <summary>Avgör om en definition ska köras nu utifrån ScheduledReport.NastaKorning eller cron-uttryck.</summary>
    private static bool ArForfallen(ReportDefinition def, ScheduledReport? sched, ReportExecution? lastExec, DateTime now)
    {
        // 1. Explicit nästa-körning på kopplad ScheduledReport-rad vinner.
        if (sched?.NastaKorning is { } nasta)
            return nasta <= now;

        // 2. Annars tolka cron-uttrycket relativt senaste körning.
        var cron = CronSchedule.TryParse(def.CronExpression);
        if (cron is not null)
        {
            var ankare = lastExec?.StartadVid ?? now.AddYears(-1);
            return cron.ArForfallenSedan(ankare, now);
        }

        // 3. Otolkbart uttryck → kör en gång om den aldrig körts (fail-safe).
        return lastExec is null;
    }

    private async Task KorRapportAsync(
        RegionHRDbContext db,
        ReportExecutionService execService,
        ExportService export,
        EmailNotificationSender email,
        ReportDefinition def,
        ScheduledReport? sched,
        CancellationToken ct)
    {
        _logger.LogInformation("Kör schemalagd rapport: {Name}", def.Namn);

        var exec = ReportExecution.Starta(def.Id);
        await db.ReportExecutions.AddAsync(exec, ct);
        await db.SaveChangesAsync(ct);

        try
        {
            var result = await execService.ExecuteAsync(def, ct);

            var format = sched?.Format;
            var bytes = ByggFil(export, result, format);
            var filnamn = $"{Sanera(def.Namn)}_{DateTime.UtcNow:yyyyMMddHHmmss}.{Extension(format)}";

            if (!string.IsNullOrWhiteSpace(def.MottagareEpost))
            {
                await email.SendAsync(
                    def.MottagareEpost!, def.MottagareEpost!,
                    $"Schemalagd rapport: {def.Namn}",
                    ByggHtml(def, result, bytes.Length), ct);
            }

            exec.Slutfor($"/reports/{filnamn}");
            sched?.MarkeraSomKord();
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Schemalagd rapport '{Name}' klar: {Rows} rader ({Bytes} byte). Nästa körning: {Next}",
                def.Namn, result.AntalRader, bytes.Length, sched?.NastaKorning?.ToString("u") ?? "enligt cron");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Schemalagd rapport '{Name}' misslyckades", def.Namn);
            exec.MarkeraFel(ex.Message);
            await db.SaveChangesAsync(ct);
        }
    }

    private static byte[] ByggFil(ExportService export, ReportResult result, string? format)
    {
        var headers = result.Rubriker.ToArray();
        if (string.Equals(format, "Excel", StringComparison.OrdinalIgnoreCase))
        {
            return export.ToExcel<IReadOnlyList<string>>(
                result.Rader, "Rapport", headers, row => row.Cast<object>().ToArray());
        }

        // CSV som robust standard (även för PDF-val tills en riktig PDF-motor kopplas in).
        return export.ToCsv<IReadOnlyList<string>>(result.Rader, headers, row => row.ToArray());
    }

    private static string Extension(string? format) =>
        string.Equals(format, "Excel", StringComparison.OrdinalIgnoreCase) ? "xlsx" : "csv";

    private static string ByggHtml(ReportDefinition def, ReportResult result, int bytes)
    {
        var kolumner = result.Rubriker.Count > 0 ? string.Join(", ", result.Rubriker) : "(inga)";
        return $"<h2>{def.Namn}</h2>" +
               $"<p>{def.Beskrivning}</p>" +
               $"<p><strong>{result.AntalRader}</strong> rader genererades{(result.ArGrupperad ? " (grupperad)" : "")}.</p>" +
               $"<p>Kolumner: {kolumner}</p>" +
               $"<p>Bifogad fil: {bytes} byte.</p>" +
               "<hr><p><em>Automatiskt genererad av OpenHR schemalagda rapporter.</em></p>";
    }

    private static string Sanera(string namn)
    {
        var ogiltiga = Path.GetInvalidFileNameChars();
        var rensad = new string(namn.Select(c => ogiltiga.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(rensad) ? "rapport" : rensad;
    }
}
