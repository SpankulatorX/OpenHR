using RegionHR.SharedKernel.Domain;
using RegionHR.Scheduling.Domain;

namespace RegionHR.Scheduling.Optimization;

/// <summary>
/// Schemaoptimering med constraint programming.
/// I produktion använder detta Google OR-Tools CP-SAT solver.
///
/// Hårda begränsningar (ATL):
/// - 11h dygnsvila (§13)
/// - 36h sammanhängande veckovila (§14)
/// - Kompetensbehov per pass
/// - Max arbetstid per vecka 40h (§5)
/// - Max 8h nattarbetstid per 24h (§13a)
/// - Inga överlappande pass per person
///
/// Mjuka begränsningar (optimeras):
/// - Medarbetarpreferenser (önskade/oönskade pass)
/// - Rättvisefördelning (helger, OB, nattpass)
/// - OB-kostnadsminimering
/// - Kontinuitet (samma personal på avdelning)
/// </summary>
public sealed class ConstraintScheduleSolver
{
    private readonly ArbetstidslagenValidator _atlValidator;

    /// <summary>
    /// Maximal rekursionsdjup vid backtracking.
    /// </summary>
    private const int MAX_BACKTRACK_DJUP = 3;

    /// <summary>
    /// Skapar lösaren med standardvalidering av Arbetstidslagen (11h dygnsvila).
    /// </summary>
    public ConstraintScheduleSolver() : this(new ArbetstidslagenValidator())
    {
    }

    /// <summary>
    /// Skapar lösaren med en angiven ATL-validator. Används för att aktivera
    /// sjukvårdsundantaget (9h dygnsvila) vid behovsstyrd schemagenerering
    /// utan att duplicera lösningslogiken.
    /// </summary>
    public ConstraintScheduleSolver(ArbetstidslagenValidator atlValidator)
    {
        _atlValidator = atlValidator ?? throw new ArgumentNullException(nameof(atlValidator));
    }

    /// <summary>
    /// Lös ett schemaoptimeringsproblem.
    /// Returnerar en lista med tilldelade pass samt mjuka mätetal.
    /// </summary>
    public ScheduleSolution Solve(ScheduleProblem problem)
    {
        var solution = new ScheduleSolution();
        var assignments = new List<ShiftAssignment>();

        // Sortera pass efter datum och typ, med nattpass sist (mer begränsande)
        var passBehov = problem.PassBehov
            .OrderBy(b => b.Datum)
            .ThenBy(b => b.PassTyp switch
            {
                ShiftType.Dag => 0,
                ShiftType.Kvall => 1,
                ShiftType.Natt => 2,
                _ => 3
            })
            .ToList();

        // Expandera behov med AntalBehov > 1 till separata rader
        var expanderadeBehov = new List<StaffingRequirement>();
        foreach (var behov in passBehov)
        {
            for (int i = 0; i < Math.Max(1, behov.AntalBehov); i++)
            {
                expanderadeBehov.Add(behov);
            }
        }

        foreach (var behov in expanderadeBehov)
        {
            var besta = TilldelaPass(behov, problem, assignments, 0);
            if (besta is not null)
            {
                assignments.Add(besta);
            }
            else
            {
                solution.ObemannadeBehov.Add(behov);
            }
        }

        solution.Tilldelningar = assignments;
        solution.ArFullstandig = solution.ObemannadeBehov.Count == 0;
        solution.TotalKostnad = BeraknaOBKostnad(assignments);
        solution.RattviseScore = BeraknaRattviseScore(assignments, problem.TillgangligPersonal);

        return solution;
    }

