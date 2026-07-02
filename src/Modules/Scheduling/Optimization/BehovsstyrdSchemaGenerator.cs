using RegionHR.SharedKernel.Domain;
using RegionHR.Scheduling.Domain;

namespace RegionHR.Scheduling.Optimization;

/// <summary>
/// Behovsstyrd schemaautomatik för dygnet-runt-vård.
///
/// Tar bemanningsbehov från en <see cref="StaffingTemplate"/> (min/optimal antal per
/// veckodag, passtyp, tid och kompetens) och auto-genererar ett schemaförslag som täcker
/// behovet över en konkret period. Själva tilldelningen och de hårda ATL-begränsningarna
/// (dygnsvila 11h/9h vård §13, veckovila 36h §14, veckoarbetstid §5, nattarbetstid §13a)
/// samt rättvisefördelningen delegeras till <see cref="ConstraintScheduleSolver"/> —
/// denna klass duplicerar inte lösaren utan expanderar behovsmallen och rapporterar
/// täckningsgrad (under-/överbemanning) per pass.
///
/// Eftersom lösaren aldrig bryter mot en hård ATL-begränsning lämnar den hellre ett pass
/// obemannat än schemalägger olagligt. Därför är gapen i täckningen ärliga: de speglar
/// bemanningsbrist, inte regelbrott. Det genererade schemat valideras dessutom mot ATL i
/// efterhand så att eventuella varningar synliggörs i stället för att döljas.
/// </summary>
public sealed class BehovsstyrdSchemaGenerator
{
    /// <summary>
    /// Generera ett behovsstyrt schemaförslag för perioden.
    /// </summary>
    public SchemaForslag Generera(BehovsstyrdSchemaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var period = request.Period;
        if (period.End is null)
            throw new ArgumentException(
                "Perioden måste ha ett slutdatum för behovsstyrd generering.", nameof(request));

        var slutDatum = period.End.Value;

        // 1. Expandera bemanningsmallens rader till konkreta passinstanser för varje
        //    datum i perioden vars veckodag matchar raden.
        var passInstanser = new List<PassInstans>();
        for (var d = period.Start; d <= slutDatum; d = d.AddDays(1))
        {
            foreach (var rad in request.Behovsrader.Where(r => r.Veckodag == d.DayOfWeek))
            {
                passInstanser.Add(new PassInstans(d, rad));
            }
        }

        // 2. Bygg lösarens behov. Målbemanning styr hur många som eftersträvas per pass:
        //    Optimal = full önskad bemanning, Minimum = enbart lägsta godtagbara nivå.
        var behov = passInstanser.Select(pi => new StaffingRequirement
        {
            Datum = pi.Datum,
            PassTyp = pi.Rad.PassTyp,
            Start = pi.Rad.Start,
            Slut = pi.Rad.Slut,
            Rast = pi.Rad.Rast,
            AntalBehov = request.Mal == BemanningsMal.Optimal ? pi.Rad.OptimalAntal : pi.Rad.MinAntal,
            KravdaKompetenser = [.. pi.Rad.KravdaKompetenser]
        }).ToList();

        var problem = new ScheduleProblem
        {
            EnhetId = request.EnhetId,
            Period = period,
            PassBehov = behov,
            TillgangligPersonal = [.. request.TillgangligPersonal]
        };

        // 3. Kör constraint-lösaren med rätt ATL-profil (sjukvård 9h eller standard 11h).
        var atlValidator = new ArbetstidslagenValidator(request.ArSjukvard);
        var solver = new ConstraintScheduleSolver(atlValidator);
        var solution = solver.Solve(problem);

        // 4. Beräkna täckning per pass-slot. Rader som råkar dela exakt samma slot
        //    (datum/passtyp/tid) aggregeras så att tillsatt antal inte dubbelräknas.
        var tackning = passInstanser
            .GroupBy(pi => new SlotNyckel(pi.Datum, pi.Rad.PassTyp, pi.Rad.Start, pi.Rad.Slut))
            .Select(g =>
            {
                var tillsatt = solution.Tilldelningar.Count(a =>
                    a.Datum == g.Key.Datum &&
                    a.PassTyp == g.Key.PassTyp &&
                    a.Start == g.Key.Start &&
                    a.Slut == g.Key.Slut);

                return new PassTackning
                {
                    Datum = g.Key.Datum,
                    PassTyp = g.Key.PassTyp,
                    Start = g.Key.Start,
                    Slut = g.Key.Slut,
                    MinBehov = g.Sum(x => x.Rad.MinAntal),
                    OptimalBehov = g.Sum(x => x.Rad.OptimalAntal),
                    Tillsatt = tillsatt,
                    KravdaKompetenser = g.SelectMany(x => x.Rad.KravdaKompetenser)
                        .Distinct()
                        .ToList()
                };
            })
            .OrderBy(t => t.Datum)
            .ThenBy(t => t.Start)
            .ToList();

        // 5. Validera det genererade schemat mot ATL. Normalt tomt eftersom lösaren
        //    respekterar de hårda begränsningarna, men surfas ärligt om något slinker igenom.
        var atlResultat = atlValidator.ValidateSchedule(solution.Tilldelningar, period);

        var totaltMinBehov = tackning.Sum(t => t.MinBehov);
        var totaltTillsattMotMin = tackning.Sum(t => Math.Min(t.Tillsatt, t.MinBehov));
        var tackningsgrad = totaltMinBehov == 0
            ? 100m
            : Math.Round((decimal)totaltTillsattMotMin / totaltMinBehov * 100m, 1);

        return new SchemaForslag
        {
            EnhetId = request.EnhetId,
            Period = period,
            ArSjukvard = request.ArSjukvard,
            Mal = request.Mal,
            Tilldelningar = solution.Tilldelningar,
            Tackning = tackning,
            ATLVarningar = [.. atlResultat.Overtraldelser],
            TotalOBKostnad = solution.TotalKostnad,
            RattviseScore = solution.RattviseScore,
            TotaltMinBehov = totaltMinBehov,
            TotaltTillsatt = tackning.Sum(t => t.Tillsatt),
            TackningsgradProcent = tackningsgrad
        };
    }

