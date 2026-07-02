using RegionHR.Core.Contracts;
using RegionHR.LAS.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.LAS.Services;

/// <summary>
/// LAS-uppföljningstjänst.
/// Hanterar ackumulering, konverteringsspaning, turordningslistor,
/// daglig batchkontroll och dashboard.
/// </summary>
public sealed class LASService
{
    private readonly ICoreHRModule _coreHR;
    private readonly ILASRepository _repository;

    public LASService(ICoreHRModule coreHR, ILASRepository repository)
    {
        _coreHR = coreHR;
        _repository = repository;
    }

    /// <summary>Registrera en ny tidsbegränsad anställning för LAS-bevakning.</summary>
    public async Task<LASAccumulation> RegistreraPeriodAsync(
        EmployeeId anstallId, EmploymentType typ,
        DateOnly startDatum, DateOnly slutDatum,
        CancellationToken ct = default)
    {
        var accumulation = await _repository.GetByEmployeeAsync(anstallId, ct);

        if (accumulation is null)
        {
            accumulation = LASAccumulation.Skapa(anstallId, typ);
            accumulation.LaggTillPeriod(startDatum, slutDatum);
            await _repository.AddAsync(accumulation, ct);
        }
        else
        {
            accumulation.LaggTillPeriod(startDatum, slutDatum);
            await _repository.UpdateAsync(accumulation, ct);
        }

        return accumulation;
    }

    /// <summary>Hämta alla som är nära konverteringsgräns.</summary>
    public async Task<IReadOnlyList<LASAccumulation>> HamtaAlarmeringarAsync(CancellationToken ct = default)
    {
        var naraGrans = await _repository.GetByStatus(LASStatus.NaraGrans, ct);
        var kritiskNara = await _repository.GetByStatus(LASStatus.KritiskNara, ct);

        return naraGrans.Concat(kritiskNara)
            .OrderByDescending(a => a.AckumuleradeDagar)
            .ToList();
    }

    /// <summary>
    /// Daglig batchkontroll: omberäkna alla aktiva ackumuleringar,
    /// kontrollera gränser och generera alarmeringar.
    /// </summary>
    public async Task KorDagligKontrollAsync(CancellationToken ct = default)
    {
        var aktiva = await _repository.GetAllaAktiva(ct);
        var idag = DateOnly.FromDateTime(DateTime.Today);

        foreach (var acc in aktiva)
        {
            acc.Omberakna(idag);
            await _repository.UpdateAsync(acc, ct);
        }
    }

    /// <summary>
    /// Hämta alarm-dashboard med räkningar per status och top-10 närmast konvertering.
    /// </summary>
    public async Task<LASAlarmDashboard> HamtaDashboardAsync(CancellationToken ct = default)
    {
        var alla = await _repository.GetAllaAktiva(ct);

        var underGrans = alla.Count(a => a.Status == LASStatus.UnderGrans);
        var naraGrans = alla.Count(a => a.Status == LASStatus.NaraGrans);
        var kritiskNara = alla.Count(a => a.Status == LASStatus.KritiskNara);
        var konverterade = alla.Count(a => a.Status == LASStatus.KonverteradTillTillsvidare);
        var medForetradesratt = alla.Count(a => a.HarForetradesratt);

        var topNarmast = alla
            .Where(a => a.Status is not LASStatus.KonverteradTillTillsvidare)
            .OrderByDescending(a => a.AckumuleradeDagar)
            .Take(10)
            .ToList();

        return new LASAlarmDashboard
        {
            TotaltAktiva = alla.Count,
            UnderGrans = underGrans,
            NaraGrans = naraGrans,
            KritiskNara = kritiskNara,
            Konverterade = konverterade,
            MedForetradesratt = medForetradesratt,
            TopNarmastKonvertering = topNarmast
        };
    }

    /// <summary>
    /// Hämta alla som har aktiv företrädesrätt.
    /// </summary>
    public async Task<IReadOnlyList<LASAccumulation>> HamtaForetradesrattsinnehavareAsync(CancellationToken ct = default)
    {
        var alla = await _repository.GetAllaAktiva(ct);
        return alla
            .Where(a => a.HarForetradesratt && a.ForetradesrattUtgar >= DateOnly.FromDateTime(DateTime.Today))
            .OrderByDescending(a => a.AckumuleradeDagar)
            .ToList();
    }

    /// <summary>
    /// Avsluta anställning: sätt företrädesrätt om den anställde är berättigad.
    /// </summary>
    public async Task AvslutaAnstallningAsync(EmployeeId anstallId, DateOnly slutDatum, CancellationToken ct = default)
    {
        var acc = await _repository.GetByEmployeeAsync(anstallId, ct);
        if (acc is null)
            return;

        acc.SattForetradesratt(slutDatum);
        await _repository.UpdateAsync(acc, ct);
    }

    /// <summary>
    /// Registrera/korrigera en LAS-period med attestkontroll (HR-åtgärd).
    /// Systemet är experten: självattest avvisas — HR kan inte registrera sin egen LAS-status.
    /// </summary>
    public async Task<LASAccumulation> RegistreraPeriodAsync(
        EmployeeId anstallId, EmploymentType typ,
        DateOnly startDatum, DateOnly slutDatum,
        Guid? utfortAvEmployeeId, string utfortAvNamn,
        CancellationToken ct = default)
    {
        SakerstallEjSjalvattest(anstallId, utfortAvEmployeeId);
        if (slutDatum < startDatum)
            throw new ArgumentException("Slutdatum kan inte vara före startdatum.");
        return await RegistreraPeriodAsync(anstallId, typ, startDatum, slutDatum, ct);
    }

