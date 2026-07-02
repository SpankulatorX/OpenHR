using Microsoft.EntityFrameworkCore;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Leave.Domain;
using RegionHR.Notifications.Domain;
using RegionHR.Scheduling.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Web.Services;

/// <summary>
/// Beräknar och persisterar flexsaldo ur stämplingar (faktisk tid) jämfört med schemalagd tid,
/// samt hanterar flexinställningar (gränser). Själva räkningen görs av den rena
/// <see cref="FlexCalculator"/>; denna tjänst hämtar underlag och sparar resultat.
/// </summary>
public class FlexService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;

    public FlexService(IDbContextFactory<RegionHRDbContext> dbFactory) => _dbFactory = dbFactory;

    /// <summary>Hämta sparad flexinställning eller standard om ingen finns.</summary>
    public async Task<FlexInstallning> HamtaInstallningAsync(Guid anstallGuid, CancellationToken ct = default)
    {
        var empId = EmployeeId.From(anstallGuid);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var inst = await db.Set<FlexInstallning>()
            .FirstOrDefaultAsync(x => x.AnstallId == empId, ct);
        return inst ?? FlexInstallning.Standard(empId);
    }

    /// <summary>
    /// Spara flexinställning (endast chef/HR). Validerar gränserna: MaxPlus ≥ 0, MaxMinus ≤ 0,
    /// daglig gräns ≥ 0. Kastar <see cref="ArgumentException"/> vid ogiltiga värden.
    /// </summary>
    public async Task SparaInstallningAsync(
        Guid anstallGuid,
        bool flexAktiverad,
        decimal maxPlusTimmar,
        decimal maxMinusTimmar,
        decimal dagligFlexgransTimmar,
        Guid andradAv,
        CancellationToken ct = default)
    {
        if (maxPlusTimmar < 0m)
            throw new ArgumentException("Övre gräns kan inte vara negativ.", nameof(maxPlusTimmar));
        if (maxMinusTimmar > 0m)
            throw new ArgumentException("Undre gräns kan inte vara positiv.", nameof(maxMinusTimmar));
        if (dagligFlexgransTimmar < 0m)
            throw new ArgumentException("Daglig flexgräns kan inte vara negativ.", nameof(dagligFlexgransTimmar));

        var empId = EmployeeId.From(anstallGuid);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var inst = await db.Set<FlexInstallning>()
            .FirstOrDefaultAsync(x => x.AnstallId == empId, ct);

        if (inst is null)
        {
            inst = FlexInstallning.Standard(empId);
            db.Set<FlexInstallning>().Add(inst);
        }

        inst.FlexAktiverad = flexAktiverad;
        inst.MaxPlusTimmar = maxPlusTimmar;
        inst.MaxMinusTimmar = maxMinusTimmar;
        inst.DagligFlexgransTimmar = dagligFlexgransTimmar;
        inst.SenastAndradAv = andradAv;
        inst.UppdateradVid = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Beräkna flexöversikt (läsning, inget sparas) för perioden. Underlaget är alla schemalagda
    /// pass i intervallet; endast pass med både in- och utstämpling räknas som faktisk tid.
    /// Kompsaldo = summan av registrerad övertid i perioden.
    /// </summary>
    public async Task<FlexOversikt> BeraknaOversiktAsync(
        Guid anstallGuid,
        DateOnly fran,
        DateOnly tom,
        CancellationToken ct = default)
    {
        var empId = EmployeeId.From(anstallGuid);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var installning = await db.Set<FlexInstallning>()
            .FirstOrDefaultAsync(x => x.AnstallId == empId, ct)
            ?? FlexInstallning.Standard(empId);

        var pass = await db.ScheduledShifts
            .Where(s => s.AnstallId == empId && s.Datum >= fran && s.Datum <= tom
                        && s.Status != ShiftStatus.Avbokad)
            .OrderBy(s => s.Datum)
            .ToListAsync(ct);

        var underlag = pass.Select(s => new FlexDagsunderlag(s.Datum, s.PlaneradeTimmar, s.FaktiskaTimmar)).ToList();
        var resultat = FlexCalculator.Berakna(0m, underlag, installning);

        var kompsaldo = pass.Where(s => s.OvertidTimmar.HasValue).Sum(s => s.OvertidTimmar!.Value);

        return new FlexOversikt(
            resultat.UtgaendeSaldo,
            Math.Round(kompsaldo, 2),
            installning.FlexAktiverad,
            installning.MaxPlusTimmar,
            installning.MaxMinusTimmar,
            resultat.NaddeOvreGrans,
            resultat.NaddeUndreGrans,
            resultat.Dagsposter);
    }

    /// <summary>
    /// Räkna om flexsaldot ur underlaget och spara ögonblicksbilden i <see cref="FlexBalance"/>.
    /// Returnerar den sparade balansen.
    /// </summary>
    public async Task<FlexBalance> RaknaOmOchSparaAsync(
        Guid anstallGuid,
        DateOnly fran,
        DateOnly tom,
        CancellationToken ct = default)
    {
        var empId = EmployeeId.From(anstallGuid);
        var oversikt = await BeraknaOversiktAsync(anstallGuid, fran, tom, ct);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var balance = await db.Set<FlexBalance>()
            .FirstOrDefaultAsync(x => x.AnstallId == empId, ct);

        if (balance is null)
        {
            balance = new FlexBalance { AnstallId = empId };
            db.Set<FlexBalance>().Add(balance);
        }

        balance.SaldoTimmar = oversikt.Flexsaldo;
        balance.KompsaldoTimmar = oversikt.Kompsaldo;
        balance.BeraknadTom = tom;
        balance.UppdateradVid = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return balance;
    }

    // ── Komptidsuttag ─────────────────────────────────────────────────────────
    // Rullande fönster för intjänad (brutto) komptid. Komptid tjänas in löpande
    // och tas normalt ut inom ett år; ett års fönster täcker det stämplade underlaget.
    private const int KomptidFonsterDagar = 365;

    /// <summary>
    /// Aktuellt komptidssaldo: intjänat brutto (ur övertidsunderlaget senaste året),
    /// redan uttaget (persisterad huvudbok) och tillgängligt netto att ta ut.
    /// </summary>
    public async Task<KomptidSaldo> HamtaKomptidsaldoAsync(Guid anstallGuid, CancellationToken ct = default)
    {
        var empId = EmployeeId.From(anstallGuid);
        var idag = DateOnly.FromDateTime(DateTime.Today);
        var oversikt = await BeraknaOversiktAsync(anstallGuid, idag.AddDays(-KomptidFonsterDagar), idag, ct);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var balance = await db.Set<FlexBalance>().FirstOrDefaultAsync(x => x.AnstallId == empId, ct);
        var uttaget = balance?.UttagnaKompTimmar ?? 0m;

        var intjanat = Math.Round(oversikt.Kompsaldo, 2);
        return new KomptidSaldo(intjanat, uttaget, Math.Round(intjanat - uttaget, 2));
    }

    /// <summary>Medarbetarens egna uttagsbegäranden, nyaste först.</summary>
    public async Task<IReadOnlyList<KomptidUttag>> HamtaMinaUttagAsync(Guid anstallGuid, CancellationToken ct = default)
    {
        var empId = EmployeeId.From(anstallGuid);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Set<KomptidUttag>()
            .Where(u => u.AnstallId == empId)
            .OrderByDescending(u => u.BegardVid)
            .ToListAsync(ct);
    }

    /// <summary>Alla uttagsbegäranden som väntar på beslut (för chefsvyn), med medarbetarnamn.</summary>
    public async Task<IReadOnlyList<KomptidUttagRad>> HamtaVantandeUttagAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var uttag = await db.Set<KomptidUttag>()
            .Where(u => u.Status == KomputtagStatus.Begard)
            .OrderBy(u => u.BegardVid)
            .ToListAsync(ct);

        if (uttag.Count == 0)
            return Array.Empty<KomptidUttagRad>();

        var ids = uttag.Select(u => u.AnstallId).Distinct().ToList();
        var namn = await db.Employees
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FulltNamn, ct);

        return uttag
            .Select(u => new KomptidUttagRad(u, namn.TryGetValue(u.AnstallId, out var n) ? n : "Okänd medarbetare"))
            .ToList();
    }

    /// <summary>
    /// Begär uttag av komptid. Validerar indata och gör en mjuk förhandskontroll mot
    /// tillgängligt saldo; den bindande kontrollen och saldodragningen sker vid godkännandet.
    /// </summary>
    public async Task<KomptidUttag> BegarKomputtagAsync(
        Guid anstallGuid,
        decimal timmar,
        KomputtagTyp typ,
        DateOnly? franDatum,
        DateOnly? tillDatum,
        string? beskrivning,
        CancellationToken ct = default)
    {
        var empId = EmployeeId.From(anstallGuid);
        // Domänvalidering (timmar > 0, giltiga datum för ledighet) sker i Skapa.
        var uttag = KomptidUttag.Skapa(empId, timmar, typ, franDatum, tillDatum, beskrivning);

        var saldo = await HamtaKomptidsaldoAsync(anstallGuid, ct);
        if (uttag.Timmar > saldo.TillgangligTimmar)
            throw new InvalidOperationException(
                $"Otillräcklig komptid. Tillgängligt: {saldo.TillgangligTimmar:0.##} h, begärt: {uttag.Timmar:0.##} h.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Set<KomptidUttag>().Add(uttag);
        await db.SaveChangesAsync(ct);
        return uttag;
    }

    /// <summary>
    /// Godkänner ett komptidsuttag. Allt sker atomiskt i en <c>SaveChanges</c>:
    /// färskt bruttosaldo räknas fram, saldot dras (kan aldrig övertrasseras), och för
    /// kompledigt uttag skapas en godkänd ledighetspost och överlappande pass bokas av.
    /// Om saldot inte räcker eller statusen är fel sparas ingenting.
    /// </summary>
    public async Task<KomptidUttag> GodkannKomputtagAsync(
        Guid uttagId, Guid godkannare, string? kommentar = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var uttag = await db.Set<KomptidUttag>().FirstOrDefaultAsync(u => u.Id == uttagId, ct)
            ?? throw new InvalidOperationException("Uttagsbegäran hittades inte.");

        // Färskt bruttosaldo ur övertidsunderlaget (samma fönster som visningen).
        var idag = DateOnly.FromDateTime(DateTime.Today);
        var oversikt = await BeraknaOversiktAsync(
            uttag.AnstallId.Value, idag.AddDays(-KomptidFonsterDagar), idag, ct);

        var balance = await db.Set<FlexBalance>().FirstOrDefaultAsync(x => x.AnstallId == uttag.AnstallId, ct);
        if (balance is null)
        {
            balance = new FlexBalance { AnstallId = uttag.AnstallId };
            db.Set<FlexBalance>().Add(balance);
        }
        balance.SaldoTimmar = oversikt.Flexsaldo;
        balance.KompsaldoTimmar = Math.Round(oversikt.Kompsaldo, 2);
        balance.BeraknadTom = idag;

        // Statusövergång först (kastar om ej Begärd), sedan den enda faktiska saldodragningen
        // (kastar om otillräckligt) — inget delresultat sparas om något av detta kastar.
        uttag.Godkann(godkannare, kommentar);
        balance.RegistreraKomputtag(uttag.Timmar);

        // Kompledigt uttag → godkänd ledighetspost + avboka överlappande planerade pass.
        if (uttag.Typ == KomputtagTyp.Ledighet && uttag.FranDatum is { } fran && uttag.TillDatum is { } till)
        {
            var post = LeaveRequest.Skapa(
                uttag.AnstallId.Value, LeaveType.Komptid, fran, till,
                uttag.Beskrivning ?? "Kompledigt uttag");
            post.SkickaIn();
            post.Godkann(godkannare, kommentar);
            db.LeaveRequests.Add(post);
            uttag.KopplaLedighetspost(post.Id);

            var pass = await db.ScheduledShifts
                .Where(s => s.AnstallId == uttag.AnstallId
                            && s.Datum >= fran && s.Datum <= till
                            && s.Status == ShiftStatus.Planerad)
                .ToListAsync(ct);
            foreach (var p in pass)
                p.Status = ShiftStatus.Avbokad;
        }

        var text = uttag.Typ == KomputtagTyp.Ledighet
            ? $"Ditt uttag av {uttag.Timmar:0.##} h komptid som ledighet " +
              $"{uttag.FranDatum:yyyy-MM-dd}–{uttag.TillDatum:yyyy-MM-dd} har godkänts."
            : $"Ditt uttag av {uttag.Timmar:0.##} h komptid som utbetalning har godkänts " +
              "och tas med i nästa lönekörning.";
        db.Notifications.Add(Notification.Create(
            uttag.AnstallId.Value, "Komptidsuttag godkänt", text,
            NotificationType.Info, actionUrl: "/minsida/saldon"));

        await db.SaveChangesAsync(ct);
        return uttag;
    }

    /// <summary>Avslår ett komptidsuttag med obligatorisk motivering och notifierar medarbetaren.</summary>
    public async Task AvslaKomputtagAsync(
        Guid uttagId, Guid handlaggare, string kommentar, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var uttag = await db.Set<KomptidUttag>().FirstOrDefaultAsync(u => u.Id == uttagId, ct)
            ?? throw new InvalidOperationException("Uttagsbegäran hittades inte.");

        uttag.Avsla(handlaggare, kommentar);

        db.Notifications.Add(Notification.Create(
            uttag.AnstallId.Value, "Komptidsuttag avslaget",
            $"Din begäran om uttag av {uttag.Timmar:0.##} h komptid har avslagits. {kommentar}",
            NotificationType.Warning, actionUrl: "/minsida/saldon"));

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Medarbetaren återkallar sin egen ännu obeslutade uttagsbegäran.
    /// Endast begärande medarbetare får återkalla.
    /// </summary>
    public async Task AterkallaKomputtagAsync(
        Guid uttagId, Guid anstallGuid, CancellationToken ct = default)
    {
        var empId = EmployeeId.From(anstallGuid);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var uttag = await db.Set<KomptidUttag>().FirstOrDefaultAsync(u => u.Id == uttagId, ct)
            ?? throw new InvalidOperationException("Uttagsbegäran hittades inte.");

        if (uttag.AnstallId != empId)
            throw new InvalidOperationException("Endast den som begärt uttaget kan återkalla det.");

        uttag.Aterkalla();
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Komptidssaldo: intjänat brutto, redan uttaget och tillgängligt netto (timmar).</summary>
public sealed record KomptidSaldo(decimal IntjanatTimmar, decimal UttagetTimmar, decimal TillgangligTimmar);

/// <summary>En väntande uttagsbegäran ihop med medarbetarens namn (för chefsvyn).</summary>
public sealed record KomptidUttagRad(KomptidUttag Uttag, string AnstallNamn);

/// <summary>Aggregerad flexöversikt för presentation.</summary>
public sealed record FlexOversikt(
    decimal Flexsaldo,
    decimal Kompsaldo,
    bool FlexAktiverad,
    decimal MaxPlus,
    decimal MaxMinus,
    bool NaddeOvreGrans,
    bool NaddeUndreGrans,
    IReadOnlyList<FlexDagspost> Dagsposter);