    private readonly record struct PassInstans(DateOnly Datum, StaffingRequirementLine Rad);

    private readonly record struct SlotNyckel(DateOnly Datum, ShiftType PassTyp, TimeOnly Start, TimeOnly Slut);
}

/// <summary>
/// Målbemanning för genereringen.
/// </summary>
public enum BemanningsMal
{
    /// <summary>Eftersträva enbart lägsta godtagbara bemanning (MinAntal).</summary>
    Minimum,

    /// <summary>Eftersträva full önskad bemanning (OptimalAntal).</summary>
    Optimal
}

/// <summary>
/// Bemanningsläge för ett enskilt pass i förhållande till behovet.
/// </summary>
public enum BemanningsLage
{
    /// <summary>Färre tillsatta än minimikravet.</summary>
    Underbemannad,

    /// <summary>Minst minimikravet men högst optimalnivån.</summary>
    Balanserad,

    /// <summary>Fler tillsatta än optimalnivån.</summary>
    Overbemannad
}

/// <summary>
/// Indata till <see cref="BehovsstyrdSchemaGenerator"/>.
/// </summary>
public sealed class BehovsstyrdSchemaRequest
{
    /// <summary>Enhet som schemat gäller.</summary>
    public required OrganizationId EnhetId { get; init; }

    /// <summary>Perioden som ska schemaläggas (måste ha ett slutdatum).</summary>
    public required DateRange Period { get; init; }

    /// <summary>Bemanningsbehovets rader (typiskt från en <see cref="StaffingTemplate"/>).</summary>
    public required IReadOnlyList<StaffingRequirementLine> Behovsrader { get; init; }

    /// <summary>Tillgänglig personal som kan tilldelas pass.</summary>
    public required IReadOnlyList<PersonalInfo> TillgangligPersonal { get; init; }

    /// <summary>Om sjukvårdsundantaget (9h dygnsvila) ska tillämpas i stället för 11h.</summary>
    public bool ArSjukvard { get; init; }

    /// <summary>Målbemanning: sträva mot optimal eller enbart minimibemanning.</summary>
    public BemanningsMal Mal { get; init; } = BemanningsMal.Optimal;

    /// <summary>
    /// Bygg en request direkt från en bemanningsmall.
    /// </summary>
    public static BehovsstyrdSchemaRequest FranMall(
        StaffingTemplate mall,
        DateRange period,
        IReadOnlyList<PersonalInfo> tillgangligPersonal,
        bool arSjukvard = false,
        BemanningsMal mal = BemanningsMal.Optimal)
    {
        ArgumentNullException.ThrowIfNull(mall);
        ArgumentNullException.ThrowIfNull(tillgangligPersonal);

        return new BehovsstyrdSchemaRequest
        {
            EnhetId = mall.EnhetId,
            Period = period,
            Behovsrader = mall.Rader,
            TillgangligPersonal = tillgangligPersonal,
            ArSjukvard = arSjukvard,
            Mal = mal
        };
    }
}

