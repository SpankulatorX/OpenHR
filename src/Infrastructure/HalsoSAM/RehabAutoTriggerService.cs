using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RegionHR.HalsoSAM.Services;

namespace RegionHR.Infrastructure.HalsoSAM;

/// <summary>
/// Bakgrundsjobb som förverkligar den (tidigare döda) automatiska rehab-triggningen.
/// Skannar sjukfrånvaro per anställd, kör <see cref="SickLeaveMonitor"/> och skapar
/// automatiskt ett rehabärende när en tröskel passeras — förankrat i sjukfallets
/// faktiska dag 1 så att milstolparna (dag 14/90/180/365) blir korrekta.
/// Idempotent: skapar aldrig dubblett om den anställde redan har ett pågående ärende.
/// </summary>
public sealed class RehabAutoTriggerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RehabAutoTriggerService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(6);

    public RehabAutoTriggerService(
        IServiceScopeFactory scopeFactory, ILogger<RehabAutoTriggerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Låt seed/uppstart hinna klart innan första körningen.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var antal = await KorEnGangAsync(stoppingToken);
                if (antal > 0)
                    _logger.LogInformation("RehabAutoTriggerService: skapade {Antal} nya rehabärenden", antal);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fel vid automatisk rehab-triggning");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Kör en enda skanning. Returnerar antal automatiskt skapade rehabärenden.
    /// Bryts ut för testbarhet/manuell körning.
    /// </summary>
    internal async Task<int> KorEnGangAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<SickLeaveNotificationDataProvider>();
        var rehabService = scope.ServiceProvider.GetRequiredService<RehabService>();
        var monitor = scope.ServiceProvider.GetRequiredService<SickLeaveMonitor>();

        var perPerson = await provider.HamtaPerioderPerAnstalldAsync(ct);
        var skapade = 0;

        foreach (var (anstallId, perioder) in perPerson)
        {
            var signal = monitor.AnalyseraSignal(perioder);
            if (signal is null) continue;

            var skapat = await rehabService.SkapaOmSaknasAsync(anstallId, signal, ct);
            if (skapat is not null)
            {
                skapade++;
                _logger.LogInformation(
                    "Auto-skapade rehabärende {CaseId} för anställd {AnstallId}: trigger {Trigger}, sjukfall dag 1 {Dag1}",
                    skapat.Id, anstallId.Value, signal.Trigger, signal.SjukfallDag1);
            }
        }

        return skapade;
    }
}
