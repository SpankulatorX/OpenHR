using RegionHR.SharedKernel.Abstractions;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.SalaryReview.Domain;

/// <summary>
/// Löneöversynsrunda. Hanterar budgetfördelning, löneförslag och facklig avstämning.
/// </summary>
public sealed class SalaryReviewRound : AggregateRoot<Guid>
{
    public string Namn { get; private set; } = string.Empty;
    public int Ar { get; private set; }
    public CollectiveAgreementType Avtalsomrade { get; private set; }
    public Money TotalBudget { get; private set; }
    public Money FordeladBudget { get; private set; } = Money.Zero;
    public Money AterstaendeBudget => TotalBudget - FordeladBudget;
    public SalaryReviewStatus Status { get; private set; }
    public DateOnly IkrafttradandeDatum { get; private set; }
    public string? FackligRepresentant { get; private set; }

    /// <summary>Datum då rundan faktiskt genomfördes (ny lön applicerades). Null tills status är Genomförd.</summary>
    public DateOnly? GenomfordDatum { get; private set; }

    private readonly List<SalaryProposal> _forslag = [];
    public IReadOnlyList<SalaryProposal> Forslag => _forslag.AsReadOnly();

    private SalaryReviewRound() { }

    public static SalaryReviewRound Skapa(
        string namn, int ar, CollectiveAgreementType avtal,
        Money budget, DateOnly ikrafttradande)
    {
        return new SalaryReviewRound
        {
            Id = Guid.NewGuid(),
            Namn = namn,
            Ar = ar,
            Avtalsomrade = avtal,
            TotalBudget = budget,
            Status = SalaryReviewStatus.Planering,
            IkrafttradandeDatum = ikrafttradande
        };
    }

    public SalaryProposal LaggTillForslag(
        EmployeeId anstallId, Money nuvarandeLon, Money foreslagenLon, string motivering,
        EmploymentId? anstallningId = null)
    {
        // Systemet är experten: ogiltiga förslag avvisas här, inte i UI:t.
        // (InvalidOperationException används genomgående för domäninvarianter i denna metod.)
        if (Status != SalaryReviewStatus.Planering)
            throw new InvalidOperationException("Löneförslag kan bara läggas till under planeringsfasen.");
        if (foreslagenLon.Amount <= 0)
            throw new InvalidOperationException("Föreslagen lön måste vara positiv.");
        if (string.IsNullOrWhiteSpace(motivering))
            throw new InvalidOperationException("Motivering måste anges för löneförslaget.");

        var okning = foreslagenLon - nuvarandeLon;
        if (FordeladBudget + okning > TotalBudget)
            throw new InvalidOperationException("Budget överskriden");

        var forslag = new SalaryProposal
        {
            AnstallId = anstallId,
            AnstallningId = anstallningId,
            NuvarandeLon = nuvarandeLon,
            ForeslagenLon = foreslagenLon,
            Okning = okning,
            OkningProcent = nuvarandeLon.Amount > 0 ? (okning.Amount / nuvarandeLon.Amount * 100m) : 0m,
            Motivering = motivering,
            Status = SalaryProposalStatus.Forslag
        };
        _forslag.Add(forslag);
        FordeladBudget = FordeladBudget + okning;
        return forslag;
    }

    /// <summary>
    /// Skickar rundan till facklig avstämning. Kräver att minst ett förslag finns.
    /// </summary>
    public void SkickaFackligAvstemning()
    {
        if (Status != SalaryReviewStatus.Planering)
            throw new InvalidOperationException("Kan bara skicka till facklig avstämning från planeringsstatus");

        if (_forslag.Count == 0)
            throw new InvalidOperationException("Inga förslag att skicka till facklig avstämning");

        Status = SalaryReviewStatus.FackligAvstemning;
    }

