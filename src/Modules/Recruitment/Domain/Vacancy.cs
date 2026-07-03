using RegionHR.SharedKernel.Abstractions;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Recruitment.Domain;

public sealed class Vacancy : AggregateRoot<Guid>
{
    public OrganizationId EnhetId { get; private set; }
    public string Titel { get; private set; } = string.Empty;
    public string Beskrivning { get; private set; } = string.Empty;
    public EmploymentType Anstallningsform { get; private set; }
    public Money? Lonespann_Min { get; private set; }
    public Money? Lonespann_Max { get; private set; }
    public DateOnly SistaAnsokningsDag { get; private set; }
    public VacancyStatus Status { get; private set; }
    public bool PubliceradExternt { get; private set; }
    public bool PubliceradPlatsbanken { get; private set; }
    public Guid? TillsattAnsokanId { get; private set; }

    private readonly List<Application> _ansokngar = [];
    public IReadOnlyList<Application> Ansokngar => _ansokngar.AsReadOnly();

    private Vacancy() { }

    public static Vacancy Skapa(
        OrganizationId enhetId, string titel, string beskrivning,
        EmploymentType anstallningsform, DateOnly sistadag)
    {
        return new Vacancy
        {
            Id = Guid.NewGuid(),
            EnhetId = enhetId,
            Titel = titel,
            Beskrivning = beskrivning,
            Anstallningsform = anstallningsform,
            SistaAnsokningsDag = sistadag,
            Status = VacancyStatus.Utkast
        };
    }

    /// <summary>
    /// Sätter (eller rensar) vakansens lönespann. Ett angivet minimum får inte överstiga maximum.
    /// </summary>
    public void SattLonespann(Money? min, Money? max)
    {
        if (min is not null && max is not null && min.Value.Amount > max.Value.Amount)
            throw new ArgumentException("Lägsta lön kan inte vara högre än högsta lön.");

        Lonespann_Min = min;
        Lonespann_Max = max;
    }

    public void Publicera(bool externt = true, bool platsbanken = false)
    {
        if (Status == VacancyStatus.Tillsatt)
            throw new InvalidOperationException("En tillsatt vakans kan inte publiceras om.");

        Status = VacancyStatus.Publicerad;
        PubliceradExternt = externt;
        PubliceradPlatsbanken = platsbanken;
    }

    public Application TaEmotAnsokan(string namn, string epost, string? cvFilId = null)
    {
        if (Status != VacancyStatus.Publicerad)
            throw new InvalidOperationException("Kan inte ta emot ansökningar för icke-publicerad vakans");

        var application = new Application
        {
            Namn = namn,
            Epost = epost,
            CVFilId = cvFilId,
            Status = ApplicationStatus.Mottagen
        };
        _ansokngar.Add(application);
        return application;
    }

    /// <summary>
    /// Stänger vakansen så att inga fler ansökningar kan tas emot.
    /// </summary>
    public void Stang()
    {
        if (Status != VacancyStatus.Publicerad)
            throw new InvalidOperationException("Kan bara stänga publicerade vakanser");

        Status = VacancyStatus.Stangd;
    }

    /// <summary>
    /// Markerar vakansen som tillsatt med den angivna ansökan.
    /// </summary>
    public void Tillsatt(Guid ansokanId)
    {
        if (Status != VacancyStatus.Publicerad && Status != VacancyStatus.Stangd)
            throw new InvalidOperationException("Kan bara tillsätta publicerade eller stängda vakanser");

        var ansokan = _ansokngar.FirstOrDefault(a => a.Id == ansokanId)
            ?? throw new InvalidOperationException($"Ansökan {ansokanId} hittades inte");

        TillsattAnsokanId = ansokanId;
        ansokan.Status = ApplicationStatus.Anstalld;
        Status = VacancyStatus.Tillsatt;
    }
}

public enum VacancyStatus { Utkast, Publicerad, Stangd, Tillsatt }

public sealed class Application
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Namn { get; set; } = string.Empty;
    public string Epost { get; set; } = string.Empty;
    public string? CVFilId { get; set; }
    public ApplicationStatus Status { get; set; }
    public int? Poang { get; set; }
    public string? BedomningsKommentar { get; set; }
    public DateTime? IntervjuTidpunkt { get; set; }
    public DateTime InkomVid { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Bedömer ansökan med poäng och kommentar.
    /// </summary>
    public void Bedoma(int poang, string kommentar)
    {
        if (Status != ApplicationStatus.Mottagen && Status != ApplicationStatus.UnderGranskning)
            throw new InvalidOperationException("Kan bara bedöma mottagna eller granskade ansökningar");

        Poang = poang;
        BedomningsKommentar = kommentar;
        Status = ApplicationStatus.UnderGranskning;
    }

    /// <summary>
    /// Bjuder in till intervju.
    /// </summary>
    public void BjudInIntervju(DateTime tidpunkt)
    {
        if (Status != ApplicationStatus.UnderGranskning)
            throw new InvalidOperationException("Kan bara bjuda in granskade ansökningar till intervju");

        IntervjuTidpunkt = tidpunkt;
        Status = ApplicationStatus.Intervju;
    }

    /// <summary>
    /// Erbjuder tjänsten till sökanden.
    /// </summary>
    public void ErbjudTjanst()
    {
        if (Status != ApplicationStatus.Intervju)
            throw new InvalidOperationException("Kan bara erbjuda tjänst efter intervju");

        Status = ApplicationStatus.Erbjudande;
    }

    /// <summary>
    /// Avslår ansökan med anledning.
    /// </summary>
    public void Avsluta(string anledning)
    {
        if (Status == ApplicationStatus.Anstalld || Status == ApplicationStatus.Avslagen)
            throw new InvalidOperationException("Kan inte avsluta en redan anställd eller avslagen ansökan");

        if (string.IsNullOrWhiteSpace(anledning))
            throw new ArgumentException("Anledning måste anges", nameof(anledning));

        BedomningsKommentar = anledning;
        Status = ApplicationStatus.Avslagen;
    }
}

public enum ApplicationStatus { Mottagen, UnderGranskning, Intervju, Erbjudande, Anstalld, Avslagen }