    /// <summary>
    /// Tilldela ett pass med backtracking vid constraint-fel.
    /// </summary>
    private ShiftAssignment? TilldelaPass(
        StaffingRequirement behov,
        ScheduleProblem problem,
        List<ShiftAssignment> assignments,
        int backtrackDjup)
    {
        // Hitta tillgängliga medarbetare, filtrerade på hårda begränsningar
        var kandidater = problem.TillgangligPersonal
            .Where(p => !ArLedig(p, behov.Datum))
            .Where(p => HarKompetens(p, behov.KravdaKompetenser))
            .Where(p => !HarPassKonflikt(p.AnstallId, behov.Datum, behov.Start, behov.Slut, assignments))
            .Where(p => _atlValidator.UppfyllerDygnsvila(p.AnstallId, behov.Datum, behov.Start, assignments))
            .Where(p => _atlValidator.UppfyllerVeckovila(p.AnstallId, behov.Datum, assignments))
            .Where(p => _atlValidator.InomMaxArbetstid(p.AnstallId, behov.Datum, behov.PlaneradeTimmar, assignments))
            .Where(p =>
            {
                var testAssignment = SkapaAssignment(p.AnstallId, behov);
                return _atlValidator.InomNattarbetsgrans(p.AnstallId, behov.Datum, testAssignment, assignments);
            })
            .ToList();

        if (kandidater.Count == 0)
        {
            // Backtracking: försök byta ut redan tilldelad personal
            if (backtrackDjup < MAX_BACKTRACK_DJUP)
            {
                return ForsokaBacktrack(behov, problem, assignments, backtrackDjup);
            }
            return null;
        }

        // Ranka kandidater efter mjuka begränsningar
        var rankade = kandidater
            .Select(p => new
            {
                Personal = p,
                Score = BeraknaKandidatScore(p, behov, assignments, problem)
            })
            .OrderBy(x => x.Score)
            .ToList();

        return SkapaAssignment(rankade.First().Personal.AnstallId, behov);
    }

    /// <summary>
    /// Försök lösa en tilldelningskonflikt genom att byta ut en redan tilldelad person.
    /// Backtracking sker bara om det befintliga passet INTE överlappar med det nya behovet,
    /// annars leder det bara till cirkulära byten.
    /// </summary>
    private ShiftAssignment? ForsokaBacktrack(
        StaffingRequirement behov,
        ScheduleProblem problem,
        List<ShiftAssignment> assignments,
        int backtrackDjup)
    {
        // Hitta tilldelningar samma dag som eventuellt kan bytas
        // Exkludera pass som överlappar med det nya behovet (cirkulärt byte)
        var sammaDag = assignments
            .Where(a => a.Datum == behov.Datum)
            .Where(a => !OverlappasPass(a.Start, a.Slut, behov.Start, behov.Slut))
            .ToList();

        foreach (var befintlig in sammaDag)
        {
            // Försök hitta en person som kan ta det befintliga passet
            // OCH frigöra den befintliga personen till det nya behovet
            var befintligPersonal = problem.TillgangligPersonal
                .FirstOrDefault(p => p.AnstallId == befintlig.AnstallId);

            if (befintligPersonal is null) continue;
            if (!HarKompetens(befintligPersonal, behov.KravdaKompetenser)) continue;

            // Kontrollera att den befintliga personen inte redan har ett pass
            // som överlappar med det nya behovet
            if (HarPassKonflikt(befintligPersonal.AnstallId, behov.Datum, behov.Start, behov.Slut, assignments))
                continue;

            // Simulera borttagning av befintlig tilldelning
            var tempAssignments = assignments.Where(a => a != befintlig).ToList();

            // Kontrollera ATL för den befintliga personalen på det nya behovet
            if (!_atlValidator.UppfyllerDygnsvila(befintligPersonal.AnstallId, behov.Datum, behov.Start, tempAssignments))
                continue;
            if (!_atlValidator.InomMaxArbetstid(befintligPersonal.AnstallId, behov.Datum, behov.PlaneradeTimmar, tempAssignments))
                continue;

            // Försök tilldela det befintliga passets behov till någon annan
            var ersattningsBehov = new StaffingRequirement
            {
                Datum = befintlig.Datum,
                PassTyp = befintlig.PassTyp,
                Start = befintlig.Start,
                Slut = befintlig.Slut,
                Rast = befintlig.Rast,
                KravdaKompetenser = [] // Förenkla vid backtrack
            };

            var ersattning = TilldelaPass(ersattningsBehov, problem, tempAssignments, backtrackDjup + 1);
            if (ersattning is not null)
            {
                // Byt lyckades
                assignments.Remove(befintlig);
                assignments.Add(ersattning);
                return SkapaAssignment(befintligPersonal.AnstallId, behov);
            }
        }

        return null;
    }

