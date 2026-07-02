using RegionHR.SharedKernel.Domain;

namespace RegionHR.Scheduling.Domain;

/// <summary>Hur intjänad komptid tas ut.</summary>
public enum KomputtagTyp
{
    /// <summary>Tas ut som betald ledighet (kompledig) — genererar en ledighetspost.</summary>
    Ledighet,

    /// <summary>Tas ut som lön (utbetalning i nästa lönekörning).</summary>
    Utbetalning
}

/// <summary>Livscykel för en komptidsuttagsbegäran.</summary>
public enum KomputtagStatus
{
    /// <summary>Begärd av medarbetaren, väntar på chefsgodkännande.</summary>
    Begard,

    /// <summary>Godkänd av chef — saldot är draget.</summary>
    Godkand,

    /// <summary>Avslagen av chef — saldot orört.</summary>
    Avslagen,

    /// <summary>Återkallad av medarbetaren innan beslut.</summary>
    Aterkallad
}

/// <summary>
/// En begäran om att ta ut intjänad komptid, antingen som kompledig eller som utbetalning.
/// Är den godkänningsbara posten i flödet: medarbetaren begär (<see cref="KomputtagStatus.Begard"/>),
/// chef godkänner eller avslår. Först vid godkännande dras komptidssaldot
/// (via <see cref="FlexBalance.RegistreraKomputtag"/>) — statusövergångarna här bär bara
/// arbetsflödet; själva saldodragningen orkestreras av webblagret så att den blir atomisk
/// med ev. ledighetspost och notifiering.
/// </summary>
public sealed class KomptidUttag
{
    public Guid Id { get; private set; }

    public EmployeeId AnstallId { get; private set; }

    /// <summary>Antal timmar som begärs tas ut (alltid positivt).</summary>
    public decimal Timmar { get; private set; }

    public KomputtagTyp Typ { get; private set; }

    public KomputtagStatus Status { get; private set; }

    /// <summary>Första ledighetsdag (endast för <see cref="KomputtagTyp.Ledighet"/>).</summary>
    public DateOnly? FranDatum { get; private set; }

    /// <summary>Sista ledighetsdag (endast för <see cref="KomputtagTyp.Ledighet"/>).</summary>
    public DateOnly? TillDatum { get; private set; }

    /// <summary>Koppling till den ledighetspost som skapades vid godkänt kompledigt uttag.</summary>
    public Guid? LedighetspostId { get; private set; }

    public string? Beskrivning { get; private set; }

    public DateTime BegardVid { get; private set; }

    public Guid? HandlagdAv { get; private set; }

    public DateTime? HandlagdVid { get; private set; }

    public string? Kommentar { get; private set; }

    private KomptidUttag() { } // EF Core

    /// <summary>
    /// Skapar en ny uttagsbegäran med status <see cref="KomputtagStatus.Begard"/>.
    /// Validerar att timmar är positivt och att ett ledighetsuttag har giltiga datum.
    /// </summary>
    public static KomptidUttag Skapa(
        EmployeeId anstallId,
        decimal timmar,
        KomputtagTyp typ,
        DateOnly? franDatum,
        DateOnly? tillDatum,
        string? beskrivning)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timmar);

        if (typ == KomputtagTyp.Ledighet)
        {
            if (franDatum is null || tillDatum is null)
                throw new ArgumentException("Kompledigt uttag kräver från- och tilldatum.");
            if (tillDatum < franDatum)
                throw new ArgumentException("Slutdatum kan inte vara före startdatum.");
        }
        else
        {
            // Utbetalning har ingen ledighetsperiod.
            franDatum = null;
            tillDatum = null;
        }

        return new KomptidUttag
        {
            Id = Guid.NewGuid(),
            AnstallId = anstallId,
            Timmar = Math.Round(timmar, 2),
            Typ = typ,
            Status = KomputtagStatus.Begard,
            FranDatum = franDatum,
            TillDatum = tillDatum,
            Beskrivning = beskrivning,
            BegardVid = DateTime.UtcNow
        };
    }

    /// <summary>Godkänner begäran (Begärd → Godkänd). Kastar om den inte väntar på beslut.</summary>
    public void Godkann(Guid handlaggare, string? kommentar)
    {
        if (Status != KomputtagStatus.Begard)
            throw new InvalidOperationException(
                $"Kan bara godkänna en begäran som väntar på beslut. Nuvarande status: {Status}.");

        HandlagdAv = handlaggare;
        HandlagdVid = DateTime.UtcNow;
        Kommentar = kommentar;
        Status = KomputtagStatus.Godkand;
    }

    /// <summary>Kopplar den ledighetspost som skapades vid ett godkänt kompledigt uttag.</summary>
    public void KopplaLedighetspost(Guid ledighetspostId)
    {
        if (Status != KomputtagStatus.Godkand)
            throw new InvalidOperationException("Ledighetspost kan bara kopplas till ett godkänt uttag.");

        LedighetspostId = ledighetspostId;
    }

    /// <summary>Avslår begäran med obligatorisk motivering (Begärd → Avslagen).</summary>
    public void Avsla(Guid handlaggare, string kommentar)
    {
        if (Status != KomputtagStatus.Begard)
            throw new InvalidOperationException(
                $"Kan bara avslå en begäran som väntar på beslut. Nuvarande status: {Status}.");

        ArgumentException.ThrowIfNullOrWhiteSpace(kommentar);

        HandlagdAv = handlaggare;
        HandlagdVid = DateTime.UtcNow;
        Kommentar = kommentar;
        Status = KomputtagStatus.Avslagen;
    }

    /// <summary>Medarbetaren återkallar sin egen begäran innan beslut (Begärd → Återkallad).</summary>
    public void Aterkalla()
    {
        if (Status != KomputtagStatus.Begard)
            throw new InvalidOperationException(
                $"Kan bara återkalla en begäran som väntar på beslut. Nuvarande status: {Status}.");

        Status = KomputtagStatus.Aterkallad;
    }
}
