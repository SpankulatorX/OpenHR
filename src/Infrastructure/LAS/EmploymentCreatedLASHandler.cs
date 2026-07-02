using Microsoft.Extensions.Logging;
using RegionHR.Core.Domain;
using RegionHR.LAS.Services;
using RegionHR.SharedKernel.Abstractions;

namespace RegionHR.Infrastructure.LAS;

/// <summary>
/// Domänevent-hanterare som auto-registrerar en LAS-period när en anställning skapas.
/// Följer dispatchermönstret i <c>DomainEventDispatcher</c>: registreras i DI som
/// <see cref="IDomainEventHandler{TEvent}"/> och löses ut via <c>GetServices</c> efter
/// att anställningen sparats (SavedChanges-interceptorn).
///
/// Endast visstid (SAVA/vikariat) ackumuleras; övriga former ignoreras av
/// <see cref="LASAutoChainService"/>. Fel i LAS-bokföringen får aldrig fälla själva
/// anställningsregistreringen, därför fångas och loggas undantag här (samma filosofi
/// som automations-/webhook-stegen i dispatchern).
/// </summary>
public sealed class EmploymentCreatedLASHandler : IDomainEventHandler<EmploymentCreatedEvent>
{
    private readonly LASAutoChainService _autoChain;
    private readonly ILogger<EmploymentCreatedLASHandler> _logger;

    public EmploymentCreatedLASHandler(LASAutoChainService autoChain, ILogger<EmploymentCreatedLASHandler> logger)
    {
        _autoChain = autoChain;
        _logger = logger;
    }

    public async Task HandleAsync(EmploymentCreatedEvent domainEvent, CancellationToken ct = default)
    {
        try
        {
            var result = await _autoChain.RegistreraFranAnstallningAsync(domainEvent.EmploymentId, ct);
            _logger.LogInformation(
                "LAS auto-kedja (anställning {EmploymentId} skapad): {Status} — {Meddelande}",
                domainEvent.EmploymentId, result.Status, result.Meddelande);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "LAS auto-kedja misslyckades för skapad anställning {EmploymentId}",
                domainEvent.EmploymentId);
        }
    }
}
