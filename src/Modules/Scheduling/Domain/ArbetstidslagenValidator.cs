using RegionHR.SharedKernel.Domain;
using RegionHR.Scheduling.Optimization;

namespace RegionHR.Scheduling.Domain;

/// <summary>
/// Validerar pass mot Arbetstidslagen (1982:673) och EU:s arbetstidsdirektiv.
/// Hanterar nattpass (pass som korsar midnatt) korrekt genom att beräkna
/// faktisk vila som sammanhängande timmar, inte enbart tid mellan passtart.
/// </summary>
public sealed class ArbetstidslagenValidator
{
    // Hårda begränsningar per ATL
    /// <summary>§13: Minst 11 timmars sammanhängande vila per 24-timmarsperiod.</summary>
    public const int MIN_DYGNSVILA_TIMMAR = 11;

    /// <summary>
    /// Undantag för sjukvård: minst 9 timmars sammanhängande vila per 24-timmarsperiod.
    /// Per SKR/fackligt avtal (okt 2023) för hälso- och sjukvårdspersonal.
    /// </summary>
    public const int MIN_DYGNSVILA_SJUKVARD = 9;

    /// <summary>
    /// Anger om validatorn tillämpar sjukvårdsundantaget för dygnsvila (9h istället för 11h).
    /// </summary>
    public bool ArSjukvard { get; }

    /// <summary>
    /// Faktiskt dygnsvila-minimum baserat på om sjukvårdsundantag tillämpas.
    /// </summary>
    public int EffektivtMinDygnsvila => ArSjukvard ? MIN_DYGNSVILA_SJUKVARD : MIN_DYGNSVILA_TIMMAR;

    public ArbetstidslagenValidator() : this(arSjukvard: false) { }

    public ArbetstidslagenValidator(bool arSjukvard)
    {
        ArSjukvard = arSjukvard;
    }

    /// <summary>§14: Minst 36 timmars sammanhängande vila per sjudagarsperiod.</summary>
    public const int MIN_VECKOVILA_TIMMAR = 36;

    /// <summary>§5: Högst 40 timmars ordinarie arbetstid per vecka.</summary>
    public const decimal MAX_ORDINARIE_VECKOARBETSTID = 40m;

    /// <summary>§8: Högst 48 timmars övertid per fyraveckorsperiod.</summary>
    public const decimal MAX_OVERTID_PER_4_VECKOR = 48m;

    /// <summary>§8: Högst 200 timmars övertid per kalenderår.</summary>
    public const decimal MAX_OVERTID_PER_AR = 200m;

    /// <summary>§13a: Högst 8 timmars nattarbetstid per 24-timmarsperiod.</summary>
    public const decimal MAX_NATTARBETSTID_PER_24H = 8m;

    /// <summary>EU:s arbetstidsdirektiv: Högst 48 timmars total arbetstid per vecka i genomsnitt över 4 månader.</summary>
    public const decimal MAX_TOTAL_ARBETSTID_PER_VECKA = 48m;

