using Microsoft.Extensions.Logging;
using RegionHR.Core.Domain;
using RegionHR.LAS.Services;
using RegionHR.SharedKernel.Abstractions;

namespace RegionHR.Infrastructure.LAS;

/// <summary>
/// Domänevent-hanterare som avslutar LAS-uppföljningen när en anställning avslutas:
/// bedömer och sätter företrädesrätt (§25 LAS) via <see cref="LASAutoChainService"/>.
/// Registreras i DI som <see cref="IDomainEventHandler{TEvent}"/> och löses ut av
/// <c>DomainEventDispatcher</c> efter att avslutet sparats.
///
/// Fel i LAS-bokföringen får inte fälla själva avslutet — undantag fångas och loggas.
/// </summary>
public sealed class EmploymentEndedLASHandler : IDomainEventHandler<EmploymentEndedEvent>
{
    private readonly LASAutoChainService _autoChain;
    private readonly ILogger<EmploymentEndedLASHandler> _logger;

    public EmploymentEndedLASHandler(LASAutoChainService autoChain, ILogger<EmploymentEndedLASHandler> logger)
    {
        _autoChain = autoChain;
        _logger = logger;
    }

    public async Task HandleAsync(EmploymentEndedEvent domainEvent, CancellationToken ct = default)
    {
        try
        {
            var result = await _autoChain.AvslutaFranAnstallningAsync(
                domainEvent.EmployeeId, domainEvent.SlutDatum, ct);
            _logger.LogInformation(
                "LAS auto-kedja (anställning {EmploymentId} avslutad {SlutDatum:yyyy-MM-dd}): {Status} — {Meddelande}",
                domainEvent.EmploymentId, domainEvent.SlutDatum, result.Status, result.Meddelande);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "LAS auto-kedja misslyckades för avslutad anställning {EmploymentId}",
                domainEvent.EmploymentId);
        }
    }
}
