using Microsoft.EntityFrameworkCore;
using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Leave.Domain;
using RegionHR.Payroll.Domain;
using RegionHR.Payroll.Engine;
using RegionHR.Scheduling.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Payroll;

/// <summary>
/// Bygger ett riktigt <see cref="PayrollInput"/> per anställd och period ur databasen.
/// Systemet är experten: underlaget härleds ur schema (OB, övertid, jour, beredskap),
/// frånvaroregister (sjuk, semester, föräldraledighet) och anställningens giltighetsperiod —
/// inte ur handmatade schabloner.
///
/// Aggregeringen sker i minnet eftersom <see cref="ScheduledShift.PlaneradeTimmar"/> och
/// <see cref="ScheduledShift.FaktiskaTimmar"/> är beräknade egenskaper som EF inte kan översätta.
/// </summary>
public sealed class PayrollInputBuilder
{
    // Sjuklön betalas dag 2–14 (Sjuklönelagen 7 §). Fler sjukdagar hanteras av Försäkringskassan
    // och ska inte generera sjuklön i löneunderlaget.
    private const int MaxSjuklonedagar = 14;

    /// <summary>
    /// Bygg löneunderlag för en anställning och en kalendermånad.
    /// </summary>
    public async Task<PayrollInput> BuildAsync(
        RegionHRDbContext db,
        Employment employment,
        int year,
        int month,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(employment);

        var firstDay = new DateOnly(year, month, 1);
        var lastDay = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var anstallId = employment.AnstallId;

        // Arbetsdagar i månaden (mån–fre exkl. svenska helgdagar).
        var arbetsdagarIManaden = RaknaArbetsdagar(firstDay, lastDay);

        // Arbetade dagar = arbetsdagar som ligger inom anställningens giltighetsperiod.
        // Detta proportionerar grundlönen korrekt vid anställning/avslut mitt i månaden;
        // frånvaro (sjuk/semester) dras separat som egna rader och räknas inte bort här.
        var arbetadeDagar = RaknaArbetsdagar(firstDay, lastDay,
            d => employment.Giltighetsperiod.IsActiveOn(d));

        var input = new PayrollInput
        {
            ArbetadeDagar = arbetadeDagar,
            ArbetsdagarIManadens = arbetsdagarIManaden,
            Kostnadsstalle = employment.EnhetId.Value.ToString()
        };

        // === Schema: OB, övertid, jour, beredskap ===
        var shifts = await db.ScheduledShifts
            .Where(s => s.AnstallId == anstallId
                        && s.Datum >= firstDay
                        && s.Datum <= lastDay
                        && s.Status != ShiftStatus.Avbokad
                        && s.Status != ShiftStatus.Bytt)
            .ToListAsync(ct);

        AggregeraSchema(shifts, input);

        // Övertid: föredra schemats instämplade övertid, annars godkänd tidrapport för perioden.
        if (input.OvertidTimmar == 0m)
        {
            var timesheet = await db.Timesheets
                .Where(t => t.AnstallId == anstallId.Value && t.Ar == year && t.Manad == month)
                .OrderByDescending(t => t.SkapadVid)
                .FirstOrDefaultAsync(ct);

            if (timesheet is not null && timesheet.Overtid > 0m)
            {
                input.OvertidTimmar = timesheet.Overtid;
                input.KvalificeradOvertid = timesheet.Overtid > 2m;
            }
        }

        // === Frånvaro: sjuk, semester, föräldraledighet (endast godkänd frånvaro) ===
        var leave = await db.LeaveRequests
            .Where(l => l.AnstallId == anstallId.Value
                        && l.Status == LeaveRequestStatus.Godkand
                        && l.FranDatum <= lastDay
                        && l.TillDatum >= firstDay)
            .ToListAsync(ct);

        AggregeraFranvaro(leave, firstDay, lastDay, input);

        // === Nettolöneavdrag ===
        // Löneutmätning (Kronofogden) och fackavgift saknar ännu ett dedikerat register.
        // De byggs in här när källorna finns; tills dess 0 kr (aldrig en schablon).
        input.Loneutmatning = Money.Zero;
        input.Fackavgift = Money.Zero;

        return input;
    }

    /// <summary>
    /// Aggregera OB-timmar per kategori, övertid, jour- och beredskapstimmar ur passen.
    /// </summary>
    private static void AggregeraSchema(IReadOnlyList<ScheduledShift> shifts, PayrollInput input)
    {
        var obPerKategori = new Dictionary<OBCategory, decimal>();
        var overtid = 0m;
        var kvalificerad = false;
        var jour = 0m;
        var beredskap = 0m;

        foreach (var pass in shifts)
        {
            var timmar = pass.FaktiskaTimmar ?? pass.PlaneradeTimmar;

            switch (pass.PassTyp)
            {
                case ShiftType.Jour:
                    jour += timmar;
                    continue;
                case ShiftType.Beredskap:
                    beredskap += timmar;
                    continue;
            }

            if (pass.OBKategori != OBCategory.Ingen && timmar > 0m)
            {
                obPerKategori.TryGetValue(pass.OBKategori, out var befintlig);
                obPerKategori[pass.OBKategori] = befintlig + timmar;
            }

            if (pass.OvertidTimmar is { } ot && ot > 0m)
            {
                overtid += ot;
                if (ot > 2m) kvalificerad = true; // AB: > 2h/dag bedöms som kvalificerad övertid
            }
        }

        input.OBTimmar = obPerKategori
            .Select(kv => new OBInput { Kategori = kv.Key, Timmar = kv.Value })
            .OrderBy(o => o.Kategori)
            .ToList();
        input.OvertidTimmar = overtid;
        input.KvalificeradOvertid = kvalificerad;
        input.JourTimmar = jour;
        input.BeredskapsTimmar = beredskap;
    }

    /// <summary>
    /// Aggregera frånvarodagar (arbetsdagar inom perioden) per frånvarotyp.
    /// </summary>
    private static void AggregeraFranvaro(
        IReadOnlyList<LeaveRequest> leave, DateOnly firstDay, DateOnly lastDay, PayrollInput input)
    {
        var sjuk = 0;
        var semester = 0;
        var foraldra = 0;

        foreach (var l in leave)
        {
            var from = l.FranDatum > firstDay ? l.FranDatum : firstDay;
            var to = l.TillDatum < lastDay ? l.TillDatum : lastDay;
            var dagar = RaknaArbetsdagar(from, to);
            if (dagar == 0) continue;

            switch (l.Typ)
            {
                case LeaveType.Sjukfranvaro:
                    sjuk += dagar;
                    break;
                case LeaveType.Semester:
                    semester += dagar;
                    break;
                case LeaveType.Foraldraledighet:
                    foraldra += dagar;
                    break;
            }
        }

        // Sjuklön betalas endast dag 2–14; övriga sjukdagar ligger hos Försäkringskassan.
        input.SjukdagarMedLon = Math.Min(sjuk, MaxSjuklonedagar);
        input.SemesterdagarUttagna = semester;
        input.ForaldraledigaDagar = foraldra;
    }

    /// <summary>
    /// Räkna arbetsdagar (mån–fre exkl. svenska helgdagar) i ett datumintervall,
    /// med ett valfritt extra villkor per dag.
    /// </summary>
    private static int RaknaArbetsdagar(DateOnly from, DateOnly to, Func<DateOnly, bool>? predicate = null)
    {
        if (to < from) return 0;

        var antal = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            if (SvenskaHelgdagar.ArHelgdag(d))
                continue;
            if (predicate is not null && !predicate(d))
                continue;
            antal++;
        }

        return antal;
    }
}
