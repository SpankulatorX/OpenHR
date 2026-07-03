namespace RegionHR.Infrastructure.Arbetsmiljo;

public enum RiskStatus { Identifierad, UnderBehandling, Atgardad }

/// <summary>
/// Riskbedömning med sannolikhet × konsekvens.
/// RiskVarde är en beräknad property — lagras INTE i DB.
/// </summary>
public class RiskAssessment
{
    public Guid Id { get; private set; }
    public string RiskNamn { get; private set; } = default!;
    public string? Beskrivning { get; private set; }
    public int Sannolikhet { get; private set; } // 1-5
    public int Konsekvens { get; private set; }  // 1-5
    public RiskStatus Status { get; private set; }
    public string? Atgard { get; private set; }

    /// <summary>Fritext — ingen koppling till Employee i v1.</summary>
    public string? Ansvarig { get; private set; }

    public DateOnly? Deadline { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>Beräknad property. Lagras inte i DB. Sannolikhet × Konsekvens.</summary>
    public int RiskVarde => Sannolikhet * Konsekvens;

    private RiskAssessment() { }

    public static RiskAssessment Skapa(
        string riskNamn, string? beskrivning,
        int sannolikhet, int konsekvens,
        string? atgard, string? ansvarig, DateOnly? deadline)
    {
        if (sannolikhet < 1 || sannolikhet > 5)
            throw new ArgumentException("Sannolikhet måste vara 1-5", nameof(sannolikhet));
        if (konsekvens < 1 || konsekvens > 5)
            throw new ArgumentException("Konsekvens måste vara 1-5", nameof(konsekvens));

        return new RiskAssessment
        {
            Id = Guid.NewGuid(),
            RiskNamn = riskNamn,
            Beskrivning = beskrivning,
            Sannolikhet = sannolikhet,
            Konsekvens = konsekvens,
            Status = RiskStatus.Identifierad,
            Atgard = atgard,
            Ansvarig = ansvarig,
            Deadline = deadline,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Nästa status i riskbedömningens livscykel
    /// (Identifierad → Under behandling → Åtgärdad), eller null om redan åtgärdad.
    /// </summary>
    public RiskStatus? NastaStatus => Status switch
    {
        RiskStatus.Identifierad => RiskStatus.UnderBehandling,
        RiskStatus.UnderBehandling => RiskStatus.Atgardad,
        _ => null
    };

    /// <summary>Flyttar riskbedömningen ett steg framåt i åtgärdskedjan.</summary>
    public void FlyttaFram()
    {
        if (NastaStatus is { } nasta)
            Status = nasta;
    }

    /// <summary>Sätter eller uppdaterar deadline för åtgärden.</summary>
    public void SattDeadline(DateOnly? deadline) => Deadline = deadline;
}