    /// <summary>
    /// Validera ett enskilt nytt pass mot befintliga tilldelningar för en anställd.
    /// </summary>
    public ValidationResult ValidateShift(
        EmployeeId anstallId,
        ShiftAssignment newShift,
        IReadOnlyList<ShiftAssignment> existing)
    {
        var violations = new List<ValidationViolation>();

        var minDygnsvila = EffektivtMinDygnsvila;
        if (!UppfyllerDygnsvila(anstallId, newShift.Datum, newShift.Start, existing))
        {
            violations.Add(new ValidationViolation(
                "ATL §13 Dygnsvila",
                $"Dygnsvilan understiger {minDygnsvila} timmar. Det krävs minst {minDygnsvila} timmars sammanhängande vila per 24-timmarsperiod.{(ArSjukvard ? " (Sjukvårdsundantag tillämpat)" : "")}",
                ViolationSeverity.Hard));
        }

        // Kontrollera dygnsvila framåt: om det redan finns pass dagen efter,
        // säkerställ att det nya passets slut ger tillräcklig vila.
        if (!UppfyllerDygnsvilaFramat(anstallId, newShift, existing))
        {
            violations.Add(new ValidationViolation(
                "ATL §13 Dygnsvila (framåt)",
                $"Det nya passet ger otillräcklig vila före nästa redan planerade pass. Minst {minDygnsvila} timmars sammanhängande dygnsvila krävs.{(ArSjukvard ? " (Sjukvårdsundantag tillämpat)" : "")}",
                ViolationSeverity.Hard));
        }

        if (!UppfyllerVeckovila(anstallId, newShift.Datum, existing))
        {
            violations.Add(new ValidationViolation(
                "ATL §14 Veckovila",
                $"Veckovilan understiger {MIN_VECKOVILA_TIMMAR} timmar. Det krävs minst {MIN_VECKOVILA_TIMMAR} timmars sammanhängande vila per sjudagarsperiod.",
                ViolationSeverity.Hard));
        }

        if (!InomMaxArbetstid(anstallId, newShift.Datum, newShift.PlaneradeTimmar, existing))
        {
            violations.Add(new ValidationViolation(
                "ATL §5 Max veckoarbetstid",
                $"Veckoarbetstiden överstiger {MAX_ORDINARIE_VECKOARBETSTID} timmar. Ordinarie arbetstid får högst uppgå till {MAX_ORDINARIE_VECKOARBETSTID} timmar per vecka.",
                ViolationSeverity.Hard));
        }

        if (!InomNattarbetsgrans(anstallId, newShift.Datum, newShift, existing))
        {
            violations.Add(new ValidationViolation(
                "ATL §13a Nattarbetstid",
                $"Nattarbetstiden överstiger {MAX_NATTARBETSTID_PER_24H} timmar per 24-timmarsperiod. Nattarbetare får arbeta högst {MAX_NATTARBETSTID_PER_24H} timmar per dygn.",
                ViolationSeverity.Hard));
        }

        return new ValidationResult(violations);
    }

    /// <summary>
    /// Mappa ett persisterat <see cref="ScheduledShift"/> till en <see cref="ShiftAssignment"/>
    /// som ATL-validatorn arbetar med. Använder planerad tid (inte faktisk instämplad).
    /// </summary>
    public static ShiftAssignment TillShiftAssignment(ScheduledShift pass) => new()
    {
        AnstallId = pass.AnstallId,
        Datum = pass.Datum,
        PassTyp = pass.PassTyp,
        Start = pass.PlaneradStart,
        Slut = pass.PlaneradSlut,
        Rast = pass.Rast
    };

    /// <summary>
    /// Validera ett nytt manuellt inlagt pass mot den anställdes redan planerade pass.
    /// Bekvämlighetsmetod som UI:t använder när ett pass läggs till i schemagriden:
    /// mappar <see cref="ScheduledShift"/> → <see cref="ShiftAssignment"/> och kör
    /// <see cref="ValidateShift"/> (dygnsvila §13, veckovila §14, veckoarbetstid §5,
    /// nattarbetstid §13a). Passet med samma Id filtreras bort så att ett pass inte
    /// jämförs mot sig självt.
    /// </summary>
    public ValidationResult ValidateNyttPass(
        ScheduledShift nyttPass,
        IEnumerable<ScheduledShift> befintligaPass)
    {
        var existing = befintligaPass
            .Where(p => p.AnstallId == nyttPass.AnstallId && p.Id != nyttPass.Id)
            .Select(TillShiftAssignment)
            .ToList();

        return ValidateShift(nyttPass.AnstallId, TillShiftAssignment(nyttPass), existing);
    }

