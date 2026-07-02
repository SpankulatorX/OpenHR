using RegionHR.SharedKernel.Abstractions;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.HalsoSAM.Domain;

/// <summary>
/// Rehabiliteringsärende (HälsoSAM).
/// Triggas automatiskt vid sjukfrånvaromönster.
/// </summary>
public sealed class RehabCase : AggregateRoot<Guid>
{
    /// <summary>GDPR: antal år efter avslut innan gallring.</summary>
    private const int GALLRINGS_AR = 2;

    public EmployeeId AnstallId { get; private set; }
    public RehabTrigger Trigger { get; private set; }
    public RehabStatus Status { get; private set; }
    public EmployeeId? ArendeagareHR { get; private set; }
    public DateTime SkapadVid { get; private set; }
    public string? RehabPlan { get; private set; }

    /// <summary>
    /// Sjukfallets faktiska dag 1 (första sjukdagen / sjukanmälan / läkarintygets
    /// startdatum). ALLA milstolpar (dag 14/90/180/365) räknas från detta datum,
    /// inte från när ärendet skapades i systemet. Se <see cref="Rehabkedja"/>.
    /// </summary>
    public DateOnly? SjukfallDag1 { get; private set; }

    // Uppföljningsdagar enligt rehabiliteringskedjan — beräknade från SjukfallDag1
    public DateTime? Uppfoljning14Dagar { get; private set; }
    public DateTime? Uppfoljning90Dagar { get; private set; }
    public DateTime? Uppfoljning180Dagar { get; private set; }
    public DateTime? Uppfoljning365Dagar { get; private set; }

    /// <summary>GDPR: automatiskt satt till (ärendet avslutat + 2 år) vid avslut.</summary>
    public DateTime? GallringsDatum { get; private set; }

    private readonly List<RehabNote> _anteckningar = [];
    public IReadOnlyList<RehabNote> Anteckningar => _anteckningar.AsReadOnly();

    private readonly List<RehabUppfoljning> _uppfoljningar = [];
    public IReadOnlyList<RehabUppfoljning> Uppfoljningar => _uppfoljningar.AsReadOnly();

    private RehabCase() { }

    /// <summary>
    /// Skapar ett rehabärende utan känt sjukfallsstartdatum. Milstolparna ankras då
    /// vid skapandeögonblicket (bakåtkompatibelt bekvämlighetsanrop). Använd hellre
    /// överlagringen med explicit <paramref name="anstallId"/>+dag 1 så att kedjan
    /// räknas från sjukfallets faktiska första dag.
    /// </summary>
    public static RehabCase Skapa(EmployeeId anstallId, RehabTrigger trigger)
    {
        var now = DateTime.UtcNow;
        var rehab = new RehabCase
        {
            Id = Guid.NewGuid(),
            AnstallId = anstallId,
            Trigger = trigger,
            Status = RehabStatus.Signal,
            SkapadVid = now,
            SjukfallDag1 = DateOnly.FromDateTime(now)
        };
        rehab.BeraknaMilstolpar(now);
        return rehab;
    }

    /// <summary>
    /// Skapar ett rehabärende förankrat i sjukfallets faktiska dag 1. Milstolparna
    /// (dag 14/90/180/365 enligt <see cref="Rehabkedja"/>) beräknas från
    /// <paramref name="sjukfallDag1"/> — detta är det korrekta produktionsanropet.
    /// </summary>
    public static RehabCase Skapa(EmployeeId anstallId, RehabTrigger trigger, DateOnly sjukfallDag1)
    {
        var rehab = new RehabCase
        {
            Id = Guid.NewGuid(),
            AnstallId = anstallId,
            Trigger = trigger,
            Status = RehabStatus.Signal,
            SkapadVid = DateTime.UtcNow,
            SjukfallDag1 = sjukfallDag1
        };
        rehab.BeraknaMilstolpar(AnkareFor(sjukfallDag1));
        return rehab;
    }