/// <summary>
/// Täckningsstatus för ett enskilt pass (en slot: datum + passtyp + tid).
/// </summary>
public sealed class PassTackning
{
    /// <summary>Datum för passet.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>Passtyp (dag/kväll/natt m.m.).</summary>
    public ShiftType PassTyp { get; init; }

    /// <summary>Passets starttid.</summary>
    public TimeOnly Start { get; init; }

    /// <summary>Passets sluttid.</summary>
    public TimeOnly Slut { get; init; }

    /// <summary>Lägsta godtagbara bemanning för passet.</summary>
    public int MinBehov { get; init; }

    /// <summary>Önskad (optimal) bemanning för passet.</summary>
    public int OptimalBehov { get; init; }

    /// <summary>Antal faktiskt tilldelade i det genererade förslaget.</summary>
    public int Tillsatt { get; init; }

    /// <summary>Kompetenser som passet kräver.</summary>
    public IReadOnlyList<string> KravdaKompetenser { get; init; } = [];

    /// <summary>Antal som saknas för att nå minimibemanning (0 om täckt).</summary>
    public int UnderskottMotMin => Math.Max(0, MinBehov - Tillsatt);

    /// <summary>Antal utöver optimalnivån (0 om inom mål).</summary>
    public int OverskottMotOptimal => Math.Max(0, Tillsatt - OptimalBehov);

    /// <summary>Bemanningsläge i förhållande till behovet.</summary>
    public BemanningsLage Lage =>
        Tillsatt < MinBehov ? BemanningsLage.Underbemannad
        : Tillsatt > OptimalBehov ? BemanningsLage.Overbemannad
        : BemanningsLage.Balanserad;

    /// <summary>Täckningsgrad mot minimibemanning (0–1, kapad till 1).</summary>
    public decimal TackningsgradMotMin => MinBehov == 0 ? 1m : Math.Min(1m, (decimal)Tillsatt / MinBehov);
}

/// <summary>
/// Resultatet av en behovsstyrd generering.
/// </summary>
public sealed class SchemaForslag
{
    /// <summary>Enhet som förslaget gäller.</summary>
    public OrganizationId EnhetId { get; init; }

    /// <summary>Perioden som schemalagts.</summary>
    public DateRange Period { get; init; } = null!;

    /// <summary>Om sjukvårdsundantaget (9h dygnsvila) tillämpades.</summary>
    public bool ArSjukvard { get; init; }

    /// <summary>Målbemanningen som användes.</summary>
    public BemanningsMal Mal { get; init; }

    /// <summary>Genererade passtilldelningar.</summary>
    public IReadOnlyList<ShiftAssignment> Tilldelningar { get; init; } = [];

    /// <summary>Täckningsstatus per pass.</summary>
    public IReadOnlyList<PassTackning> Tackning { get; init; } = [];

    /// <summary>ATL-varningar för det genererade schemat (tomt = fullt lagenligt).</summary>
    public IReadOnlyList<ValidationViolation> ATLVarningar { get; init; } = [];

    /// <summary>Total OB-kostnad för de genererade passen.</summary>
    public Money TotalOBKostnad { get; init; } = Money.Zero;

    /// <summary>Rättvise-score (standardavvikelse i OB/helg/natt-fördelning; lägre = jämnare).</summary>
    public double RattviseScore { get; init; }

    /// <summary>Summerat minimibehov över alla pass.</summary>
    public int TotaltMinBehov { get; init; }

    /// <summary>Summa faktiskt tillsatta över alla pass.</summary>
    public int TotaltTillsatt { get; init; }

    /// <summary>Sammanvägd täckningsgrad mot minimibemanning i procent.</summary>
    public decimal TackningsgradProcent { get; init; }

    /// <summary>Är det genererade schemat helt ATL-kompliant?</summary>
    public bool ATLKompliant => ATLVarningar.Count == 0;

    /// <summary>Underbemannade pass.</summary>
    public IReadOnlyList<PassTackning> Underbemannade =>
        Tackning.Where(t => t.Lage == BemanningsLage.Underbemannad).ToList();

    /// <summary>Överbemannade pass.</summary>
    public IReadOnlyList<PassTackning> Overbemannade =>
        Tackning.Where(t => t.Lage == BemanningsLage.Overbemannad).ToList();

    /// <summary>True om minimibemanningen är uppfylld för samtliga pass.</summary>
    public bool FullTackning => Underbemannade.Count == 0;
}