    /// <summary>
    /// Validera hela schemat för en period. Kontrollerar alla anställda.
    /// </summary>
    public ValidationResult ValidateSchedule(
        IReadOnlyList<ShiftAssignment> assignments,
        DateRange period)
    {
        var violations = new List<ValidationViolation>();
        var perEmployee = assignments.GroupBy(a => a.AnstallId);

        foreach (var group in perEmployee)
        {
            var anstallId = group.Key;
            var employeeAssignments = group.OrderBy(a => a.Datum).ThenBy(a => a.Start).ToList();

            // Kontrollera dygnsvila mellan alla på varandra följande pass
            for (int i = 1; i < employeeAssignments.Count; i++)
            {
                var prev = employeeAssignments[i - 1];
                var curr = employeeAssignments[i];

                var vilaTimmar = BeraknaVilaTimmar(prev, curr);
                if (vilaTimmar < EffektivtMinDygnsvila && vilaTimmar >= 0)
                {
                    violations.Add(new ValidationViolation(
                        "ATL §13 Dygnsvila",
                        $"Anställd {anstallId}: Otillräcklig dygnsvila ({vilaTimmar:F1}h) mellan pass {prev.Datum} {prev.Slut} och {curr.Datum} {curr.Start}. Minst {EffektivtMinDygnsvila}h krävs.{(ArSjukvard ? " (Sjukvårdsundantag tillämpat)" : "")}",
                        ViolationSeverity.Hard));
                }
            }

            // Kontrollera veckoarbetstid för varje vecka i perioden
            var startDatum = period.Start;
            var slutDatum = period.End ?? period.Start.AddDays(28);

            var veckoStart = startDatum.AddDays(-(int)startDatum.DayOfWeek + (startDatum.DayOfWeek == DayOfWeek.Sunday ? -6 : 1));
            while (veckoStart <= slutDatum)
            {
                var veckoSlut = veckoStart.AddDays(7);
                var veckoTimmar = employeeAssignments
                    .Where(a => a.Datum >= veckoStart && a.Datum < veckoSlut)
                    .Sum(a => a.PlaneradeTimmar);

                if (veckoTimmar > MAX_ORDINARIE_VECKOARBETSTID)
                {
                    violations.Add(new ValidationViolation(
                        "ATL §5 Max veckoarbetstid",
                        $"Anställd {anstallId}: Veckan {veckoStart:yyyy-MM-dd} har {veckoTimmar:F1}h arbetstid, max {MAX_ORDINARIE_VECKOARBETSTID}h tillåtet.",
                        ViolationSeverity.Hard));
                }

                veckoStart = veckoSlut;
            }

            // Kontrollera veckovila: 36h sammanhängande vila per 7-dagarsperiod
            veckoStart = startDatum.AddDays(-(int)startDatum.DayOfWeek + (startDatum.DayOfWeek == DayOfWeek.Sunday ? -6 : 1));
            while (veckoStart <= slutDatum)
            {
                var veckoSlut = veckoStart.AddDays(7);
                var passIVeckan = employeeAssignments
                    .Where(a => a.Datum >= veckoStart && a.Datum < veckoSlut)
                    .OrderBy(a => a.Datum).ThenBy(a => a.Start)
                    .ToList();

                if (passIVeckan.Count > 0 && !HarTillrackligVeckovila(passIVeckan, veckoStart, veckoSlut))
                {
                    violations.Add(new ValidationViolation(
                        "ATL §14 Veckovila",
                        $"Anställd {anstallId}: Otillräcklig veckovila under veckan {veckoStart:yyyy-MM-dd}. Minst {MIN_VECKOVILA_TIMMAR}h sammanhängande vila krävs per sjudagarsperiod.",
                        ViolationSeverity.Hard));
                }

                veckoStart = veckoSlut;
            }

            // Kontrollera nattarbetstid (§13a: total arbetstid per 24h för nattarbetare)
            var nattPass = employeeAssignments.Where(a => ArNattpass(a)).ToList();
            foreach (var natt in nattPass)
            {
                if (natt.PlaneradeTimmar > MAX_NATTARBETSTID_PER_24H)
                {
                    violations.Add(new ValidationViolation(
                        "ATL §13a Nattarbetstid",
                        $"Anställd {anstallId}: Nattarbetarens arbetstid {natt.PlaneradeTimmar:F1}h den {natt.Datum:yyyy-MM-dd} överstiger max {MAX_NATTARBETSTID_PER_24H}h per 24-timmarsperiod.",
                        ViolationSeverity.Hard));
                }
            }
        }

        return new ValidationResult(violations);
    }

    /// <summary>
    /// Kontrollera att det finns minst 11 timmars sammanhängande dygnsvila
    /// bakåt i tiden (från föregående pass till det nya passets start).
    /// Hanterar nattpass som korsar midnatt korrekt.
    /// </summary>
    public bool UppfyllerDygnsvila(
        EmployeeId anstallId,
        DateOnly datum,
        TimeOnly start,
        IReadOnlyList<ShiftAssignment> existing)
    {
        // Hitta det senaste passet för personen som slutar innan det nya passet börjar.
        // Vi letar i ett fönster av 2 dagar bakåt för att fånga nattpass som korsar midnatt.
        var tidigastDatum = datum.AddDays(-2);
        var nyPassStartDT = datum.ToDateTime(start);

        var foregaendePass = existing
            .Where(a => a.AnstallId == anstallId && a.Datum >= tidigastDatum && a.Datum <= datum)
            .Select(a => new
            {
                Assignment = a,
                SlutDateTime = BeraknaSlutDateTime(a)
            })
            .Where(a => a.SlutDateTime <= nyPassStartDT)
            .OrderByDescending(a => a.SlutDateTime)
            .FirstOrDefault();

        if (foregaendePass is null) return true;

        var vilaTimmar = (nyPassStartDT - foregaendePass.SlutDateTime).TotalHours;
        return vilaTimmar >= EffektivtMinDygnsvila;
    }

