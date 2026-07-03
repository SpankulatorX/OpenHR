using System.Text;
using Microsoft.EntityFrameworkCore;
using RegionHR.Core.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Persistence.Demo;

/// <summary>
/// Procedurell generator som befolkar demo-databasen med ~11 000 anställda fördelade
/// över Region Örebro läns organisationsträd (byggt av <see cref="DemoOrganisation"/>).
///
/// Designbeslut:
/// <list type="bullet">
/// <item>Använder domänfabrikerna (<c>Employee.Skapa</c> + <c>LaggTillAnstallning</c>)
///   så att alla LAS-/arbetsrättsinvarianter valideras precis som i produktion.</item>
/// <item>Giltiga, unika personnummer via <see cref="PersonnummerGenerator"/> (beräknad
///   Luhn-siffra, könskonsekvent paritet).</item>
/// <item>Prestanda: <c>AddRange</c> + <c>SaveChangesAsync</c> i batchar (default 1 500),
///   med <c>ChangeTracker.Clear()</c> mellan batchar för att hålla minnet konstant.</item>
/// <item>Endast personal-/anställningsposter skapas för massan — inga scheman,
///   lönekörningar eller ledigheter. Domänevents nollställs per anställd innan spar
///   (<c>ClearDomainEvents</c>) så att den per-anställnings LAS-auto-kedjan och övriga
///   sidoeffekter inte triggas 11 000 gånger vid seed. De handplockade demo-användarna
///   ovanför i seeden behåller sina events och sin rika data helt orörda.</item>
/// </list>
/// </summary>
public static class DemoBefolkningsGenerator
{
    /// <summary>Målstorlek på den genererade demo-befolkningen. Justera fritt.</summary>
    public const int DEMO_BEFOLKNING = 11_000;

    /// <summary>Antal anställda per SaveChanges-batch.</summary>
    private const int BATCH = 1_500;

    /// <summary>
    /// Bygger ut organisationsträdet och genererar <paramref name="antal"/> anställda.
    /// Anropas från <c>SeedData.SeedAsync</c> EFTER den handplockade demo-datan sparats.
    /// </summary>
    /// <param name="db">Kontext (redan sparad handplockad seed — säkert att clear:a change tracker).</param>
    /// <param name="regionId">Den redan seedade regionnoden (nya förvaltningar hängs här).</param>
    /// <param name="usoId">Den redan seedade USÖ-noden (nya kliniker hängs här).</param>
    /// <param name="abAvtalsId">DB-id för AB-kollektivavtalet som anställningarna kopplas till.</param>
    /// <param name="reserveradePersonnummer">Personnummer som redan används (handplockade demo-användare).</param>
    /// <param name="antal">Antal att generera (default <see cref="DEMO_BEFOLKNING"/>).</param>
    /// <param name="rng">Slumpkälla (injiceras för deterministiska tester).</param>
    public static async Task GenereraAsync(
        RegionHRDbContext db,
        OrganizationId regionId,
        OrganizationId usoId,
        CollectiveAgreementId? abAvtalsId,
        IEnumerable<string>? reserveradePersonnummer = null,
        int antal = DEMO_BEFOLKNING,
        Random? rng = null)
    {
        rng ??= new Random();
        var idag = DateOnly.FromDateTime(DateTime.Today);

        // 1) Bygg och spara det utökade organisationsträdet (en SaveChanges).
        var (enheter, bemanningsbara) = DemoOrganisation.Bygg(regionId, usoId);
        db.OrganizationUnits.AddRange(enheter);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        if (antal <= 0 || bemanningsbara.Count == 0) return;

        // 2) Fördela antalet över enheterna enligt relativ storleksvikt.
        var fordelning = BeraknaFordelning(bemanningsbara, antal);

        var pnrGen = new PersonnummerGenerator(rng, reserveradePersonnummer);
        var anvandaEpost = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var batch = new List<Employee>(BATCH);
        var lopnummer = 0;

        for (var ei = 0; ei < bemanningsbara.Count; ei++)
        {
            var enhet = bemanningsbara[ei];
            var antalPaEnhet = fordelning[ei];

            for (var k = 0; k < antalPaEnhet; k++)
            {
                batch.Add(SkapaAnstalld(enhet, ++lopnummer, pnrGen, anvandaEpost, abAvtalsId, rng, idag));

                if (batch.Count >= BATCH)
                    await SparaBatchAsync(db, batch);
            }
        }

        if (batch.Count > 0)
            await SparaBatchAsync(db, batch);
    }