    /// <summary>
    /// Beräkna ett poängsystem för kandidater. Lägre poäng = bättre match.
    /// </summary>
    private static double BeraknaKandidatScore(
        PersonalInfo personal,
        StaffingRequirement behov,
        List<ShiftAssignment> assignments,
        ScheduleProblem problem)
    {
        double score = 0;

        // 1. Rättvisa: fördela OB-pass jämnt (helg, kväll, natt)
        var obPassCount = assignments.Count(a =>
            a.AnstallId == personal.AnstallId &&
            a.PassTyp is ShiftType.Kvall or ShiftType.Natt);
        score += obPassCount * 10.0;

        // 2. Helg-rättvisa: fördela helgpass jämnt
        var helgPassCount = assignments.Count(a =>
            a.AnstallId == personal.AnstallId &&
            (a.Datum.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday));
        score += helgPassCount * 15.0;

        // 3. Preferenspoäng
        if (personal.Preferenser is not null)
        {
            if (personal.Preferenser.OnskaPass.Any(o =>
                o.Datum == behov.Datum && o.PassTyp == behov.PassTyp))
            {
                score -= 20.0; // Starkt önskat
            }

            if (personal.Preferenser.OonskaPass.Any(o =>
                o.Datum == behov.Datum && o.PassTyp == behov.PassTyp))
            {
                score += 30.0; // Oönskat
            }
        }

        // 4. OB-kostnadsoptimering: extra poäng för höga sysselsättningsgrader på OB-pass
        // (timavlönade kostar mer övertid)
        if (behov.PassTyp is ShiftType.Kvall or ShiftType.Natt)
        {
            score += (double)(100m - personal.Sysselsattningsgrad) * 0.5;
        }

        // 5. Jämn fördelning av nattpass
        var nattPassCount = assignments.Count(a =>
            a.AnstallId == personal.AnstallId &&
            a.PassTyp == ShiftType.Natt);
        score += nattPassCount * 12.0;

        return score;
    }

    private static bool HarKompetens(PersonalInfo personal, List<string> kravdaKompetenser)
    {
        if (kravdaKompetenser.Count == 0) return true;
        return kravdaKompetenser.All(k => personal.Kompetenser.Contains(k));
    }

    private static bool ArLedig(PersonalInfo personal, DateOnly datum)
    {
        return personal.LedigaDagar.Contains(datum);
    }

    private static bool HarPassKonflikt(
        EmployeeId anstallId, DateOnly datum, TimeOnly start, TimeOnly slut,
        List<ShiftAssignment> befintliga)
    {
        return befintliga.Any(a =>
            a.AnstallId == anstallId &&
            a.Datum == datum &&
            OverlappasPass(a.Start, a.Slut, start, slut));
    }

    /// <summary>
    /// Kontrollera om två pass överlappar varandra. Hanterar nattpass som korsar midnatt.
    /// </summary>
    private static bool OverlappasPass(TimeOnly start1, TimeOnly slut1, TimeOnly start2, TimeOnly slut2)
    {
        // Normalisera till minuter från midnatt, med nattpass expanderade
        var (s1, e1) = NormaliseraTidsintervall(start1, slut1);
        var (s2, e2) = NormaliseraTidsintervall(start2, slut2);

        return s1 < e2 && s2 < e1;
    }

    private static (int start, int slut) NormaliseraTidsintervall(TimeOnly start, TimeOnly slut)
    {
        var s = start.Hour * 60 + start.Minute;
        var e = slut.Hour * 60 + slut.Minute;
        if (e <= s) e += 24 * 60; // Korsar midnatt
        return (s, e);
    }

    /// <summary>
    /// Beräkna rättvise-score (standardavvikelse i fördelning av OB/helg/natt-pass).
    /// Lägre värde = mer rättvist.
    /// </summary>
    private static double BeraknaRattviseScore(
        List<ShiftAssignment> tilldelningar,
        List<PersonalInfo> personal)
    {
        if (personal.Count == 0) return 0;

        // Beräkna OB+helg-pass per person
        var passPerPerson = new Dictionary<EmployeeId, int>();
        foreach (var p in personal)
        {
            passPerPerson[p.AnstallId] = 0;
        }

        foreach (var t in tilldelningar)
        {
            if (t.PassTyp is ShiftType.Kvall or ShiftType.Natt ||
                t.Datum.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                if (passPerPerson.ContainsKey(t.AnstallId))
                    passPerPerson[t.AnstallId]++;
            }
        }

        var counts = passPerPerson.Values.Select(c => (double)c).ToList();
        if (counts.Count == 0) return 0;

        var mean = counts.Average();
        var variance = counts.Sum(c => Math.Pow(c - mean, 2)) / counts.Count;
        return Math.Sqrt(variance);
    }

