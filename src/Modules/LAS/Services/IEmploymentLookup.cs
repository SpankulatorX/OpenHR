using RegionHR.SharedKernel.Domain;

namespace RegionHR.LAS.Services;

/// <summary>
/// Läsport som LAS-modulen använder för att slå upp en anställnings period och form
/// utifrån dess <see cref="EmploymentId"/>. Domänevent för anställningar
/// (EmploymentCreatedEvent) bär bara id + form, inte start/slut — därför behöver
/// LAS auto-kedjan denna port för att hämta datumen utan att känna till Core-modellen
/// eller databasen direkt.
///
/// Implementeras i infrastrukturen (EmploymentLookup mot RegionHRDbContext).
/// I test stubbas den utan databas.
/// </summary>
public interface IEmploymentLookup
{
    Task<AnstallningsPeriod?> GetEmploymentAsync(EmploymentId employmentId, CancellationToken ct = default);
}

/// <summary>
/// Minimal projektion av en anställning för LAS-ackumulering: vem, vilken form
/// och vilken tidsperiod (slut är null för öppna/tillsvidareanställningar).
/// </summary>
public sealed record AnstallningsPeriod(
    EmployeeId AnstallId,
    EmploymentType Anstallningsform,
    DateOnly Start,
    DateOnly? Slut);
