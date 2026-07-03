namespace RegionHR.Infrastructure.Arbetsmiljo;

public enum SafetyRoundStatus { Planerad, Genomford, Avslutad }

/// <summary>
/// Skyddsrond genomförd vid en enhet.
/// </summary>
public class SafetyRound
{
    public Guid Id { get; private set; }
    public DateTime Datum { get; private set; }

    /// <summary>Logisk referens till OrganizationUnit.Id.Value. Inget FK-constraint.</summary>
    public Guid EnhetId { get; private set; }

    /// <summary>Fritext — kommaseparerade namn i v1.</summary>
    public string Deltagare { get; private set; } = default!;

    public int AntalBrister { get; private set; }
    public SafetyRoundStatus Status { get; private set; }
    public string? Anteckningar { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private SafetyRound() { }

    public static SafetyRound Skapa(
        DateTime datum, Guid enhetId, string deltagare,
        int antalBrister, SafetyRoundStatus status, string? anteckningar = null)
    {
        return new SafetyRound
        {
            Id = Guid.NewGuid(),
            Datum = datum,
            EnhetId = enhetId,
            Deltagare = deltagare,
            AntalBrister = antalBrister,
            Status = status,
            Anteckningar = anteckningar,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Registrerar att ronden genomförts. Antalet upptäckta brister och
    /// eventuella anteckningar sparas och status sätts till Genomförd.
    /// </summary>
    public void Genomfor(int antalBrister, string? anteckningar = null)
    {
        if (antalBrister < 0)
            throw new ArgumentOutOfRangeException(nameof(antalBrister), "Antal brister kan inte vara negativt.");
        AntalBrister = antalBrister;
        Anteckningar = anteckningar;
        Status = SafetyRoundStatus.Genomford;
    }

    /// <summary>Stänger ronden när alla brister åtgärdats.</summary>
    public void Avsluta()
    {
        Status = SafetyRoundStatus.Avslutad;
    }
}
