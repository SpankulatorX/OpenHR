namespace RegionHR.GDPR.Klassa;

/// <summary>
/// En post i OpenHR:s informationsklassningsregister enligt KLASSA. Varje post beskriver en
/// informationsmängd (t.ex. "Lönedata", "Rehabärenden") och dess klassning i de tre
/// skyddsaspekterna konfidentialitet, riktighet och tillgänglighet (nivå 1–4), med motivering
/// per aspekt, skyddsåtgärder och tillämpligt lagrum. Registret utgör underlag för riskanalys
/// och val av säkerhetsåtgärder.
/// </summary>
public class InformationsklassPost
{
    public Guid Id { get; private set; }

    /// <summary>Informationsmängdens namn, t.ex. "Lönedata".</summary>
    public string Informationsmangd { get; private set; } = "";

    /// <summary>Kort beskrivning av vad mängden innehåller.</summary>
    public string Beskrivning { get; private set; } = "";

    public InformationsKategori Kategori { get; private set; }

    /// <summary>Vilket system-/modulområde i OpenHR mängden hör till (t.ex. "Lön", "HälsoSAM").</summary>
    public string Systemomrade { get; private set; } = "";

    public KonsekvensNiva Konfidentialitet { get; private set; }
    public KonsekvensNiva Riktighet { get; private set; }
    public KonsekvensNiva Tillganglighet { get; private set; }

    public string KonfidentialitetMotivering { get; private set; } = "";
    public string RiktighetMotivering { get; private set; } = "";
    public string TillganglighetMotivering { get; private set; } = "";

    /// <summary>Skyddsåtgärder som fritext (en per rad).</summary>
    public string Skyddsatgarder { get; private set; } = "";

    /// <summary>Tillämpligt lagrum, t.ex. "GDPR art. 9, OSL 25 kap., Arkivlagen".</summary>
    public string Lagrum { get; private set; } = "";

    /// <summary>True om posten är en fördefinierad standardklassning (seedad), inte manuellt tillagd.</summary>
    public bool ArFordefinierad { get; private set; }

    public DateTime SenastGranskad { get; private set; }
    public string? GranskadAv { get; private set; }

    private InformationsklassPost() { }

    public static InformationsklassPost Skapa(
        string informationsmangd,
        string beskrivning,
        InformationsKategori kategori,
        string systemomrade,
        KonsekvensNiva konfidentialitet,
        KonsekvensNiva riktighet,
        KonsekvensNiva tillganglighet,
        string konfidentialitetMotivering,
        string riktighetMotivering,
        string tillganglighetMotivering,
        string skyddsatgarder,
        string lagrum,
        bool arFordefinierad = false)
    {
        if (string.IsNullOrWhiteSpace(informationsmangd))
            throw new ArgumentException("Informationsmängd måste anges.", nameof(informationsmangd));

        return new InformationsklassPost
        {
            Id = Guid.NewGuid(),
            Informationsmangd = informationsmangd.Trim(),
            Beskrivning = beskrivning ?? "",
            Kategori = kategori,
            Systemomrade = systemomrade ?? "",
            Konfidentialitet = konfidentialitet,
            Riktighet = riktighet,
            Tillganglighet = tillganglighet,
            KonfidentialitetMotivering = konfidentialitetMotivering ?? "",
            RiktighetMotivering = riktighetMotivering ?? "",
            TillganglighetMotivering = tillganglighetMotivering ?? "",
            Skyddsatgarder = skyddsatgarder ?? "",
            Lagrum = lagrum ?? "",
            ArFordefinierad = arFordefinierad,
            SenastGranskad = DateTime.UtcNow
        };
    }

    /// <summary>Uppdaterar klassning och motiveringar samt registrerar ny granskningstidpunkt.</summary>
    public void Uppdatera(
        string beskrivning,
        InformationsKategori kategori,
        string systemomrade,
        KonsekvensNiva konfidentialitet,
        KonsekvensNiva riktighet,
        KonsekvensNiva tillganglighet,
        string konfidentialitetMotivering,
        string riktighetMotivering,
        string tillganglighetMotivering,
        string skyddsatgarder,
        string lagrum,
        string? granskadAv)
    {
        Beskrivning = beskrivning ?? "";
        Kategori = kategori;
        Systemomrade = systemomrade ?? "";
        Konfidentialitet = konfidentialitet;
        Riktighet = riktighet;
        Tillganglighet = tillganglighet;
        KonfidentialitetMotivering = konfidentialitetMotivering ?? "";
        RiktighetMotivering = riktighetMotivering ?? "";
        TillganglighetMotivering = tillganglighetMotivering ?? "";
        Skyddsatgarder = skyddsatgarder ?? "";
        Lagrum = lagrum ?? "";
        GranskadAv = granskadAv;
        SenastGranskad = DateTime.UtcNow;
    }

    /// <summary>Högsta konsekvensnivå av de tre aspekterna — mängdens sammanvägda skyddsnivå.</summary>
    public KonsekvensNiva HogstaKonsekvens =>
        KlassaRegler.HogstaNiva(Konfidentialitet, Riktighet, Tillganglighet);

    /// <summary>Kortprofil på formen "K3 R4 T2".</summary>
    public string Klassningsprofil =>
        $"K{(int)Konfidentialitet} R{(int)Riktighet} T{(int)Tillganglighet}";

    /// <summary>Är detta en känslig personuppgift enligt GDPR art. 9 (utifrån kategori)?</summary>
    public bool ArKansligPersonuppgift => KlassaRegler.ArKansligPersonuppgift(Kategori);

    /// <summary>Uppfyller klassningen kategorins rekommenderade KLASSA-miniminivåer?</summary>
    public bool UppfyllerRekommenderatKrav =>
        KlassaRegler.UppfyllerKrav(Kategori, Konfidentialitet, Riktighet, Tillganglighet);
}