    /// <summary>
    /// Kontrollera veckovila: minst 36 timmars sammanhängande vila per 7-dagarsperiod.
    /// </summary>
    public bool UppfyllerVeckovila(
        EmployeeId anstallId,
        DateOnly datum,
        IReadOnlyList<ShiftAssignment> existing)
    {
        // Beräkna veckans start (måndag)
        var veckoStart = datum.AddDays(-(int)datum.DayOfWeek + (datum.DayOfWeek == DayOfWeek.Sunday ? -6 : 1));
        var veckoSlut = veckoStart.AddDays(7);

        var passIVeckan = existing
            .Where(a => a.AnstallId == anstallId && a.Datum >= veckoStart && a.Datum < veckoSlut)
            .OrderBy(a => a.Datum).ThenBy(a => a.Start)
            .ToList();

        // Om 6 eller fler pass i veckan kontrolleras att det finns en lucka på >= 36h
        if (passIVeckan.Count < 6) return true;

        return HarTillrackligVeckovila(passIVeckan, veckoStart, veckoSlut);
    }

    /// <summary>
    /// Kontrollera att veckoarbetstiden inte överstiger 40 timmar.
    /// </summary>
    public bool InomMaxArbetstid(
        EmployeeId anstallId,
        DateOnly datum,
        decimal timmar,
        IReadOnlyList<ShiftAssignment> existing)
    {
        var veckoStart = datum.AddDays(-(int)datum.DayOfWeek + (datum.DayOfWeek == DayOfWeek.Sunday ? -6 : 1));
        var veckoSlut = veckoStart.AddDays(7);

        var veckoTimmar = existing
            .Where(a => a.AnstallId == anstallId &&
                       a.Datum >= veckoStart &&
                       a.Datum < veckoSlut)
            .Sum(a => a.PlaneradeTimmar);

        return veckoTimmar + timmar <= MAX_ORDINARIE_VECKOARBETSTID;
    }

    /// <summary>
    /// Kontrollera nattarbetsgräns: §13a högst 8 timmar per 24-timmarsperiod
    /// för arbetstagare som arbetar natt (kl 22:00-06:00).
    /// Enligt ATL §13a gäller begränsningen den totala arbetstiden per 24-timmarsperiod
    /// för den som klassas som nattarbetare (arbetar under nattperioden 22:00-06:00).
    /// </summary>
    public bool InomNattarbetsgrans(
        EmployeeId anstallId,
        DateOnly datum,
        ShiftAssignment shift,
        IReadOnlyList<ShiftAssignment> existing)
    {
        if (!ArNattpass(shift)) return true;

        // §13a: Nattarbetares arbetstid får uppgå till i genomsnitt 8h per 24-timmarsperiod.
        // Vi kontrollerar den totala arbetstiden (exkl rast) för passet.
        return shift.PlaneradeTimmar <= MAX_NATTARBETSTID_PER_24H;
    }

    /// <summary>
    /// Avgör om ett pass räknas som nattarbete (arbetstid faller inom 22:00-06:00).
    /// </summary>
    private static bool ArNattpass(ShiftAssignment shift)
    {
        var nattStart = new TimeOnly(22, 0);
        var nattSlut = new TimeOnly(6, 0);

        // Pass som börjar efter 22 eller slutar efter midnatt och innan 06
        if (shift.Start >= nattStart) return true;
        if (shift.Slut <= nattSlut && shift.Slut > TimeOnly.MinValue) return true;
        // Nattpass som korsar midnatt: start > slut
        if (shift.Start > shift.Slut) return true;

        return false;
    }

    /// <summary>
    /// Beräkna faktisk sluttidpunkt som DateTime, med hänsyn till nattpass som korsar midnatt.
    /// </summary>
    private static DateTime BeraknaSlutDateTime(ShiftAssignment shift)
    {
        var startDT = shift.Datum.ToDateTime(shift.Start);
        var slutDT = shift.Datum.ToDateTime(shift.Slut);

        // Om sluttid är före starttid har passet korsat midnatt
        if (slutDT <= startDT)
        {
            slutDT = slutDT.AddDays(1);
        }

        return slutDT;
    }

