using RegionHR.Scheduling.Domain;
using RegionHR.Scheduling.Optimization;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Scheduling;

public class SchemaOptimizer
{
    private readonly ArbetstidslagenValidator _atl = new();

    public SchemaForslag Optimera(SchemaRequest request)
    {
        var forslag = new List<PassTilldelning>();
        var personal = request.TillgangligPersonal.ToList();
        var passIndex = 0;

        foreach (var dag in EachDay(request.Period))
        {
            foreach (var pass in request.PassTyper)
            {
                var antal = pass.AntalPersoner;
                for (int i = 0; i < antal; i++)
                {
                    var person = personal[passIndex % personal.Count];
                    forslag.Add(new PassTilldelning(person, dag, pass.Namn, pass.Start, pass.Slut));
                    passIndex++;
                }
            }
        }

        // Calculate metrics
        var timmarPerPerson = forslag.GroupBy(f => f.PersonNamn)
            .ToDictionary(g => g.Key, g => g.Sum(p => (p.Slut - p.Start).TotalHours));
        var maxTimmar = timmarPerPerson.Values.Max();
        var minTimmar = timmarPerPerson.Values.Min();

        return new SchemaForslag(
            Tilldelningar: forslag,
            TotalPass: forslag.Count,
            ObemannadeDagar: 0,
            BalansIndex: Math.Round(minTimmar / maxTimmar * 100, 1),
            ViloRegelBrott: RaknaViloRegelBrott(forslag, request.Period));
    }

    /// <summary>
    /// Räkna faktiska vilotidsbrott (ATL §13 dygnsvila + §14 veckovila) i förslaget
    /// genom att köra <see cref="ArbetstidslagenValidator"/> över tilldelningarna.
    /// Round-robin-tilldelningen tar ingen hänsyn till vila, så detta ger ett ärligt
    /// mått i stället för det tidigare hårdkodade 0-värdet.
    /// </summary>
    private int RaknaViloRegelBrott(
        List<PassTilldelning> forslag,
        (DateOnly Start, DateOnly End) period)
    {
        if (forslag.Count == 0) return 0;

        // Stabil, deterministisk anställnings-id per personnamn så att alla pass för
        // samma person grupperas ihop i validatorn.
        var idPerNamn = new Dictionary<string, EmployeeId>();
        var assignments = new List<ShiftAssignment>();

        foreach (var t in forslag)
        {
            if (!idPerNamn.TryGetValue(t.PersonNamn, out var id))
            {
                id = EmployeeId.New();
                idPerNamn[t.PersonNamn] = id;
            }

            assignments.Add(new ShiftAssignment
            {
                AnstallId = id,
                Datum = t.Dag,
                PassTyp = HarledPassTyp(t.Start, t.Slut),
                Start = TimeOnly.FromTimeSpan(t.Start),
                Slut = TimeOnly.FromTimeSpan(t.Slut),
                Rast = TimeSpan.Zero
            });
        }

        var resultat = _atl.ValidateSchedule(assignments, new DateRange(period.Start, period.End));

        // Endast vilotidsregler räknas som "vilobrott" (dygns- och veckovila).
        return resultat.Overtraldelser.Count(v =>
            v.Regel.Contains("Dygnsvila", StringComparison.OrdinalIgnoreCase) ||
            v.Regel.Contains("Veckovila", StringComparison.OrdinalIgnoreCase));
    }

    private static ShiftType HarledPassTyp(TimeSpan start, TimeSpan slut)
    {
        // Nattpass: börjar 21:00 eller senare, slutar 07:00 eller tidigare, eller korsar midnatt.
        if (start >= TimeSpan.FromHours(21) || slut <= TimeSpan.FromHours(7) || slut <= start)
            return ShiftType.Natt;
        if (start >= TimeSpan.FromHours(14))
            return ShiftType.Kvall;
        return ShiftType.Dag;
    }

    private static IEnumerable<DateOnly> EachDay((DateOnly Start, DateOnly End) period)
    {
        for (var d = period.Start; d <= period.End; d = d.AddDays(1))
            yield return d;
    }
}

public record SchemaRequest(
    (DateOnly Start, DateOnly End) Period,
    List<string> TillgangligPersonal,
    List<PassTyp> PassTyper);

public record PassTyp(string Namn, TimeSpan Start, TimeSpan Slut, int AntalPersoner);
public record PassTilldelning(string PersonNamn, DateOnly Dag, string PassTyp, TimeSpan Start, TimeSpan Slut);
public record SchemaForslag(
    List<PassTilldelning> Tilldelningar, int TotalPass,
    int ObemannadeDagar, double BalansIndex, int ViloRegelBrott);
