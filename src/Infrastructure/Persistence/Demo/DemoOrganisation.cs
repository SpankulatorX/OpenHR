using RegionHR.Core.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Persistence.Demo;

/// <summary>Ett yrke med lönespann (SEK/mån, 2026 års nivå) och AID-kod för statistik.</summary>
public sealed record Yrke(string Titel, int LonMin, int LonMax, string? AidKod = null);

/// <summary>Ett yrke med en relativ andel (vikt) inom en enhetsprofil.</summary>
public sealed record YrkeAndel(Yrke Yrke, int Andel);

/// <summary>Yrkessammansättningen för en enhetstyp (t.ex. sjukhusavdelning, vårdcentral).</summary>
public sealed record EnhetsProfil(string Namn, IReadOnlyList<YrkeAndel> Yrken);

/// <summary>En bemanningsbar enhet: id + profil + relativ storleksvikt.</summary>
public sealed record DemoEnhet(OrganizationId Id, string Namn, EnhetsProfil Profil, int Vikt);

/// <summary>
/// Katalog över yrken som förekommer i Region Örebro län, med realistiska
/// månadslönespann (2026) och AID-koder. Löner slås upp som ett intervall och
/// randomiseras per anställd i generatorn.
/// </summary>
public static class Yrken
{
    public static readonly Yrke Undersköterska = new("Undersköterska", 26000, 32000, "206011");
    public static readonly Yrke Skötare = new("Skötare", 27000, 33000, "206013");
    public static readonly Yrke Sjuksköterska = new("Sjuksköterska", 33000, 41000, "204012");
    public static readonly Yrke Specialistsjuksköterska = new("Specialistsjuksköterska", 39000, 47000, "203011");
    public static readonly Yrke Barnmorska = new("Barnmorska", 39000, 47000, "202011");
    public static readonly Yrke Distriktssköterska = new("Distriktssköterska", 37000, 45000, "203012");
    public static readonly Yrke Röntgensjuksköterska = new("Röntgensjuksköterska", 34000, 42000, "204013");
    public static readonly Yrke Överläkare = new("Överläkare", 75000, 115000, "101012");
    public static readonly Yrke STläkare = new("ST-läkare", 48000, 58000, "102012");
    public static readonly Yrke Underläkare = new("Underläkare", 40000, 47000, "102011");
    public static readonly Yrke Distriktsläkare = new("Distriktsläkare", 78000, 105000, "101013");
    public static readonly Yrke BiomedicinskAnalytiker = new("Biomedicinsk analytiker", 33000, 41000, "205011");
    public static readonly Yrke Tandläkare = new("Tandläkare", 46000, 68000, "301011");
    public static readonly Yrke Tandhygienist = new("Tandhygienist", 33000, 41000, "302011");
    public static readonly Yrke Tandsköterska = new("Tandsköterska", 27000, 33000, "303011");
    public static readonly Yrke Fysioterapeut = new("Fysioterapeut", 33000, 41000, "207011");
    public static readonly Yrke Arbetsterapeut = new("Arbetsterapeut", 33000, 40000, "208011");
    public static readonly Yrke Psykolog = new("Psykolog", 41000, 53000, "209011");
    public static readonly Yrke Kurator = new("Kurator", 35000, 43000, "210011");
    public static readonly Yrke Dietist = new("Dietist", 34000, 42000, "211011");
    public static readonly Yrke MedicinskSekreterare = new("Medicinsk sekreterare", 29000, 35000, "401011");
    public static readonly Yrke Vårdadministratör = new("Vårdadministratör", 28000, 34000, "401012");
    public static readonly Yrke HRspecialist = new("HR-specialist", 37000, 49000, "502011");
    public static readonly Yrke Ekonom = new("Ekonom", 37000, 51000, "503011");
    public static readonly Yrke ITtekniker = new("IT-tekniker", 36000, 48000, "504011");
    public static readonly Yrke Systemförvaltare = new("Systemförvaltare", 42000, 56000, "504012");
    public static readonly Yrke Inköpare = new("Inköpare", 39000, 51000, "505011");
    public static readonly Yrke Administratör = new("Administratör", 28000, 36000, "501011");
    public static readonly Yrke Handläggare = new("Handläggare", 34000, 46000, "501012");
    public static readonly Yrke Kommunikatör = new("Kommunikatör", 35000, 45000, "506011");
    public static readonly Yrke Verksamhetschef = new("Verksamhetschef", 62000, 95000, "601011");
    public static readonly Yrke Enhetschef = new("Enhetschef", 49000, 69000, "602011");
    public static readonly Yrke Avdelningschef = new("Avdelningschef", 52000, 74000, "602012");
    public static readonly Yrke Trafikplanerare = new("Trafikplanerare", 34000, 46000, "701011");
    public static readonly Yrke Bibliotekarie = new("Bibliotekarie", 32000, 41000, "801011");
    public static readonly Yrke Kulturhandläggare = new("Kulturhandläggare", 35000, 45000, "802011");
    public static readonly Yrke Lärare = new("Lärare", 34000, 45000, "803011");
    public static readonly Yrke Fastighetsskötare = new("Fastighetsskötare", 28000, 35000, "702011");
    public static readonly Yrke Lokalvårdare = new("Lokalvårdare", 25000, 30000, "703011");
    public static readonly Yrke Måltidsbiträde = new("Måltidsbiträde", 25000, 30000, "704011");
    public static readonly Yrke Kock = new("Kock", 27000, 34000, "704012");
}

