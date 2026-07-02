using Microsoft.EntityFrameworkCore;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Recruitment.Domain;
using RegionHR.Recruitment.Services;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Web.Services;

/// <summary>
/// Driver rekryteringsflödet i den driftsatta Blazor-appen: vakanser, ansökningar,
/// pipeline-steg (bedöm → intervju → erbjudande) och — det som stänger kedjan —
/// tillsättning av en kandidat till en riktig anställd med onboarding.
///
/// Når data direkt via <see cref="IDbContextFactory{RegionHRDbContext}"/> (ingen HTTP),
/// i linje med övriga Blazor-tjänster. Konverteringslogiken ligger i den rena
/// <see cref="KandidatKonvertering"/> i Recruitment-modulen; här sköts persistensen.
/// </summary>
public sealed class RekryteringService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;

    public RekryteringService(IDbContextFactory<RegionHRDbContext> dbFactory) => _dbFactory = dbFactory;

    /// <summary>Hämtar vakanser med sina ansökningar för pipeline/tillsättnings-vyn.</summary>
    public async Task<List<VakansOversikt>> HamtaVakanserAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var vakanser = await db.Vacancies
            .Include(v => v.Ansokngar)
            .OrderByDescending(v => v.SistaAnsokningsDag)
            .ToListAsync(ct);

        var enhetNamn = (await db.OrganizationUnits.ToListAsync(ct))
            .ToDictionary(e => e.Id.Value, e => e.Namn);

        return vakanser.Select(v => new VakansOversikt(
            v.Id,
            v.Titel,
            enhetNamn.GetValueOrDefault(v.EnhetId.Value, "—"),
            v.Anstallningsform,
            v.Status,
            v.SistaAnsokningsDag,
            v.Lonespann_Min?.Amount,
            v.Lonespann_Max?.Amount,
            v.TillsattAnsokanId,
            v.Ansokngar
                .OrderByDescending(a => a.Poang ?? -1)
                .ThenBy(a => a.Namn)
                .Select(a => new AnsokanOversikt(a.Id, a.Namn, a.Epost, a.Status, a.Poang, a.IntervjuTidpunkt))
                .ToList()))
            .ToList();
    }

    /// <summary>Bedömer en ansökan (Mottagen/Granskning → Granskning).</summary>
    public async Task BedomAsync(Guid vakansId, Guid ansokanId, int poang, string kommentar, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (_, ansokan) = await LaddaAsync(db, vakansId, ansokanId, ct);
        ansokan.Bedoma(poang, kommentar);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Bjuder in en granskad kandidat till intervju.</summary>
    public async Task BokaIntervjuAsync(Guid vakansId, Guid ansokanId, DateTime tidpunkt, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (_, ansokan) = await LaddaAsync(db, vakansId, ansokanId, ct);
        ansokan.BjudInIntervju(tidpunkt);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Erbjuder tjänsten till en kandidat efter intervju (Intervju → Erbjudande).</summary>
    public async Task ErbjudTjanstAsync(Guid vakansId, Guid ansokanId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (_, ansokan) = await LaddaAsync(db, vakansId, ansokanId, ct);
        ansokan.ErbjudTjanst();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Avslår en ansökan med angiven anledning.</summary>
    public async Task AvslutaAnsokanAsync(Guid vakansId, Guid ansokanId, string anledning, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (_, ansokan) = await LaddaAsync(db, vakansId, ansokanId, ct);
        ansokan.Avsluta(anledning);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Stänger kedjan: tillsätter vakansen med kandidaten, skapar ett riktigt Employee + Employment
    /// via Core-domänen och en onboarding-checklista — allt sparat atomiskt i en transaktion.
    /// Efter detta är kandidaten en sökbar anställd i personalregistret.
    /// </summary>
    public async Task<TillsattResultat> TillsattOchAnstallAsync(
        Guid vakansId, Guid ansokanId,
        string personnummer, string fornamn, string efternamn,
        string? epost, string? telefon,
        EmploymentType anstallningsform, CollectiveAgreementType kollektivavtal,
        decimal manadslon, decimal sysselsattningsgrad,
        DateOnly startdatum, DateOnly? slutdatum, string? befattning,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var vakans = await db.Vacancies
            .Include(v => v.Ansokngar)
            .FirstOrDefaultAsync(v => v.Id == vakansId, ct)
            ?? throw new InvalidOperationException($"Vakans {vakansId} hittades inte.");

        var pnr = new Personnummer(personnummer); // Validerar Luhn/datum — kastar ArgumentException vid fel.

        var data = new KandidatAnstallningsData(
            pnr, fornamn, efternamn,
            vakans.EnhetId, anstallningsform, kollektivavtal,
            manadslon, sysselsattningsgrad,
            startdatum, slutdatum, befattning, epost, telefon);

        var resultat = KandidatKonvertering.TillsattTillAnstalld(vakans, ansokanId, data);

        db.Employees.Add(resultat.Anstalld);
        db.OnboardingChecklists.Add(resultat.Onboarding);
        // vakans är redan spårad — dess ändringar (Tillsatt + ansökans status) sparas med.

        await db.SaveChangesAsync(ct);

        return new TillsattResultat(
            resultat.Anstalld.Id,
            resultat.Anstalld.FulltNamn,
            resultat.Anstalld.AktivAnstallning(startdatum)?.Befattningstitel ?? vakans.Titel,
            resultat.Onboarding.Items.Count);
    }

    /// <summary>Hämtar de onboarding-checklistor som skapats för tillsatta kandidater.</summary>
    public async Task<List<OnboardingVy>> HamtaOnboardingAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var checklistor = await db.OnboardingChecklists
            .OrderByDescending(c => c.Startdatum)
            .ToListAsync(ct);

        var vakansTitlar = (await db.Vacancies.ToListAsync(ct))
            .ToDictionary(v => v.Id, v => v.Titel);

        // Materialisera anställda och nyckla på Guid i minnet (undviker översättning av
        // value-converterad nyckel i SQL). Datamängden i en region-instans är liten.
        var anstalldIds = checklistor.Select(c => c.AnstallId).ToHashSet();
        var anstallda = (await db.Employees
                .Include(e => e.Anstallningar)
                .ToListAsync(ct))
            .Where(e => anstalldIds.Contains(e.Id.Value))
            .ToList();
        var namn = anstallda.ToDictionary(e => e.Id.Value, e => e.FulltNamn);
        var befattningar = anstallda.ToDictionary(
            e => e.Id.Value,
            e => e.Anstallningar.LastOrDefault()?.Befattningstitel ?? "—");

        return checklistor.Select(c =>
        {
            var totalt = c.Items.Count;
            var klara = c.Items.Count(i => i.Klar);
            var steg = c.Items.Select((i, index) => new OnboardingStegVy(index, i.Beskrivning, i.Klar)).ToList();
            return new OnboardingVy(
                c.Id,
                namn.GetValueOrDefault(c.AnstallId, "Okänd anställd"),
                befattningar.GetValueOrDefault(c.AnstallId, "—"),
                vakansTitlar.GetValueOrDefault(c.VakansId, "—"),
                DateOnly.FromDateTime(c.Startdatum),
                steg,
                totalt == 0 ? 0 : (int)Math.Round(klara * 100.0 / totalt));
        }).ToList();
    }

    /// <summary>Markerar ett onboarding-steg som klart.</summary>
    public async Task MarkeraOnboardingStegKlartAsync(Guid checklistId, int stegIndex, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var checklist = await db.OnboardingChecklists.FirstOrDefaultAsync(c => c.Id == checklistId, ct)
            ?? throw new InvalidOperationException($"Onboarding-checklista {checklistId} hittades inte.");
        checklist.MarkeraKlar(stegIndex);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<(Vacancy vakans, Application ansokan)> LaddaAsync(
        RegionHRDbContext db, Guid vakansId, Guid ansokanId, CancellationToken ct)
    {
        var vakans = await db.Vacancies
            .Include(v => v.Ansokngar)
            .FirstOrDefaultAsync(v => v.Id == vakansId, ct)
            ?? throw new InvalidOperationException($"Vakans {vakansId} hittades inte.");
        var ansokan = vakans.Ansokngar.FirstOrDefault(a => a.Id == ansokanId)
            ?? throw new InvalidOperationException($"Ansökan {ansokanId} hittades inte.");
        return (vakans, ansokan);
    }
}

/// <summary>Översikt över en vakans och dess ansökningar för pipeline-vyn.</summary>
public sealed record VakansOversikt(
    Guid Id,
    string Titel,
    string EnhetNamn,
    EmploymentType Anstallningsform,
    VacancyStatus Status,
    DateOnly SistaAnsokningsDag,
    decimal? LonMin,
    decimal? LonMax,
    Guid? TillsattAnsokanId,
    List<AnsokanOversikt> Ansokningar);

/// <summary>En ansökan i pipeline-vyn.</summary>
public sealed record AnsokanOversikt(
    Guid Id,
    string Namn,
    string Epost,
    ApplicationStatus Status,
    int? Poang,
    DateTime? IntervjuTidpunkt);

/// <summary>Resultat av en lyckad tillsättning → anställning.</summary>
public sealed record TillsattResultat(
    EmployeeId AnstalldId,
    string Namn,
    string Befattning,
    int OnboardingSteg);

/// <summary>En onboarding-checklista kopplad till en anställd, för onboarding-vyn.</summary>
public sealed record OnboardingVy(
    Guid ChecklistId,
    string AnstalldNamn,
    string Befattning,
    string VakansTitel,
    DateOnly Startdatum,
    List<OnboardingStegVy> Steg,
    int ProcentKlar);

/// <summary>Ett steg i en onboarding-checklista.</summary>
public sealed record OnboardingStegVy(int Index, string Beskrivning, bool Klar);