    private static async Task SparaBatchAsync(RegionHRDbContext db, List<Employee> batch)
    {
        db.Employees.AddRange(batch);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        batch.Clear();
    }

    /// <summary>
    /// Skapar EN anställd med giltigt personnummer, kontakt-/skatteuppgifter och en
    /// anställning (yrke, lön, sysselsättningsgrad, anställningsform) via domänfabrikerna.
    /// Domänevents nollställs innan retur så att bulk-seeden inte triggar sidoeffekter.
    /// </summary>
    private static Employee SkapaAnstalld(
        DemoEnhet enhet, int lopnummer, PersonnummerGenerator pnrGen,
        HashSet<string> anvandaEpost, CollectiveAgreementId? abAvtalsId, Random rng, DateOnly idag)
    {
        var arKvinna = rng.NextDouble() < 0.72; // regionen är kvinnodominerad (~72 % kvinnor)
        var fornamn = arKvinna
            ? SvenskaNamn.Kvinnonamn[rng.Next(SvenskaNamn.Kvinnonamn.Length)]
            : SvenskaNamn.Mansnamn[rng.Next(SvenskaNamn.Mansnamn.Length)];
        var efternamn = SvenskaNamn.Efternamn[rng.Next(SvenskaNamn.Efternamn.Length)];

        var alder = Alder(rng);
        var pnr = pnrGen.NastaUnikt(alder, arKvinna, idag);

        var employee = Employee.Skapa(pnr, fornamn, efternamn);
        employee.UppdateraKontaktuppgifter(
            UnikEpost(fornamn, efternamn, lopnummer, anvandaEpost),
            $"070-{rng.Next(100, 1000):D3} {rng.Next(0, 100):D2} {rng.Next(0, 100):D2}",
            null);
        // Örebro → Skatteverkets tabell 34, kolumn 1 (samma som handplockad seed).
        employee.UppdateraSkatteuppgifter(34, 1, "Örebro", 33.65m, harKyrkoavgift: false, kyrkoavgiftssats: null);

        var yrke = ValjYrke(enhet.Profil, rng);
        var lon = Money.SEK(Math.Round(rng.Next(yrke.LonMin, yrke.LonMax + 1) / 100m) * 100m);
        var (form, grad, start, slut) = Anstallningsvillkor(rng, pnr.BirthDate, idag);

        employee.LaggTillAnstallning(
            enhet.Id, form, CollectiveAgreementType.AB, lon, grad, start, slut,
            aidKod: yrke.AidKod, befattningstitel: yrke.Titel, avtalsId: abAvtalsId);

        // Nollställ domänevents → ingen per-anställnings LAS-auto-kedja/dispatch vid bulk-seed.
        employee.ClearDomainEvents();
        return employee;
    }

    /// <summary>
    /// Fördelar <paramref name="antal"/> platser över enheterna proportionellt mot vikt.
    /// Summan av returvektorn är EXAKT <paramref name="antal"/> (största-rest-metoden).
    /// Ren funktion — testbar utan databas.
    /// </summary>
    public static int[] BeraknaFordelning(IReadOnlyList<DemoEnhet> enheter, int antal)
    {
        var resultat = new int[enheter.Count];
        if (enheter.Count == 0 || antal <= 0) return resultat;

        long totalVikt = enheter.Sum(e => (long)e.Vikt);
        if (totalVikt <= 0)
        {
            // Ingen vikt → jämn fördelning.
            for (var i = 0; i < enheter.Count; i++)
                resultat[i] = antal / enheter.Count + (i < antal % enheter.Count ? 1 : 0);
            return resultat;
        }

        var rester = new (int Index, double Brakdel)[enheter.Count];
        var tilldelat = 0;
        for (var i = 0; i < enheter.Count; i++)
        {
            var exakt = (double)enheter[i].Vikt / totalVikt * antal;
            var golv = (int)Math.Floor(exakt);
            resultat[i] = golv;
            tilldelat += golv;
            rester[i] = (i, exakt - golv);
        }

        // Dela ut återstoden till enheterna med störst bråkdel.
        var kvar = antal - tilldelat;
        foreach (var (index, _) in rester.OrderByDescending(r => r.Brakdel).Take(Math.Max(0, kvar)))
            resultat[index]++;

        return resultat;
    }

    /// <summary>Åldersfördelning 20–67 med central tyngdpunkt (~44) via medel av två uniforma dragningar.</summary>
    private static int Alder(Random rng) => (rng.Next(20, 68) + rng.Next(20, 68)) / 2;