/// <summary>Fördefinierade yrkessammansättningar per enhetstyp.</summary>
public static class Profiler
{
    private static YrkeAndel A(Yrke y, int andel) => new(y, andel);

    /// <summary>Somatisk vårdavdelning/klinik (medicin, kirurgi, ortopedi m.fl.).</summary>
    public static readonly EnhetsProfil Sjukhusvård = new("Sjukhusvård",
    [
        A(Yrken.Sjuksköterska, 34), A(Yrken.Undersköterska, 28), A(Yrken.Specialistsjuksköterska, 7),
        A(Yrken.Överläkare, 7), A(Yrken.STläkare, 5), A(Yrken.Underläkare, 3),
        A(Yrken.MedicinskSekreterare, 6), A(Yrken.Fysioterapeut, 3), A(Yrken.Arbetsterapeut, 2),
        A(Yrken.Vårdadministratör, 2), A(Yrken.Enhetschef, 2), A(Yrken.Avdelningschef, 1)
    ]);

    /// <summary>Anestesi/IVA/operation/kirurgiska specialiteter (specialistsjuksköterske-tungt).</summary>
    public static readonly EnhetsProfil IntensivOperation = new("Intensiv/Operation",
    [
        A(Yrken.Specialistsjuksköterska, 22), A(Yrken.Sjuksköterska, 18), A(Yrken.Undersköterska, 22),
        A(Yrken.Överläkare, 12), A(Yrken.STläkare, 6), A(Yrken.Underläkare, 2),
        A(Yrken.MedicinskSekreterare, 4), A(Yrken.Enhetschef, 2)
    ]);

    /// <summary>Akutmottagning/akutklinik.</summary>
    public static readonly EnhetsProfil Akutvård = new("Akutvård",
    [
        A(Yrken.Sjuksköterska, 32), A(Yrken.Undersköterska, 26), A(Yrken.Specialistsjuksköterska, 8),
        A(Yrken.Överläkare, 8), A(Yrken.STläkare, 6), A(Yrken.Underläkare, 5),
        A(Yrken.MedicinskSekreterare, 6), A(Yrken.Enhetschef, 2)
    ]);

