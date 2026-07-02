using RegionHR.Core.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.SalaryReview.Domain;

/// <summary>
/// Genomför en fackligt godkänd löneöversynsrunda: applicerar varje godkänt
/// löneförslag som en faktisk löneändring på rätt anställning och beräknar det
/// retroaktiva belopp som lönekörningen ska efterbetala.
///
/// Ren domänlogik utan databasberoende — den anropande tjänsten laddar
/// aggregaten, kör motorn och persisterar i samma enhet av arbete.
/// Rör aldrig Core-domänen direkt; ändrar lön endast via aggregatet Employee.
/// </summary>
public sealed class SalaryReviewExecutionEngine
{
    /// <summary>
    /// Applicerar alla godkända löneförslag i rundan på de medskickade anställda,
    /// flyttar rundan till status Genomförd och returnerar en sammanställning.
    /// </summary>
    /// <param name="runda">Rundan som ska genomföras (måste vara fackligt godkänd).</param>
    /// <param name="anstallda">Berörda anställda, uppslagsbara på deras EmployeeId.</param>
    /// <param name="genomforandeDatum">Datum då lönen faktiskt ändras (slut på retrofönstret).</param>
    /// <param name="genomfordAv">Vem som genomför (loggas som ändrare på anställningen).</param>
    public SalaryReviewExecutionResult Genomfor(
        SalaryReviewRound runda,
        IReadOnlyDictionary<EmployeeId, Employee> anstallda,
        DateOnly genomforandeDatum,
        string genomfordAv)
    {
        ArgumentNullException.ThrowIfNull(runda);
        ArgumentNullException.ThrowIfNull(anstallda);
        if (string.IsNullOrWhiteSpace(genomfordAv))
            throw new ArgumentException("Genomförare måste anges.", nameof(genomfordAv));
        if (runda.Status != SalaryReviewStatus.Godkand)
            throw new InvalidOperationException(
                "Löneöversynen måste vara fackligt godkänd innan den kan genomföras.");

        var godkanda = runda.Forslag
            .Where(f => f.Status == SalaryProposalStatus.Godkand)
            .ToList();
        if (godkanda.Count == 0)
            throw new InvalidOperationException("Inga godkända löneförslag att genomföra.");

        var andringar = new List<AppliedSalaryChange>();
        foreach (var forslag in godkanda)
        {
            if (!anstallda.TryGetValue(forslag.AnstallId, out var employee))
                throw new InvalidOperationException(
                    $"Anställd {forslag.AnstallId} saknas och kan inte få ny lön.");

            var anstallning = LosAnstallning(runda, employee, forslag);

            // Applicera ny lön via aggregatet (Core validerar och höjer domänhändelse).
            employee.AndraAnstallningsLon(anstallning.Id, forslag.ForeslagenLon, genomfordAv);

            var retroManader = RetroaktivaManader(runda.IkrafttradandeDatum, genomforandeDatum);
            var retroBelopp = forslag.Okning * retroManader;
            forslag.RetroaktivtBelopp = retroBelopp;

            andringar.Add(new AppliedSalaryChange(
                forslag.AnstallId,
                anstallning.Id,
                forslag.NuvarandeLon,
                forslag.ForeslagenLon,
                forslag.Okning,
                retroManader,
                retroBelopp));
        }

        runda.Genomfor(genomforandeDatum);

        var totalOkning = andringar.Count > 0
            ? Money.SEK(andringar.Sum(a => a.Okning.Amount))
            : Money.Zero;
        var totalRetro = andringar.Count > 0
            ? Money.SEK(andringar.Sum(a => a.RetroaktivtBelopp.Amount))
            : Money.Zero;

        return new SalaryReviewExecutionResult(
            runda.Id,
            andringar.AsReadOnly(),
            totalOkning,
            totalRetro,
            runda.IkrafttradandeDatum,
            genomforandeDatum);
    }

    /// <summary>
    /// Antal hela månader mellan ikraftträdande och genomförande. Är ikraftträdandet
    /// i framtiden (eller samma månad) finns ingen retroaktivitet och resultatet är 0.
    /// </summary>
    public static int RetroaktivaManader(DateOnly ikrafttradande, DateOnly genomforande)
    {
        var manader = (genomforande.Year - ikrafttradande.Year) * 12
                      + (genomforande.Month - ikrafttradande.Month);
        return manader > 0 ? manader : 0;
    }

    /// <summary>
    /// Avgör vilken anställning som ska få den nya lönen. Systemet är experten:
    /// vid tvetydighet kastas ett fel i stället för att gissa fel anställning.
    /// </summary>
    private static Employment LosAnstallning(
        SalaryReviewRound runda, Employee employee, SalaryProposal forslag)
    {
        // 1. Explicit angiven anställning vinner alltid.
        if (forslag.AnstallningId is { } explicitId)
        {
            return employee.Anstallningar.FirstOrDefault(a => a.Id == explicitId)
                ?? throw new InvalidOperationException(
                    $"Anställning {explicitId} finns inte på anställd {forslag.AnstallId}.");
        }

        // 2. Aktiva anställningar på ikraftträdandedatumet.
        var aktiva = employee.AktivaAnstallningar(runda.IkrafttradandeDatum);
        if (aktiva.Count == 0)
            throw new InvalidOperationException(
                $"Anställd {forslag.AnstallId} har ingen aktiv anställning " +
                $"{runda.IkrafttradandeDatum:yyyy-MM-dd} att applicera ny lön på.");

        // 3. Smalna av på avtalsområde om det ger minst en träff.
        var kandidater = aktiva.Where(a => a.Kollektivavtal == runda.Avtalsomrade).ToList();
        if (kandidater.Count == 0)
            kandidater = aktiva.ToList();

        // 4. Föredra den anställning vars nuvarande lön matchar förslagets utgångslön.
        var lonMatch = kandidater.Where(a => a.Manadslon == forslag.NuvarandeLon).ToList();
        if (lonMatch.Count == 1)
            return lonMatch[0];
        if (kandidater.Count == 1)
            return kandidater[0];

        throw new InvalidOperationException(
            $"Kan inte entydigt avgöra vilken anställning för anställd {forslag.AnstallId} " +
            "som ska få ny lön — ange anställning explicit på löneförslaget.");
    }
}

/// <summary>Sammanställning av en genomförd löneöversynsrunda.</summary>
public sealed record SalaryReviewExecutionResult(
    Guid RundaId,
    IReadOnlyList<AppliedSalaryChange> Andringar,
    Money TotalOkning,
    Money TotalRetroaktivt,
    DateOnly IkrafttradandeDatum,
    DateOnly GenomforandeDatum)
{
    public int AntalAnstallda => Andringar.Count;
}

/// <summary>En applicerad löneändring för en enskild anställning.</summary>
public sealed record AppliedSalaryChange(
    EmployeeId AnstallId,
    EmploymentId AnstallningId,
    Money TidigareLon,
    Money NyLon,
    Money Okning,
    int RetroaktivaManader,
    Money RetroaktivtBelopp);