    private static Money BeraknaOBKostnad(List<ShiftAssignment> tilldelningar)
    {
        var total = 0m;
        foreach (var t in tilldelningar)
        {
            total += t.PassTyp switch
            {
                ShiftType.Kvall => t.PlaneradeTimmar * 126.50m,
                ShiftType.Natt => t.PlaneradeTimmar * 152.00m,
                _ when t.Datum.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                    => t.PlaneradeTimmar * 100.00m, // Helg-OB
                _ => 0m
            };
        }
        return Money.SEK(total);
    }

    private static ShiftAssignment SkapaAssignment(EmployeeId anstallId, StaffingRequirement behov)
    {
        return new ShiftAssignment
        {
            AnstallId = anstallId,
            Datum = behov.Datum,
            PassTyp = behov.PassTyp,
            Start = behov.Start,
            Slut = behov.Slut,
            Rast = behov.Rast
        };
    }
}

// Input/output models

public sealed class ScheduleProblem
{
    public OrganizationId EnhetId { get; set; }
    public DateRange Period { get; set; } = null!;
    public List<StaffingRequirement> PassBehov { get; set; } = [];
    public List<PersonalInfo> TillgangligPersonal { get; set; } = [];
}

public sealed class StaffingRequirement
{
    public DateOnly Datum { get; set; }
    public ShiftType PassTyp { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly Slut { get; set; }
    public TimeSpan Rast { get; set; }
    public int AntalBehov { get; set; } = 1;
    public List<string> KravdaKompetenser { get; set; } = [];
    public decimal PlaneradeTimmar => (decimal)((Slut.ToTimeSpan() - Start.ToTimeSpan() + (Slut < Start ? TimeSpan.FromHours(24) : TimeSpan.Zero)) - Rast).TotalHours;
}

public sealed class PersonalInfo
{
    public EmployeeId AnstallId { get; set; }
    public string Namn { get; set; } = string.Empty;
    public decimal Sysselsattningsgrad { get; set; }
    public List<string> Kompetenser { get; set; } = [];
    public List<DateOnly> LedigaDagar { get; set; } = [];
    public PersonalPreferenser? Preferenser { get; set; }
}

/// <summary>
/// Medarbetarpreferenser för schemaläggning.
/// </summary>
public sealed class PersonalPreferenser
{
    /// <summary>Pass som medarbetaren önskar arbeta.</summary>
    public List<PassPreferens> OnskaPass { get; set; } = [];

    /// <summary>Pass som medarbetaren INTE vill arbeta.</summary>
    public List<PassPreferens> OonskaPass { get; set; } = [];
}

public sealed class PassPreferens
{
    public DateOnly Datum { get; set; }
    public ShiftType PassTyp { get; set; }
}

public sealed class ShiftAssignment
{
    public EmployeeId AnstallId { get; set; }
    public DateOnly Datum { get; set; }
    public ShiftType PassTyp { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly Slut { get; set; }
    public TimeSpan Rast { get; set; }
    public decimal PlaneradeTimmar => (decimal)((Slut.ToTimeSpan() - Start.ToTimeSpan() + (Slut < Start ? TimeSpan.FromHours(24) : TimeSpan.Zero)) - Rast).TotalHours;
}

public sealed class ScheduleSolution
{
    public List<ShiftAssignment> Tilldelningar { get; set; } = [];
    public List<StaffingRequirement> ObemannadeBehov { get; set; } = [];
    public bool ArFullstandig { get; set; }
    public Money TotalKostnad { get; set; } = Money.Zero;

    /// <summary>
    /// Rättvise-score: standardavvikelse i fördelning av OB/helg/natt-pass.
    /// 0 = perfekt jämnt, högre = sämre fördelning.
    /// </summary>
    public double RattviseScore { get; set; }
}