    /// <summary>Kvinnoklinik/förlossning (barnmorske-tungt).</summary>
    public static readonly EnhetsProfil KvinnaBarn = new("Kvinna/Barn",
    [
        A(Yrken.Barnmorska, 30), A(Yrken.Sjuksköterska, 18), A(Yrken.Undersköterska, 20),
        A(Yrken.Överläkare, 10), A(Yrken.STläkare, 6), A(Yrken.Underläkare, 3),
        A(Yrken.MedicinskSekreterare, 6), A(Yrken.Enhetschef, 2)
    ]);

    /// <summary>Laboratoriemedicin (BMA-tungt).</summary>
    public static readonly EnhetsProfil Laboratorium = new("Laboratorium",
    [
        A(Yrken.BiomedicinskAnalytiker, 55), A(Yrken.Undersköterska, 15), A(Yrken.Överläkare, 8),
        A(Yrken.STläkare, 4), A(Yrken.Sjuksköterska, 6), A(Yrken.MedicinskSekreterare, 6),
        A(Yrken.Enhetschef, 2), A(Yrken.Avdelningschef, 1)
    ]);

    /// <summary>Radiologi/klinisk fysiologi (röntgensjuksköterske-tungt).</summary>
    public static readonly EnhetsProfil Radiologi = new("Radiologi",
    [
        A(Yrken.Röntgensjuksköterska, 45), A(Yrken.Undersköterska, 12), A(Yrken.Överläkare, 12),
        A(Yrken.STläkare, 6), A(Yrken.BiomedicinskAnalytiker, 5), A(Yrken.MedicinskSekreterare, 8),
        A(Yrken.Enhetschef, 2)
    ]);

    /// <summary>Psykiatri (allmän/rätts/BUP).</summary>
    public static readonly EnhetsProfil Psykiatri = new("Psykiatri",
    [
        A(Yrken.Skötare, 30), A(Yrken.Sjuksköterska, 24), A(Yrken.Undersköterska, 8),
        A(Yrken.Psykolog, 12), A(Yrken.Överläkare, 8), A(Yrken.STläkare, 4),
        A(Yrken.Kurator, 8), A(Yrken.MedicinskSekreterare, 4), A(Yrken.Enhetschef, 2)
    ]);

    /// <summary>Ambulanssjukvård.</summary>
    public static readonly EnhetsProfil Ambulans = new("Ambulans",
    [
        A(Yrken.Sjuksköterska, 40), A(Yrken.Specialistsjuksköterska, 25), A(Yrken.Undersköterska, 25),
        A(Yrken.Överläkare, 3), A(Yrken.Enhetschef, 2)
    ]);

    /// <summary>Habilitering och hjälpmedel.</summary>
    public static readonly EnhetsProfil Habilitering = new("Habilitering",
    [
        A(Yrken.Fysioterapeut, 18), A(Yrken.Arbetsterapeut, 16), A(Yrken.Psykolog, 14),
        A(Yrken.Kurator, 12), A(Yrken.Sjuksköterska, 10), A(Yrken.Handläggare, 8),
        A(Yrken.Administratör, 12), A(Yrken.Dietist, 4), A(Yrken.Enhetschef, 4), A(Yrken.Överläkare, 2)
    ]);

    /// <summary>Vårdcentral/primärvård.</summary>
    public static readonly EnhetsProfil Primärvård = new("Primärvård",
    [
        A(Yrken.Distriktssköterska, 15), A(Yrken.Sjuksköterska, 16), A(Yrken.Undersköterska, 18),
        A(Yrken.Distriktsläkare, 12), A(Yrken.STläkare, 4), A(Yrken.Fysioterapeut, 8),
        A(Yrken.Arbetsterapeut, 5), A(Yrken.Psykolog, 4), A(Yrken.Kurator, 4), A(Yrken.Dietist, 2),
        A(Yrken.Vårdadministratör, 10), A(Yrken.MedicinskSekreterare, 4), A(Yrken.Enhetschef, 2)
    ]);