    /// <summary>
    /// Seed/test-factory som skapar ett rehabärende med ett givet startdatum.
    /// Milstolpar beräknas konsekvent från startdatum, inte DateTime.UtcNow.
    /// Inte avsedd att anropas från UI eller produktionskod.
    /// </summary>
    internal static RehabCase SkapaForSeed(EmployeeId anstallId, RehabTrigger trigger, DateTime skapadVid)
    {
        var rehab = new RehabCase
        {
            Id = Guid.NewGuid(),
            AnstallId = anstallId,
            Trigger = trigger,
            Status = RehabStatus.Signal,
            SkapadVid = skapadVid,
            SjukfallDag1 = DateOnly.FromDateTime(skapadVid)
        };
        rehab.BeraknaMilstolpar(skapadVid);
        return rehab;
    }

    /// <summary>
    /// Korrigerar/sätter sjukfallets dag 1 och räknar om samtliga milstolpar därifrån.
    /// Används när det verkliga sjukfallsstartdatumet blir känt efter att ärendet skapats.
    /// </summary>
    public void SattSjukfallDag1(DateOnly sjukfallDag1)
    {
        SjukfallDag1 = sjukfallDag1;
        BeraknaMilstolpar(AnkareFor(sjukfallDag1));
    }

    /// <summary>Beräknar milstolpe-datumen (dag 14/90/180/365) från en ankarpunkt.</summary>
    private void BeraknaMilstolpar(DateTime ankare)
    {
        Uppfoljning14Dagar = ankare.AddDays(14);
        Uppfoljning90Dagar = ankare.AddDays(90);
        Uppfoljning180Dagar = ankare.AddDays(180);
        Uppfoljning365Dagar = ankare.AddDays(365);
    }

    /// <summary>Ankarpunkt = sjukfallets dag 1 vid 00:00 UTC.</summary>
    private static DateTime AnkareFor(DateOnly sjukfallDag1) =>
        DateTime.SpecifyKind(sjukfallDag1.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    /// <summary>Har uppföljning för given milstolpe (dag 14/90/180/365) redan registrerats?</summary>
    public bool ArUppfoljningRegistrerad(int dagNr) => _uppfoljningar.Any(u => u.DagNr == dagNr);

    public void TilldelaArendeagare(EmployeeId hrPerson)
    {
        ArendeagareHR = hrPerson;
        Status = RehabStatus.UnderUtredning;
    }

    public void SattRehabPlan(string plan)
    {
        RehabPlan = plan;
        Status = RehabStatus.AktivRehab;
    }

    public void LaggTillAnteckning(string text, EmployeeId forfattare)
    {
        _anteckningar.Add(new RehabNote
        {
            Text = text,
            ForfattareId = forfattare,
            SkapadVid = DateTime.UtcNow
        });
    }

    /// <summary>Registrera att en uppföljning har genomförts.</summary>
    public void RegistreraUppfoljning(int dagNr, string kommentar, EmployeeId utfordAv)
    {
        var uppfoljning = RehabUppfoljning.Skapa(dagNr, kommentar, utfordAv);
        _uppfoljningar.Add(uppfoljning);
    }

    public void Avsluta(string slutsats)
    {
        Status = RehabStatus.Avslutad;
        GallringsDatum = DateTime.UtcNow.AddYears(GALLRINGS_AR);
        LaggTillAnteckning($"Ärende avslutat: {slutsats}", ArendeagareHR ?? AnstallId);
    }
}

public enum RehabTrigger
{
    SexTillfallenTolvManader,       // 6+ sjuktillfällen på 12 månader
    FjortonSammanhangandeDagar,     // 14+ sammanhängande sjukdagar
    MonsterDetekterat,              // Mönsterdetektering (t.ex. alltid fredagar)
    ChefInitierat,                  // Initierat av chef
    MedarbetareInitierat            // Initierat av medarbetaren själv
}

public enum RehabStatus
{
    Signal,
    UnderUtredning,
    AktivRehab,
    Uppfoljning,
    Avslutad
}

public sealed class RehabNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public EmployeeId ForfattareId { get; set; }
    public DateTime SkapadVid { get; set; }
}