    /// <summary>
    /// Beräkna vila i timmar mellan två på varandra följande pass.
    /// </summary>
    private static double BeraknaVilaTimmar(ShiftAssignment foregaende, ShiftAssignment nasta)
    {
        var slutForegaende = BeraknaSlutDateTime(foregaende);
        var startNasta = nasta.Datum.ToDateTime(nasta.Start);

        return (startNasta - slutForegaende).TotalHours;
    }

    /// <summary>
    /// Kontrollera dygnsvila framåt: det nya passets slut måste ge tillräcklig vila
    /// till nästa redan planerade pass.
    /// </summary>
    private bool UppfyllerDygnsvilaFramat(
        EmployeeId anstallId,
        ShiftAssignment newShift,
        IReadOnlyList<ShiftAssignment> existing)
    {
        var nyttPassSlut = BeraknaSlutDateTime(newShift);
        var senastDatum = newShift.Datum.AddDays(2);

        var nastaPass = existing
            .Where(a => a.AnstallId == anstallId && a.Datum >= newShift.Datum && a.Datum <= senastDatum)
            .Select(a => new
            {
                Assignment = a,
                StartDateTime = a.Datum.ToDateTime(a.Start)
            })
            .Where(a => a.StartDateTime > nyttPassSlut)
            .OrderBy(a => a.StartDateTime)
            .FirstOrDefault();

        if (nastaPass is null) return true;

        var vilaTimmar = (nastaPass.StartDateTime - nyttPassSlut).TotalHours;
        return vilaTimmar >= EffektivtMinDygnsvila;
    }

    /// <summary>
    /// Kontrollera om det finns minst 36 timmars sammanhängande vila i en veckoperiod.
    /// </summary>
    private static bool HarTillrackligVeckovila(
        List<ShiftAssignment> passIVeckan,
        DateOnly veckoStart,
        DateOnly veckoSlut)
    {
        if (passIVeckan.Count == 0) return true;

        // Bygg upp lista med (start, slut) perioder som DateTime
        var arbetsperioder = passIVeckan
            .Select(a => (Start: a.Datum.ToDateTime(a.Start), Slut: BeraknaSlutDateTime(a)))
            .OrderBy(p => p.Start)
            .ToList();

        // Beräkna viloperioder
        var veckoStartDT = veckoStart.ToDateTime(TimeOnly.MinValue);
        var veckoSlutDT = veckoSlut.ToDateTime(TimeOnly.MinValue);

        // Vila före första passet
        var maxVila = (arbetsperioder[0].Start - veckoStartDT).TotalHours;

        // Vila mellan pass
        for (int i = 1; i < arbetsperioder.Count; i++)
        {
            var vila = (arbetsperioder[i].Start - arbetsperioder[i - 1].Slut).TotalHours;
            if (vila > maxVila) maxVila = vila;
        }

        // Vila efter sista passet
        var vilaEfter = (veckoSlutDT - arbetsperioder[^1].Slut).TotalHours;
        if (vilaEfter > maxVila) maxVila = vilaEfter;

        return maxVila >= MIN_VECKOVILA_TIMMAR;
    }
}

/// <summary>
/// Resultat av ATL-validering.
/// </summary>
public sealed class ValidationResult
{
    public bool ArGiltigt { get; }
    public IReadOnlyList<ValidationViolation> Overtraldelser { get; }

    public ValidationResult(IReadOnlyList<ValidationViolation>? overtraldelser = null)
    {
        Overtraldelser = overtraldelser ?? [];
        ArGiltigt = Overtraldelser.Count == 0;
    }

    public static ValidationResult Godkant() => new();
}

/// <summary>
/// En enskild regelöverträdelse.
/// </summary>
public sealed class ValidationViolation
{
    /// <summary>Regelreferens, t.ex. "ATL §13 Dygnsvila".</summary>
    public string Regel { get; }

    /// <summary>Beskrivning på svenska av vad som är fel.</summary>
    public string Beskrivning { get; }

    /// <summary>Allvarlighetsgrad: Hard = olagligt, Soft = varning.</summary>
    public ViolationSeverity Allvarlighet { get; }

    public ValidationViolation(string regel, string beskrivning, ViolationSeverity allvarlighet)
    {
        Regel = regel;
        Beskrivning = beskrivning;
        Allvarlighet = allvarlighet;
    }
}

/// <summary>
/// Hard = lagbrott (får ej schemaläggas), Soft = varning (kan schemaläggas med godkännande).
/// </summary>
public enum ViolationSeverity
{
    Hard,
    Soft
}