    /// <summary>Folktandvårdsklinik.</summary>
    public static readonly EnhetsProfil Folktandvård = new("Folktandvård",
    [
        A(Yrken.Tandsköterska, 44), A(Yrken.Tandläkare, 26), A(Yrken.Tandhygienist, 20),
        A(Yrken.Administratör, 7), A(Yrken.Enhetschef, 3)
    ]);

    /// <summary>Administration/service (kansli, HR, ekonomi, IT, inköp).</summary>
    public static readonly EnhetsProfil AdminService = new("Admin/Service",
    [
        A(Yrken.Administratör, 24), A(Yrken.ITtekniker, 14), A(Yrken.Systemförvaltare, 8),
        A(Yrken.HRspecialist, 14), A(Yrken.Ekonom, 14), A(Yrken.Inköpare, 8),
        A(Yrken.Kommunikatör, 8), A(Yrken.Enhetschef, 6), A(Yrken.Verksamhetschef, 4)
    ]);

    /// <summary>Regionfastigheter (drift/lokalvård).</summary>
    public static readonly EnhetsProfil Fastighet = new("Fastighet",
    [
        A(Yrken.Fastighetsskötare, 42), A(Yrken.Lokalvårdare, 26), A(Yrken.Administratör, 12),
        A(Yrken.Enhetschef, 5), A(Yrken.Ekonom, 5), A(Yrken.Inköpare, 5),
        A(Yrken.Kommunikatör, 2), A(Yrken.Verksamhetschef, 3)
    ]);

    /// <summary>Måltidsservice/kost.</summary>
    public static readonly EnhetsProfil Måltid = new("Måltid",
    [
        A(Yrken.Måltidsbiträde, 50), A(Yrken.Kock, 26), A(Yrken.Administratör, 10),
        A(Yrken.Enhetschef, 6), A(Yrken.Inköpare, 4), A(Yrken.Verksamhetschef, 4)
    ]);

    /// <summary>Regional utveckling (näringsliv, kompetens, infrastruktur, folkhälsa, energi).</summary>
    public static readonly EnhetsProfil RegionalUtveckling = new("Regional utveckling",
    [
        A(Yrken.Handläggare, 34), A(Yrken.Administratör, 18), A(Yrken.Kommunikatör, 12),
        A(Yrken.Ekonom, 10), A(Yrken.Enhetschef, 8), A(Yrken.Verksamhetschef, 4), A(Yrken.ITtekniker, 4)
    ]);

    /// <summary>Kultur och bildning (bibliotek, scenkonst, museum, kulturenhet).</summary>
    public static readonly EnhetsProfil Kultur = new("Kultur",
    [
        A(Yrken.Kulturhandläggare, 20), A(Yrken.Bibliotekarie, 22), A(Yrken.Administratör, 18),
        A(Yrken.Handläggare, 9), A(Yrken.Kommunikatör, 10), A(Yrken.Enhetschef, 6), A(Yrken.Verksamhetschef, 3)
    ]);

    /// <summary>Folkhögskola.</summary>
    public static readonly EnhetsProfil Folkhögskola = new("Folkhögskola",
    [
        A(Yrken.Lärare, 55), A(Yrken.Administratör, 22), A(Yrken.Enhetschef, 6),
        A(Yrken.Lokalvårdare, 8), A(Yrken.Kommunikatör, 4), A(Yrken.Verksamhetschef, 5)
    ]);

    /// <summary>Kollektivtrafik/Länstrafiken (planering och administration; drift upphandlas).</summary>
    public static readonly EnhetsProfil Kollektivtrafik = new("Kollektivtrafik",
    [
        A(Yrken.Trafikplanerare, 30), A(Yrken.Administratör, 26), A(Yrken.Kommunikatör, 14),
        A(Yrken.Ekonom, 10), A(Yrken.Handläggare, 10), A(Yrken.Enhetschef, 6), A(Yrken.Verksamhetschef, 4)
    ]);
}