    /// <summary>
    /// Facklig representant godkänner löneöversynsrundan.
    /// </summary>
    public void GodkannFacklig(string fackligRepresentant)
    {
        if (Status != SalaryReviewStatus.FackligAvstemning)
            throw new InvalidOperationException("Kan bara godkänna fackligt från FackligAvstemning-status");

        if (string.IsNullOrWhiteSpace(fackligRepresentant))
            throw new ArgumentException("Facklig representant måste anges", nameof(fackligRepresentant));

        FackligRepresentant = fackligRepresentant;
        Status = SalaryReviewStatus.Godkand;
    }

    /// <summary>
    /// Avvisar ett enskilt löneförslag.
    /// </summary>
    public void AvvisaForslag(Guid forslagId, string anledning)
    {
        if (string.IsNullOrWhiteSpace(anledning))
            throw new ArgumentException("Anledning måste anges", nameof(anledning));

        var forslag = _forslag.FirstOrDefault(f => f.Id == forslagId)
            ?? throw new InvalidOperationException($"Förslag {forslagId} hittades inte");

        if (forslag.Status != SalaryProposalStatus.Forslag)
            throw new InvalidOperationException("Kan bara avvisa förslag med status Forslag");

        forslag.Status = SalaryProposalStatus.Avslagen;
        forslag.AvvisningsAnledning = anledning;

        // Återställ budgeten
        FordeladBudget = FordeladBudget - forslag.Okning;
    }

    /// <summary>
    /// Godkänner ett enskilt löneförslag.
    /// </summary>
    public void GodkannForslag(Guid forslagId)
    {
        var forslag = _forslag.FirstOrDefault(f => f.Id == forslagId)
            ?? throw new InvalidOperationException($"Förslag {forslagId} hittades inte");

        if (forslag.Status != SalaryProposalStatus.Forslag)
            throw new InvalidOperationException("Kan bara godkänna förslag med status Forslag");

        forslag.Status = SalaryProposalStatus.Godkand;
    }

    /// <summary>
    /// Genomsnittlig löneökning i procent för alla aktiva (ej avslagna) förslag.
    /// </summary>
    public decimal GenomsnittligOkningProcent
    {
        get
        {
            var aktiva = _forslag.Where(f => f.Status != SalaryProposalStatus.Avslagen).ToList();
            if (aktiva.Count == 0) return 0m;
            return aktiva.Average(f => f.OkningProcent);
        }
    }

    /// <summary>
    /// Genomför löneöversynsrundan. Kräver att status är Godkand.
    /// Sätter genomförandedatum som markerar slutet på det retroaktiva fönstret
    /// [IkrafttradandeDatum, GenomfordDatum] som lönekörningen ska efterbetala.
    /// </summary>
    public void Genomfor(DateOnly? genomfordDatum = null)
    {
        if (Status != SalaryReviewStatus.Godkand)
            throw new InvalidOperationException("Kan bara genomföra från Godkänd status");
        Status = SalaryReviewStatus.Genomford;
        GenomfordDatum = genomfordDatum ?? DateOnly.FromDateTime(DateTime.UtcNow);
    }
}

public enum SalaryReviewStatus { Planering, FackligAvstemning, Godkand, Genomford }

public enum SalaryProposalStatus { Forslag, Godkand, Avslagen }

public sealed class SalaryProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public EmployeeId AnstallId { get; set; }

    /// <summary>
    /// Den specifika anställning som ska få ny lön. Kan vara null om den anställde
    /// bara har en aktiv anställning — då härleds den vid genomförandet.
    /// </summary>
    public EmploymentId? AnstallningId { get; set; }

    public Money NuvarandeLon { get; set; }
    public Money ForeslagenLon { get; set; }
    public Money Okning { get; set; }
    public decimal OkningProcent { get; set; }
    public string Motivering { get; set; } = string.Empty;
    public SalaryProposalStatus Status { get; set; } = SalaryProposalStatus.Forslag;
    public string? AvvisningsAnledning { get; set; }

    /// <summary>
    /// Retroaktivt bruttobelopp som beräknats vid genomförandet
    /// (löneökning × antal månader från ikraftträdande till genomförande).
    /// Lönekörningen betalar ut detta som retroaktiv differens. Null tills genomfört.
    /// </summary>
    public Money? RetroaktivtBelopp { get; set; }
}
