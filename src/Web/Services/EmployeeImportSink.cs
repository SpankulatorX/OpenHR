using Microsoft.EntityFrameworkCore;
using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Migration.Domain;
using RegionHR.Migration.Services;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Web.Services;

/// <summary>
/// Web-lagrets konkreta koppling mellan Migration-modulens <see cref="MigrationImportService"/>
/// och kärndomänen. Skapar riktiga <see cref="Employee"/>/<see cref="Employment"/> via Core:s
/// publika API (<c>Employee.Skapa</c> / <c>Employee.LaggTillAnstallning</c>), slår upp eller skapar
/// <see cref="OrganizationUnit"/> per enhetskod, och persisterar allt i EN transaktion
/// (ett <c>SaveChangesAsync</c>) mot databasen. En historikpost (<see cref="MigrationJob"/>) skrivs
/// i samma transaktion så att importen syns i migreringslistan.
/// </summary>
public sealed class EmployeeImportSink : IEmployeeImportSink
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;

    public EmployeeImportSink(IDbContextFactory<RegionHRDbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<IReadOnlyDictionary<string, Guid>> LaddaBefintligaAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rader = await db.Employees
            .Select(e => new { e.Id, Pnr = (string)e.Personnummer })
            .ToListAsync(ct);

        var lookup = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var rad in rader)
            lookup.TryAdd(rad.Pnr, rad.Id.Value);
        return lookup;
    }

    public async Task<SinkExekveringsResultat> ExekveraAsync(
        IReadOnlyList<EmployeeImportOperation> operationer,
        MigrationImportContext kontext,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Förladda enheter för idempotent uppslag (enhetskod matchas mot kostnadsställe eller namn).
        // Cachen fylls även på med nyskapade enheter så att flera rader med samma nya enhetskod
        // delar en och samma enhet inom körningen.
        var enhetCache = new Dictionary<string, OrganizationId>(StringComparer.OrdinalIgnoreCase);
        var befintligaEnheter = await db.OrganizationUnits
            .Select(o => new { o.Id, o.Kostnadsstalle, o.Namn })
            .ToListAsync(ct);
        foreach (var enhet in befintligaEnheter)
        {
            if (!string.IsNullOrWhiteSpace(enhet.Kostnadsstalle))
                enhetCache.TryAdd(enhet.Kostnadsstalle, enhet.Id);
            if (!string.IsNullOrWhiteSpace(enhet.Namn))
                enhetCache.TryAdd(enhet.Namn, enhet.Id);
        }

        var utfall = new List<SinkRadUtfall>(operationer.Count);
        var skapade = 0;
        var uppdaterade = 0;
        var fel = 0;

        foreach (var op in operationer)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (op.Typ == ImportOperation.Skapa)
                {
                    var anstalld = Employee.Skapa(op.Data.Personnummer, op.Data.Fornamn, op.Data.Efternamn);

                    if (op.Data.Epost is not null || op.Data.Telefon is not null)
                        anstalld.UppdateraKontaktuppgifter(op.Data.Epost, op.Data.Telefon, null);

                    if (op.Data.Anstallning is { } a)
                    {
                        var enhetId = LosUppEllerSkapaEnhet(db, enhetCache, a.Enhetskod);
                        anstalld.LaggTillAnstallning(
                            enhetId,
                            a.Anstallningsform,
                            a.Kollektivavtal,
                            a.Manadslon,
                            a.Sysselsattningsgrad,
                            a.Startdatum,
                            a.Slutdatum,
                            bestaKod: null,
                            aidKod: null,
                            befattningstitel: a.Befattning,
                            avtalsId: null);
                    }

                    await db.Employees.AddAsync(anstalld, ct);
                    utfall.Add(new SinkRadUtfall(op.RadNummer, true, anstalld.Id.Value, null));
                    skapade++;
                }
                else
                {
                    var id = op.BefintligtEmployeeId is { } guid ? EmployeeId.From(guid) : default;
                    var anstalld = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
                    if (anstalld is null)
                    {
                        utfall.Add(new SinkRadUtfall(op.RadNummer, false, null, "Befintlig anställd hittades inte"));
                        fel++;
                        continue;
                    }

                    if (op.Data.Epost is not null || op.Data.Telefon is not null)
                        anstalld.UppdateraKontaktuppgifter(op.Data.Epost, op.Data.Telefon, anstalld.Adress);

                    utfall.Add(new SinkRadUtfall(op.RadNummer, true, anstalld.Id.Value, null));
                    uppdaterade++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Domänvalidering (t.ex. LAS-regler) eller mappningsfel för EN rad ska inte fälla hela
                // importen — logga radens fel och fortsätt med resten.
                utfall.Add(new SinkRadUtfall(op.RadNummer, false, null, ex.Message));
                fel++;
            }
        }

        // Historikpost i migreringslistan (skrivs i samma transaktion som anställda).
        var totaltFel = fel + kontext.AntalOgiltiga;
        var jobb = MigrationJob.Skapa(kontext.Kalla, kontext.FilNamn, kontext.SkapadAv);
        jobb.SattTotaltAntalRader(kontext.TotaltAntalRader);
        jobb.StartaValidering();
        jobb.StartaDryRun();
        jobb.StartaImport();
        jobb.Slutfor(kontext.TotaltAntalRader, skapade + uppdaterade, totaltFel);
        await db.MigrationJobs.AddAsync(jobb, ct);

        try
        {
            // Ett SaveChanges = en atomisk transaktion. Misslyckas den rullas allt tillbaka.
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SinkExekveringsResultat(Array.Empty<SinkRadUtfall>(), $"Databasfel vid import: {ex.Message}");
        }

        return new SinkExekveringsResultat(utfall, null);
    }

    private static OrganizationId LosUppEllerSkapaEnhet(
        RegionHRDbContext db,
        Dictionary<string, OrganizationId> cache,
        string enhetskod)
    {
        var nyckel = enhetskod.Trim();
        if (cache.TryGetValue(nyckel, out var id))
            return id;

        var enhet = OrganizationUnit.Skapa(
            namn: nyckel,
            typ: OrganizationUnitType.Enhet,
            kostnadsstalle: nyckel,
            giltigFran: DateOnly.FromDateTime(DateTime.Today));

        db.OrganizationUnits.Add(enhet);
        cache[nyckel] = enhet.Id;
        return enhet.Id;
    }
}
