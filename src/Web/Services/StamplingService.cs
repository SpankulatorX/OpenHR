using Microsoft.EntityFrameworkCore;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Scheduling.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Web.Services;

/// <summary>
/// Medarbetar-stämpling (kom-och-gå) direkt mot databasen via IDbContextFactory.
/// Skapar TimeClockEvent, kopplar och uppdaterar dagens schemalagda pass och
/// detekterar avvikelser mot schema (sen ankomst, tidig avgång, övertid, ej planerat pass).
///
/// Planerade tider (PlaneradStart/PlaneradSlut) är väggklocka; därför jämförs de mot
/// lokal tid, medan stämplingshändelsens Tidpunkt lagras i UTC för spårbarhet.
/// </summary>
public class StamplingService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;

    /// <summary>Tröskel i minuter innan sen ankomst / tidig avgång flaggas som avvikelse.</summary>
    private const int TroskelMinuter = 15;

    public StamplingService(IDbContextFactory<RegionHRDbContext> dbFactory) => _dbFactory = dbFactory;

    /// <summary>Hämta aktuell stämplingsstatus, dagens pass och dagens händelser.</summary>
    public async Task<StamplingDagsstatus> HamtaDagsstatusAsync(Guid anstallGuid, CancellationToken ct = default)
    {
        var empId = EmployeeId.From(anstallGuid);
        var lokalDatum = DateOnly.FromDateTime(DateTime.Now);
        var utcDagStart = DateTime.UtcNow.Date;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var handelser = await db.TimeClockEvents
            .Where(e => e.AnstallId == empId && e.Tidpunkt >= utcDagStart)
            .OrderBy(e => e.Tidpunkt)
            .ToListAsync(ct);

        var pass = await db.ScheduledShifts
            .Where(s => s.AnstallId == empId && s.Datum == lokalDatum && s.Status != ShiftStatus.Avbokad)
            .OrderBy(s => s.PlaneradStart)
            .ToListAsync(ct);

        var (arInne, sedan) = BeraknaInneStatus(handelser);

        return new StamplingDagsstatus(
            arInne,
            sedan,
            pass.Select(MapPass).ToList(),
            handelser.Select(h => new StamplingHandelseRad(TypText(h.Typ), h.Tidpunkt)).ToList());
    }

    /// <summary>Stämpla in. Kopplar till dagens planerade pass om ett finns och flaggar sen ankomst.</summary>
    public async Task<StamplingResultat> StamplaInAsync(Guid anstallGuid, string? ip, CancellationToken ct = default)
    {
        var empId = EmployeeId.From(anstallGuid);
        var lokalDatum = DateOnly.FromDateTime(DateTime.Now);
        var tidLokal = TimeOnly.FromDateTime(DateTime.Now);
        var utcDagStart = DateTime.UtcNow.Date;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var handelser = await db.TimeClockEvents
            .Where(e => e.AnstallId == empId && e.Tidpunkt >= utcDagStart)
            .OrderBy(e => e.Tidpunkt)
            .ToListAsync(ct);

        var (arInne, _) = BeraknaInneStatus(handelser);
        if (arInne)
            return new StamplingResultat(false, "Du är redan instämplad. Stämpla ut först.", null);

        var clockEvent = TimeClockEvent.StamplaIn(empId, ClockSource.Webbterminal, ip);
        db.TimeClockEvents.Add(clockEvent);

        string? avvikelseText = null;

        var pass = await db.ScheduledShifts
            .Where(s => s.AnstallId == empId && s.Datum == lokalDatum && s.Status != ShiftStatus.Avbokad
                        && s.FaktiskStart == null)
            .OrderBy(s => s.PlaneradStart)
            .FirstOrDefaultAsync(ct);

        string meddelande;
        if (pass is null)
        {
            avvikelseText = $"Instämpling utan planerat pass {lokalDatum:yyyy-MM-dd} kl {tidLokal:HH:mm}.";
            meddelande = "Instämpling registrerad, men inget planerat pass hittades.";
        }
        else
        {
            clockEvent.KopplatPassId = pass.Id;
            pass.StamplaIn(tidLokal);

            var senMinuter = DiffMinuter(tidLokal, pass.PlaneradStart);
            if (senMinuter > TroskelMinuter)
            {
                avvikelseText = $"Sen ankomst: planerad start {pass.PlaneradStart:HH:mm}, faktisk start {tidLokal:HH:mm} ({senMinuter} min sen).";
                pass.RegistreraAvvikelse(AvvikelseTyp.SenAnkomst, avvikelseText);
                meddelande = "Instämpling registrerad med avvikelse.";
            }
            else
            {
                meddelande = $"Instämpling registrerad kl {tidLokal:HH:mm}.";
            }
        }

        await db.SaveChangesAsync(ct);
        return new StamplingResultat(true, meddelande, avvikelseText);
    }

    /// <summary>Stämpla ut. Stänger aktivt pass, beräknar faktiska timmar och flaggar tidig avgång/övertid.</summary>
    public async Task<StamplingResultat> StamplaUtAsync(Guid anstallGuid, string? ip, CancellationToken ct = default)
    {
        var empId = EmployeeId.From(anstallGuid);
        var lokalDatum = DateOnly.FromDateTime(DateTime.Now);
        var tidLokal = TimeOnly.FromDateTime(DateTime.Now);
        var utcDagStart = DateTime.UtcNow.Date;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var handelser = await db.TimeClockEvents
            .Where(e => e.AnstallId == empId && e.Tidpunkt >= utcDagStart)
            .OrderBy(e => e.Tidpunkt)
            .ToListAsync(ct);

        var (arInne, _) = BeraknaInneStatus(handelser);
        if (!arInne)
            return new StamplingResultat(false, "Ingen aktiv instämpling hittad. Stämpla in först.", null);

        var clockEvent = TimeClockEvent.StamplaUt(empId, ClockSource.Webbterminal, ip);
        db.TimeClockEvents.Add(clockEvent);

        string? avvikelseText = null;

        var pass = await db.ScheduledShifts
            .Where(s => s.AnstallId == empId && s.Datum == lokalDatum && s.Status != ShiftStatus.Avbokad
                        && s.FaktiskStart != null && s.FaktiskSlut == null)
            .OrderBy(s => s.PlaneradStart)
            .FirstOrDefaultAsync(ct);

        string meddelande;
        if (pass is null)
        {
            meddelande = $"Utstämpling registrerad kl {tidLokal:HH:mm}.";
        }
        else
        {
            clockEvent.KopplatPassId = pass.Id;
            pass.StamplaUt(tidLokal);

            var tidigMinuter = DiffMinuter(pass.PlaneradSlut, tidLokal);
            if (tidLokal < pass.PlaneradSlut && tidigMinuter is > TroskelMinuter and < 12 * 60)
            {
                avvikelseText = $"Tidig avgång: planerad slut {pass.PlaneradSlut:HH:mm}, faktisk slut {tidLokal:HH:mm} ({tidigMinuter} min tidig).";
                pass.RegistreraAvvikelse(AvvikelseTyp.TidigAvgang, avvikelseText);
                meddelande = "Utstämpling registrerad med avvikelse.";
            }
            else if (pass.OvertidTimmar is > 0m)
            {
                avvikelseText = $"Övertid: {pass.OvertidTimmar:F2} timmar utöver planerad tid.";
                pass.RegistreraAvvikelse(AvvikelseTyp.Overtid, avvikelseText);
                meddelande = "Utstämpling registrerad med övertid.";
            }
            else
            {
                var timmar = pass.FaktiskaTimmar ?? 0m;
                meddelande = $"Utstämpling registrerad kl {tidLokal:HH:mm}. Arbetade timmar: {timmar:F2}h.";
            }
        }

        await db.SaveChangesAsync(ct);
        return new StamplingResultat(true, meddelande, avvikelseText);
    }

    private static (bool ArInne, DateTime? Sedan) BeraknaInneStatus(IEnumerable<TimeClockEvent> handelser)
    {
        var inne = false;
        DateTime? sedan = null;
        foreach (var h in handelser.OrderBy(e => e.Tidpunkt))
        {
            switch (h.Typ)
            {
                case ClockEventType.In:
                    inne = true;
                    sedan = h.Tidpunkt;
                    break;
                case ClockEventType.Ut:
                    inne = false;
                    sedan = null;
                    break;
                case ClockEventType.Raststart:
                case ClockEventType.Rastslut:
                default:
                    break;
            }
        }
        return (inne, sedan);
    }

    private static StamplingPassRad MapPass(ScheduledShift s)
    {
        string status = s.Status switch
        {
            ShiftStatus.Planerad => "Planerad",
            ShiftStatus.Pagaende => "Pågående",
            ShiftStatus.Avslutad => "Avslutad",
            ShiftStatus.Avbokad => "Avbokad",
            ShiftStatus.Bytt => "Bytt",
            _ => s.Status.ToString()
        };

        return new StamplingPassRad(
            s.PassTyp.ToString(),
            $"{s.PlaneradStart:HH:mm}–{s.PlaneradSlut:HH:mm}",
            s.FaktiskStart?.ToString("HH:mm"),
            s.FaktiskSlut?.ToString("HH:mm"),
            status,
            s.AvvikelseBeskrivning);
    }

    private static string TypText(ClockEventType typ) => typ switch
    {
        ClockEventType.In => "Instämpling",
        ClockEventType.Ut => "Utstämpling",
        ClockEventType.Raststart => "Rast start",
        ClockEventType.Rastslut => "Rast slut",
        _ => typ.ToString()
    };

    /// <summary>Positiv differens i minuter mellan två väggklocktider, hanterar midnattspassage.</summary>
    private static int DiffMinuter(TimeOnly faktisk, TimeOnly planerad)
    {
        var diff = faktisk.ToTimeSpan() - planerad.ToTimeSpan();
        if (diff < TimeSpan.Zero) diff += TimeSpan.FromHours(24);
        return (int)diff.TotalMinutes;
    }
}

/// <summary>Aktuell stämplingsstatus samt dagens pass och händelser.</summary>
public sealed record StamplingDagsstatus(
    bool ArInstamplad,
    DateTime? InstampladVidUtc,
    IReadOnlyList<StamplingPassRad> DagensPass,
    IReadOnlyList<StamplingHandelseRad> Handelser);

/// <summary>Rad som beskriver ett planerat pass och dess faktiska tider.</summary>
public sealed record StamplingPassRad(
    string PassTyp,
    string Planerat,
    string? FaktiskStart,
    string? FaktiskSlut,
    string Status,
    string? AvvikelseText);

/// <summary>Rad för en stämplingshändelse.</summary>
public sealed record StamplingHandelseRad(string Typ, DateTime TidpunktUtc);

/// <summary>Resultat av en stämplingsåtgärd.</summary>
public sealed record StamplingResultat(bool Lyckades, string Meddelande, string? AvvikelseText);