    /// <summary>Väljer ett yrke ur profilen med viktad slump.</summary>
    private static Yrke ValjYrke(EnhetsProfil profil, Random rng)
    {
        var total = profil.Yrken.Sum(y => y.Andel);
        var traff = rng.Next(0, total);
        var ackumulerat = 0;
        foreach (var ya in profil.Yrken)
        {
            ackumulerat += ya.Andel;
            if (traff < ackumulerat) return ya.Yrke;
        }
        return profil.Yrken[^1].Yrke;
    }

    /// <summary>
    /// Bestämmer anställningsform, sysselsättningsgrad och giltighetsperiod inom
    /// arbetsrättens ramar (LAS): mest tillsvidare (~85 %), en andel visstid som alltid
    /// får ett slutdatum, och provanställning kapad till max 6 månader. Startdatum
    /// sprids bakåt men aldrig före 18 års ålder eller in i framtiden.
    /// </summary>
    private static (EmploymentType Form, Percentage Grad, DateOnly Start, DateOnly? Slut) Anstallningsvillkor(
        Random rng, DateOnly fodelsedatum, DateOnly idag)
    {
        // Tidigast möjliga anställningsstart: 18-årsdagen (aldrig i framtiden givet ålder ≥ 20).
        var tidigast = fodelsedatum.AddYears(18);
        if (tidigast > idag) tidigast = idag.AddYears(-1);
        var maxBakatDagar = Math.Max(1, idag.DayNumber - tidigast.DayNumber);

        var g = rng.NextDouble();
        var gradVarde = g < 0.74 ? 100m : g < 0.82 ? 75m : g < 0.88 ? 80m
            : g < 0.93 ? 50m : g < 0.96 ? 90m : g < 0.98 ? 60m : 25m;
        var grad = new Percentage(gradVarde);

        var r = rng.NextDouble();
        EmploymentType form;
        DateOnly start;
        DateOnly? slut = null;

        if (r < 0.85)
        {
            // Tillsvidare — spridd upp till 30 år bakåt (kapat av 18-årsgränsen).
            form = EmploymentType.Tillsvidare;
            var spann = Math.Min(maxBakatDagar, 30 * 365);
            start = idag.AddDays(-rng.Next(0, spann + 1));
        }
        else if (r < 0.93)
        {
            form = EmploymentType.Vikariat;
            start = SenareStart(rng, tidigast, idag, 3 * 365);
            slut = start.AddMonths(rng.Next(3, 19));
        }
        else if (r < 0.97)
        {
            form = EmploymentType.SAVA;
            start = SenareStart(rng, tidigast, idag, 2 * 365);
            slut = start.AddMonths(rng.Next(3, 13));
        }
        else
        {
            form = EmploymentType.Provanstallning;
            start = SenareStart(rng, tidigast, idag, 150);
            slut = start.AddMonths(Employment.MaxProvanstallningManader); // exakt 6 mån (LAS 6 §)
        }

        return (form, grad, start, slut);
    }

    /// <summary>Ett relativt sent startdatum (för visstid), aldrig före 18-årsgränsen.</summary>
    private static DateOnly SenareStart(Random rng, DateOnly tidigast, DateOnly idag, int maxDagarBakat)
    {
        var spann = Math.Min(Math.Max(1, idag.DayNumber - tidigast.DayNumber), maxDagarBakat);
        var start = idag.AddDays(-rng.Next(0, spann + 1));
        return start < tidigast ? tidigast : start;
    }

    /// <summary>Bygger en e-postadress och gör den unik med löpnummer-suffix vid krock.</summary>
    private static string UnikEpost(string fornamn, string efternamn, int lopnummer, HashSet<string> anvanda)
    {
        var bas = $"{Translitterera(fornamn)}.{Translitterera(efternamn)}";
        var epost = $"{bas}@regionorebrolan.se";
        if (!anvanda.Add(epost))
        {
            epost = $"{bas}{lopnummer}@regionorebrolan.se";
            anvanda.Add(epost);
        }
        return epost;
    }

    /// <summary>Translittererar svenska tecken till a–z för e-postlokaldelar.</summary>
    private static string Translitterera(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.ToLowerInvariant())
        {
            sb.Append(c switch
            {
                'å' or 'ä' or 'á' or 'à' => "a",
                'ö' or 'ø' or 'ó' => "o",
                'é' or 'è' or 'ê' => "e",
                'ü' or 'ú' => "u",
                'æ' => "ae",
                ' ' or '-' or '\'' => "",
                _ => char.IsLetterOrDigit(c) ? c.ToString() : ""
            });
        }
        return sb.ToString();
    }
}