    /// <summary>Korrigera (ändra datum för) en registrerad LAS-period. HR-åtgärd, ingen självattest.</summary>
    public async Task<bool> KorrigeraPeriodAsync(
        EmployeeId anstallId,
        DateOnly gammalStart, DateOnly gammalSlut,
        DateOnly nyStart, DateOnly nySlut,
        Guid? utfortAvEmployeeId, string utfortAvNamn,
        CancellationToken ct = default)
    {
        SakerstallEjSjalvattest(anstallId, utfortAvEmployeeId);
        var acc = await _repository.GetByEmployeeAsync(anstallId, ct);
        if (acc is null)
            return false;

        var andrad = acc.AndraPeriod(gammalStart, gammalSlut, nyStart, nySlut);
        if (andrad)
            await _repository.UpdateAsync(acc, ct);
        return andrad;
    }

    /// <summary>Ta bort en felregistrerad LAS-period. HR-åtgärd, ingen självattest.</summary>
    public async Task<bool> TaBortPeriodAsync(
        EmployeeId anstallId, DateOnly startDatum, DateOnly slutDatum,
        Guid? utfortAvEmployeeId, string utfortAvNamn,
        CancellationToken ct = default)
    {
        SakerstallEjSjalvattest(anstallId, utfortAvEmployeeId);
        var acc = await _repository.GetByEmployeeAsync(anstallId, ct);
        if (acc is null)
            return false;

        var borttagen = acc.TaBortPeriod(startDatum, slutDatum);
        if (borttagen)
            await _repository.UpdateAsync(acc, ct);
        return borttagen;
    }

    /// <summary>
    /// Konvertera visstidsanställning till tillsvidareanställning (formbyte, §5a LAS).
    /// HR-beslut med attestkontroll. Returnerar false om ingen ackumulering finns eller redan konverterad.
    /// </summary>
    public async Task<bool> KonverteraTillTillsvidareAsync(
        EmployeeId anstallId, DateOnly konverteringsDatum,
        Guid? utfortAvEmployeeId, string utfortAvNamn,
        CancellationToken ct = default)
    {
        SakerstallEjSjalvattest(anstallId, utfortAvEmployeeId);
        var acc = await _repository.GetByEmployeeAsync(anstallId, ct);
        if (acc is null)
            return false;

        var konverterad = acc.KonverteraTillTillsvidare(konverteringsDatum, utfortAvNamn);
        if (konverterad)
            await _repository.UpdateAsync(acc, ct);
        return konverterad;
    }

    /// <summary>
    /// Bevilja företrädesrätt vid anställningens slut (§25 LAS). HR-beslut med attestkontroll.
    /// Returnerar true om den anställde efter bedömning har företrädesrätt.
    /// </summary>
    public async Task<bool> BeviljaForetradesrattAsync(
        EmployeeId anstallId, DateOnly slutDatum,
        Guid? utfortAvEmployeeId, string utfortAvNamn,
        CancellationToken ct = default)
    {
        SakerstallEjSjalvattest(anstallId, utfortAvEmployeeId);
        var acc = await _repository.GetByEmployeeAsync(anstallId, ct);
        if (acc is null)
            return false;

        acc.SattForetradesratt(slutDatum);
        await _repository.UpdateAsync(acc, ct);
        return acc.HarForetradesratt;
    }

    private static void SakerstallEjSjalvattest(EmployeeId mal, Guid? utfortAvEmployeeId)
    {
        if (utfortAvEmployeeId is { } id && id == mal.Value)
            throw new InvalidOperationException(
                "Självattest är inte tillåten: du kan inte registrera, korrigera eller besluta om din egen LAS-status.");
    }

    /// <summary>Generera turordningslista för en driftsenhet.</summary>
    public async Task<IReadOnlyList<TurordningsPost>> GenereraTurordningslistaAsync(
        OrganizationId enhetId, DateOnly datum, CancellationToken ct = default)
    {
        var anstallda = await _coreHR.GetEmployeesByUnitAsync(enhetId, datum, ct);
        var turordning = new List<TurordningsPost>();

        foreach (var anstalld in anstallda)
        {
            var acc = await _repository.GetByEmployeeAsync(anstalld.Id, ct);
            if (acc is not null)
            {
                turordning.Add(new TurordningsPost(
                    anstalld.Id,
                    $"{anstalld.Fornamn} {anstalld.Efternamn}",
                    acc.Anstallningsform,
                    acc.AckumuleradeDagar,
                    acc.Status));
            }
        }

        // Turordning: senast anställd sägs upp först (sist in, först ut)
        return turordning.OrderBy(t => t.AckumuleradeDagar).ToList();
    }
}

public record TurordningsPost(
    EmployeeId AnstallId,
    string Namn,
    EmploymentType Anstallningsform,
    int AckumuleradeDagar,
    LASStatus Status);