/// <summary>
/// Bygger ut Region Örebro läns organisationsträd realistiskt utifrån den faktiska
/// organisationen: förvaltningar → sjukhus (USÖ, Karlskoga, Lindesberg) med kliniker,
/// vårdcentraler, folktandvårdskliniker, service-, kultur- och kollektivtrafikenheter.
///
/// De befintliga demo-enheterna (Region Örebro län + Universitetssjukhuset Örebro med
/// Avd 32/33, Akuten, IVA) återanvänds som ankare: USÖ:s kliniker hängs som syskon till
/// avdelningarna under det redan seedade USÖ-noden, och de nya förvaltningarna hängs
/// under den redan seedade regionnoden. Inga befintliga enheter muteras.
/// </summary>
public static class DemoOrganisation
{
    private static readonly DateOnly GiltigFran = new(2006, 1, 1);

    public static (List<OrganizationUnit> Enheter, List<DemoEnhet> Bemanningsbara) Bygg(
        OrganizationId regionId, OrganizationId usoId)
    {
        var enheter = new List<OrganizationUnit>();
        var bemanningsbara = new List<DemoEnhet>();
        var ks = 21000; // Kostnadsställe-räknare — startar över de redan använda (10000/20000/2003x/2005x/2006x)

        OrganizationUnit Skapa(string namn, OrganizationUnitType typ, OrganizationId parent,
            EnhetsProfil? profil = null, int vikt = 0)
        {
            var enhet = OrganizationUnit.Skapa(namn, typ, (ks++).ToString(), GiltigFran, overordnadEnhetId: parent);
            enheter.Add(enhet);
            if (profil is not null && vikt > 0)
                bemanningsbara.Add(new DemoEnhet(enhet.Id, namn, profil, vikt));
            return enhet;
        }

        void SkapaFlera(OrganizationId parent, OrganizationUnitType typ,
            IEnumerable<(string Namn, EnhetsProfil Profil, int Vikt)> barn)
        {
            foreach (var (namn, profil, vikt) in barn)
                Skapa(namn, typ, parent, profil, vikt);
        }

        // === USÖ-kliniker: hängs under den redan seedade USÖ-noden (syskon till Avd 32/33/Akuten/IVA) ===
        SkapaFlera(usoId, OrganizationUnitType.Avdelning, new (string, EnhetsProfil, int)[]
        {
            ("Akutkliniken", Profiler.Akutvård, 190),
            ("Anestesi- och intensivvårdskliniken", Profiler.IntensivOperation, 210),
            ("Arbets- och miljömedicinska kliniken", Profiler.Sjukhusvård, 45),
            ("Barn- och ungdomskliniken", Profiler.Sjukhusvård, 150),
            ("Geriatriska kliniken", Profiler.Sjukhusvård, 130),
            ("Handkirurgiska kliniken", Profiler.IntensivOperation, 70),
            ("Hudkliniken", Profiler.Sjukhusvård, 55),
            ("Infektionskliniken", Profiler.Sjukhusvård, 110),
            ("Kirurgiska kliniken", Profiler.IntensivOperation, 220),
            ("Kvinnokliniken", Profiler.KvinnaBarn, 200),
            ("Kärl-thoraxkliniken", Profiler.IntensivOperation, 130),
            ("Medicinska kliniken", Profiler.Sjukhusvård, 240),
            ("Neuro- och rehabiliteringsmedicinska kliniken", Profiler.Sjukhusvård, 140),
            ("Njurmedicinska kliniken", Profiler.Sjukhusvård, 90),
            ("Onkologiska kliniken", Profiler.Sjukhusvård, 150),
            ("Ortopediska kliniken", Profiler.IntensivOperation, 180),
            ("Plastik- och käkkirurgiska kliniken", Profiler.IntensivOperation, 90),
            ("Reumatologiska kliniken", Profiler.Sjukhusvård, 70),
            ("Urologiska kliniken", Profiler.IntensivOperation, 90),
            ("Ögonkliniken", Profiler.Sjukhusvård, 90),
            ("Öron-, näs- och halskliniken", Profiler.Sjukhusvård, 100),
            ("Klinisk fysiologi och nuklearmedicin", Profiler.Radiologi, 90),
            ("Röntgenkliniken", Profiler.Radiologi, 190),
            ("Laboratoriemedicinska länskliniken", Profiler.Laboratorium, 260),
            ("Ambulanssjukvården", Profiler.Ambulans, 200),
            ("Allmänpsykiatriska kliniken", Profiler.Psykiatri, 220),
            ("Rättspsykiatriska kliniken", Profiler.Psykiatri, 150),
            ("Barn- och ungdomspsykiatriska kliniken (BUP)", Profiler.Psykiatri, 120),
        });

        // === Hälso- och sjukvårdsförvaltningen → Karlskoga + Lindesberg + Habilitering ===
        var hsf = Skapa("Hälso- och sjukvårdsförvaltningen", OrganizationUnitType.Forvaltning, regionId);

        var karlskoga = Skapa("Karlskoga lasarett", OrganizationUnitType.Verksamhet, hsf.Id);
        SkapaFlera(karlskoga.Id, OrganizationUnitType.Avdelning, new (string, EnhetsProfil, int)[]
        {
            ("Akutkliniken Karlskoga", Profiler.Akutvård, 90),
            ("Medicinkliniken Karlskoga", Profiler.Sjukhusvård, 140),
            ("Kirurgkliniken Karlskoga", Profiler.IntensivOperation, 110),
            ("Ortopedkliniken Karlskoga", Profiler.IntensivOperation, 90),
            ("Anestesi- och intensivvårdskliniken Karlskoga", Profiler.IntensivOperation, 100),
            ("Röntgenkliniken Karlskoga", Profiler.Radiologi, 70),
            ("Specialistmottagningar Karlskoga", Profiler.Sjukhusvård, 80),
        });

        var lindesberg = Skapa("Lindesbergs lasarett", OrganizationUnitType.Verksamhet, hsf.Id);
        SkapaFlera(lindesberg.Id, OrganizationUnitType.Avdelning, new (string, EnhetsProfil, int)[]
        {
            ("Akutkliniken Lindesberg", Profiler.Akutvård, 70),
            ("Kliniken för medicin och geriatrik Lindesberg", Profiler.Sjukhusvård, 120),
            ("Kirurgiska kliniken Lindesberg", Profiler.IntensivOperation, 90),
            ("Ortopedkliniken Lindesberg", Profiler.IntensivOperation, 80),
            ("Anestesi- och intensivvårdskliniken Lindesberg", Profiler.IntensivOperation, 80),
            ("Röntgenkliniken Lindesberg", Profiler.Radiologi, 55),
            ("Specialistmottagningar Lindesberg", Profiler.Sjukhusvård, 70),
        });

        Skapa("Habilitering och hjälpmedel", OrganizationUnitType.Enhet, hsf.Id, Profiler.Habilitering, 220);

        // === Område Nära vård / Primärvård → alla vårdcentraler + jourcentraler + ungdomsmottagningar ===
        var narvard = Skapa("Område Nära vård / Primärvård", OrganizationUnitType.Forvaltning, regionId);
        SkapaFlera(narvard.Id, OrganizationUnitType.Enhet, new (string, EnhetsProfil, int)[]
        {
            ("Adolfsbergs vårdcentral", Profiler.Primärvård, 60),
            ("Brickebackens vårdcentral", Profiler.Primärvård, 55),
            ("Karla vårdcentral", Profiler.Primärvård, 55),
            ("Lillåns vårdcentral", Profiler.Primärvård, 50),
            ("Mikaeli vårdcentral", Profiler.Primärvård, 60),
            ("Odensbackens vårdcentral", Profiler.Primärvård, 40),
            ("Olaus Petri vårdcentral", Profiler.Primärvård, 60),
            ("Skebäcks vårdcentral", Profiler.Primärvård, 55),
            ("Tybble vårdcentral", Profiler.Primärvård, 60),
            ("Varberga vårdcentral", Profiler.Primärvård, 55),
            ("Ängens vårdcentral", Profiler.Primärvård, 55),
            ("Askersunds vårdcentral", Profiler.Primärvård, 45),
            ("Hallsbergs vårdcentral", Profiler.Primärvård, 50),
            ("Kumla vårdcentral", Profiler.Primärvård, 60),
            ("Baggängens vårdcentral", Profiler.Primärvård, 50),
            ("Brickegårdens vårdcentral", Profiler.Primärvård, 50),
            ("Karolina vårdcentral", Profiler.Primärvård, 55),
            ("Pilgårdens vårdcentral", Profiler.Primärvård, 40),
            ("Laxå vårdcentral", Profiler.Primärvård, 35),
            ("Lindesbergs vårdcentral", Profiler.Primärvård, 55),
            ("Storå vårdcentral", Profiler.Primärvård, 30),
            ("Freja vårdcentral", Profiler.Primärvård, 40),
            ("Nora vårdcentral", Profiler.Primärvård, 45),
            ("Hällefors vårdcentral", Profiler.Primärvård, 40),
            ("Kopparbergs vårdcentral", Profiler.Primärvård, 35),
            ("Vårdcentralsjouren Örebro", Profiler.Primärvård, 25),
            ("Vårdcentralsjouren södra länsdelen", Profiler.Primärvård, 20),
            ("Vårdcentralsjouren västra länsdelen", Profiler.Primärvård, 20),
            ("Vårdcentralsjouren norra länsdelen", Profiler.Primärvård, 20),
            ("Ungdomsmottagningar i länet", Profiler.Primärvård, 25),
        });

        // === Folktandvården → alla folktandvårdskliniker + specialisttandvård ===
        var ftv = Skapa("Folktandvården", OrganizationUnitType.Forvaltning, regionId);
        SkapaFlera(ftv.Id, OrganizationUnitType.Enhet, new (string, EnhetsProfil, int)[]
        {
            ("Folktandvården Adolfsberg", Profiler.Folktandvård, 30),
            ("Folktandvården Brickebacken", Profiler.Folktandvård, 26),
            ("Folktandvården Eyra", Profiler.Folktandvård, 28),
            ("Folktandvården Haga", Profiler.Folktandvård, 30),
            ("Folktandvården Hertig Karl", Profiler.Folktandvård, 30),
            ("Folktandvården Lillån", Profiler.Folktandvård, 24),
            ("Folktandvården Sofia", Profiler.Folktandvård, 26),
            ("Folktandvården Wivallius", Profiler.Folktandvård, 26),
            ("Folktandvården Odensbacken", Profiler.Folktandvård, 18),
            ("Folktandvården Kumla", Profiler.Folktandvård, 28),
            ("Folktandvården Hallsberg", Profiler.Folktandvård, 24),
            ("Folktandvården Askersund", Profiler.Folktandvård, 20),
            ("Folktandvården Laxå", Profiler.Folktandvård, 16),
            ("Folktandvården Lekeberg", Profiler.Folktandvård, 18),
            ("Folktandvården Karlskoga", Profiler.Folktandvård, 32),
            ("Folktandvården Degerfors", Profiler.Folktandvård, 20),
            ("Folktandvården Lindesberg", Profiler.Folktandvård, 28),
            ("Folktandvården Frövi", Profiler.Folktandvård, 18),
            ("Folktandvården Nora", Profiler.Folktandvård, 22),
            ("Folktandvården Hällefors", Profiler.Folktandvård, 18),
            ("Folktandvården Kopparberg", Profiler.Folktandvård, 16),
            ("Folktandvården Jourklinik", Profiler.Folktandvård, 20),
            ("Specialisttandvården", Profiler.Folktandvård, 60),
            ("Odontologisk digital klinik / Kariesmottagning", Profiler.Folktandvård, 20),
        });

        // === Regionservice → IT, fastigheter, lön, inköp, servicedesk, måltid ===
        var regionservice = Skapa("Regionservice", OrganizationUnitType.Forvaltning, regionId);
        Skapa("Området IT", OrganizationUnitType.Enhet, regionservice.Id, Profiler.AdminService, 160);
        Skapa("Regionfastigheter", OrganizationUnitType.Enhet, regionservice.Id, Profiler.Fastighet, 220);
        Skapa("Lönecenter", OrganizationUnitType.Enhet, regionservice.Id, Profiler.AdminService, 60);
        Skapa("Upphandling och inköp", OrganizationUnitType.Enhet, regionservice.Id, Profiler.AdminService, 70);
        Skapa("Servicedesk", OrganizationUnitType.Enhet, regionservice.Id, Profiler.AdminService, 60);
        Skapa("Måltidsservice", OrganizationUnitType.Enhet, regionservice.Id, Profiler.Måltid, 90);

        // === Regionkansliet → stab, HR, ekonomi, kommunikation ===
        var kansli = Skapa("Regionkansliet", OrganizationUnitType.Forvaltning, regionId);
        Skapa("Regionkansliet stab", OrganizationUnitType.Enhet, kansli.Id, Profiler.AdminService, 90);
        Skapa("HR-avdelningen", OrganizationUnitType.Enhet, kansli.Id, Profiler.AdminService, 110);
        Skapa("Ekonomiavdelningen", OrganizationUnitType.Enhet, kansli.Id, Profiler.AdminService, 90);
        Skapa("Kommunikationsavdelningen", OrganizationUnitType.Enhet, kansli.Id, Profiler.AdminService, 45);

        // === Regional utvecklingsförvaltningen → utveckling, energi, folkhälsa ===
        var regutv = Skapa("Regional utvecklingsförvaltningen", OrganizationUnitType.Forvaltning, regionId);
        Skapa("Regional utveckling", OrganizationUnitType.Enhet, regutv.Id, Profiler.RegionalUtveckling, 90);
        Skapa("Energikontoret", OrganizationUnitType.Enhet, regutv.Id, Profiler.RegionalUtveckling, 25);
        Skapa("Folkhälsoenheten", OrganizationUnitType.Enhet, regutv.Id, Profiler.RegionalUtveckling, 30);

        // === Kultur och bildning → kultur, bibliotek, scenkonst, folkhögskolor ===
        var kultur = Skapa("Kultur och bildning", OrganizationUnitType.Forvaltning, regionId);
        Skapa("Kulturenheten", OrganizationUnitType.Enhet, kultur.Id, Profiler.Kultur, 60);
        Skapa("Regionbibliotek Örebro län", OrganizationUnitType.Enhet, kultur.Id, Profiler.Kultur, 40);
        Skapa("Örebro länsteater", OrganizationUnitType.Enhet, kultur.Id, Profiler.Kultur, 45);
        Skapa("Örebro Konserthus / Länsmusiken", OrganizationUnitType.Enhet, kultur.Id, Profiler.Kultur, 45);
        Skapa("Fellingsbro folkhögskola", OrganizationUnitType.Enhet, kultur.Id, Profiler.Folkhögskola, 55);
        Skapa("Kävesta folkhögskola", OrganizationUnitType.Enhet, kultur.Id, Profiler.Folkhögskola, 50);

        // === Kollektivtrafik / Länstrafiken ===
        var trafik = Skapa("Kollektivtrafikförvaltningen", OrganizationUnitType.Forvaltning, regionId);
        Skapa("Länstrafiken Örebro", OrganizationUnitType.Enhet, trafik.Id, Profiler.Kollektivtrafik, 90);
        Skapa("Kollektivtrafik stab", OrganizationUnitType.Enhet, trafik.Id, Profiler.Kollektivtrafik, 40);

        return (enheter, bemanningsbara);
    }
}
