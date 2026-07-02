using RegionHR.SharedKernel.Abstractions;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Travel.Domain;

/// <summary>
/// Resekrav/utlägg.
/// </summary>
public sealed class TravelClaim : AggregateRoot<Guid>
{
    public EmployeeId AnstallId { get; private set; }
    public string Beskrivning { get; private set; } = string.Empty;
    public DateOnly ReseDatum { get; private set; }
    public TravelClaimStatus Status { get; private set; }
    public Money TotalBelopp { get; private set; } = Money.Zero;

    // Traktamente (Skatteverkets satser 2025)
    public int? HelaDagar { get; private set; }
    public int? HalvaDagar { get; private set; }
    public Money? Traktamente { get; private set; }

    // Milersättning
    public decimal? KordaMil { get; private set; }
    public Money? Milersattning { get; private set; }

    // Attestering
    public string? AttesteradAv { get; private set; }
    public DateTime? AttesteradVid { get; private set; }
    public string? AvvisningsAnledning { get; private set; }

    private readonly List<ExpenseItem> _utlagg = [];
    public IReadOnlyList<ExpenseItem> Utlagg => _utlagg.AsReadOnly();

    private TravelClaim() { }

    // Skatteverkets satser 2025
    private const decimal TRAKTAMENTE_HELDAG_INRIKES = 300m;   // Skatteverket, inkomstår 2026
    private const decimal TRAKTAMENTE_HALVDAG_INRIKES = 150m;  // Skatteverket, inkomstår 2026
    private const decimal MILERSATTNING_SATS = 25m; // kr per mil

    public static TravelClaim Skapa(EmployeeId anstallId, string beskrivning, DateOnly datum)
    {
        return new TravelClaim
        {
            Id = Guid.NewGuid(),
            AnstallId = anstallId,
            Beskrivning = beskrivning,
            ReseDatum = datum,
            Status = TravelClaimStatus.Utkast
        };
    }

    public void SattTraktamente(int helaDagar, int halvaDagar)
    {
        HelaDagar = helaDagar;
        HalvaDagar = halvaDagar;
        Traktamente = Money.SEK(helaDagar * TRAKTAMENTE_HELDAG_INRIKES + halvaDagar * TRAKTAMENTE_HALVDAG_INRIKES);
        BeraknaTotal();
    }

    public void SattMilersattning(decimal mil)
    {
        KordaMil = mil;
        Milersattning = Money.SEK(mil * MILERSATTNING_SATS);
        BeraknaTotal();
    }

    public void LaggTillUtlagg(string beskrivning, Money belopp, string? kvittoBildId = null)
    {
        _utlagg.Add(new ExpenseItem
        {
            Beskrivning = beskrivning,
            Belopp = belopp,
            KvittoBildId = kvittoBildId
        });
        BeraknaTotal();
    }

    public void SkickaIn()
    {
        if (Status != TravelClaimStatus.Utkast)
            throw new InvalidOperationException("Kan bara skicka in resekrav med status Utkast");

        Status = TravelClaimStatus.Inskickad;
    }

    /// <summary>
    /// Attesterar (godkänner) resekravet.
    /// </summary>
    public void Attestera(string attestant)
    {
        if (Status != TravelClaimStatus.Inskickad)
            throw new InvalidOperationException("Kan bara attestera inskickade resekrav");

        if (string.IsNullOrWhiteSpace(attestant))
            throw new ArgumentException("Attestant måste anges", nameof(attestant));

        AttesteradAv = attestant;
        AttesteradVid = DateTime.UtcNow;
        Status = TravelClaimStatus.Godkand;
    }

    /// <summary>
    /// Avvisar resekravet.
    /// </summary>
    public void Avvisa(string attestant, string anledning)
    {
        if (Status != TravelClaimStatus.Inskickad)
            throw new InvalidOperationException("Kan bara avvisa inskickade resekrav");

        if (string.IsNullOrWhiteSpace(attestant))
            throw new ArgumentException("Attestant måste anges", nameof(attestant));

        if (string.IsNullOrWhiteSpace(anledning))
            throw new ArgumentException("Anledning måste anges", nameof(anledning));

        AttesteradAv = attestant;
        AttesteradVid = DateTime.UtcNow;
        AvvisningsAnledning = anledning;
        Status = TravelClaimStatus.Avslagen;
    }

    /// <summary>
    /// Markerar resekravet som utbetalt.
    /// </summary>
    public void MarkeraSomUtbetald()
    {
        if (Status != TravelClaimStatus.Godkand)
            throw new InvalidOperationException("Kan bara markera godkända resekrav som utbetalda");

        Status = TravelClaimStatus.Utbetald;
    }

    private void BeraknaTotal()
    {
        TotalBelopp = (Traktamente ?? Money.Zero) +
                      (Milersattning ?? Money.Zero) +
                      Money.SEK(_utlagg.Sum(u => u.Belopp.Amount));
    }
}

public enum TravelClaimStatus { Utkast, Inskickad, Godkand, Utbetald, Avslagen }

public sealed class ExpenseItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Beskrivning { get; set; } = string.Empty;
    public Money Belopp { get; set; }
    public string? KvittoBildId { get; set; }
}
