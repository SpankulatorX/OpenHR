using Xunit;
using RegionHR.Recruitment.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Recruitment.Tests;

public class RecruitmentTests
{
    private static Vacancy SkapaTestVakans()
    {
        return Vacancy.Skapa(
            OrganizationId.New(),
            "Sjuksköterska",
            "Vi söker en erfaren sjuksköterska till akutmottagningen.",
            EmploymentType.Tillsvidare,
            new DateOnly(2026, 5, 1));
    }

    [Fact]
    public void Skapa_vakans_satter_korrekta_varden()
    {
        var enhetId = OrganizationId.New();
        var vacancy = Vacancy.Skapa(
            enhetId, "Undersköterska", "Beskrivning",
            EmploymentType.Vikariat, new DateOnly(2026, 6, 1));

        Assert.Equal(enhetId, vacancy.EnhetId);
        Assert.Equal("Undersköterska", vacancy.Titel);
        Assert.Equal(EmploymentType.Vikariat, vacancy.Anstallningsform);
        Assert.Equal(VacancyStatus.Utkast, vacancy.Status);
        Assert.False(vacancy.PubliceradExternt);
        Assert.False(vacancy.PubliceradPlatsbanken);
    }

    [Fact]
    public void Publicera_intern_extern_platsbanken()
    {
        var vacancy = SkapaTestVakans();

        vacancy.Publicera(externt: true, platsbanken: true);

        Assert.Equal(VacancyStatus.Publicerad, vacancy.Status);
        Assert.True(vacancy.PubliceradExternt);
        Assert.True(vacancy.PubliceradPlatsbanken);
    }

    [Fact]
    public void Publicera_enbart_internt()
    {
        var vacancy = SkapaTestVakans();

        vacancy.Publicera(externt: false, platsbanken: false);

        Assert.Equal(VacancyStatus.Publicerad, vacancy.Status);
        Assert.False(vacancy.PubliceradExternt);
        Assert.False(vacancy.PubliceradPlatsbanken);
    }

    [Fact]
    public void TaEmotAnsokan_for_icke_publicerad_vakans_kastar_exception()
    {
        var vacancy = SkapaTestVakans();

        Assert.Throws<InvalidOperationException>(() =>
            vacancy.TaEmotAnsokan("Erik Johansson", "erik@test.se"));
    }

    [Fact]
    public void TaEmotAnsokan_for_publicerad_vakans_lyckas()
    {
        var vacancy = SkapaTestVakans();
        vacancy.Publicera();

        var application = vacancy.TaEmotAnsokan("Erik Johansson", "erik@test.se", "cv-123");

        Assert.Single(vacancy.Ansokngar);
        Assert.Equal("Erik Johansson", application.Namn);
        Assert.Equal("erik@test.se", application.Epost);
        Assert.Equal("cv-123", application.CVFilId);
        Assert.Equal(ApplicationStatus.Mottagen, application.Status);
    }

    [Fact]
    public void Bedom_ansokan_med_poang()
    {
        var vacancy = SkapaTestVakans();
        vacancy.Publicera();
        var application = vacancy.TaEmotAnsokan("Test", "test@test.se");

        application.Bedoma(85, "Stark kandidat med relevant erfarenhet");

        Assert.Equal(85, application.Poang);
        Assert.Equal("Stark kandidat med relevant erfarenhet", application.BedomningsKommentar);
        Assert.Equal(ApplicationStatus.UnderGranskning, application.Status);
    }

    [Fact]
    public void Komplett_rekryteringsflow_publicera_till_anstalld()
    {
        var vacancy = SkapaTestVakans();

        // Publicera
        vacancy.Publicera(externt: true, platsbanken: true);

        // Ta emot ansökan
        var application = vacancy.TaEmotAnsokan("Maria Andersson", "maria@test.se", "cv-456");
        Assert.Equal(ApplicationStatus.Mottagen, application.Status);

        // Bedöm
        application.Bedoma(90, "Utmärkt kandidat");
        Assert.Equal(ApplicationStatus.UnderGranskning, application.Status);

        // Bjud in till intervju
        var intervjuTid = new DateTime(2026, 4, 15, 10, 0, 0, DateTimeKind.Utc);
        application.BjudInIntervju(intervjuTid);
        Assert.Equal(ApplicationStatus.Intervju, application.Status);
        Assert.Equal(intervjuTid, application.IntervjuTidpunkt);

        // Erbjud tjänst
        application.ErbjudTjanst();
        Assert.Equal(ApplicationStatus.Erbjudande, application.Status);

        // Tillsätt
        vacancy.Tillsatt(application.Id);
        Assert.Equal(VacancyStatus.Tillsatt, vacancy.Status);
        Assert.Equal(ApplicationStatus.Anstalld, application.Status);
        Assert.Equal(application.Id, vacancy.TillsattAnsokanId);
    }

    [Fact]
    public void SattLonespann_satter_min_och_max()
    {
        var vacancy = SkapaTestVakans();

        vacancy.SattLonespann(Money.SEK(28000m), Money.SEK(34000m));

        Assert.NotNull(vacancy.Lonespann_Min);
        Assert.NotNull(vacancy.Lonespann_Max);
        Assert.Equal(28000m, vacancy.Lonespann_Min!.Value.Amount);
        Assert.Equal(34000m, vacancy.Lonespann_Max!.Value.Amount);
    }

    [Fact]
    public void SattLonespann_min_over_max_kastar_exception()
    {
        var vacancy = SkapaTestVakans();

        Assert.Throws<ArgumentException>(() =>
            vacancy.SattLonespann(Money.SEK(40000m), Money.SEK(30000m)));
    }

    [Fact]
    public void SattLonespann_tillater_att_rensa_till_null()
    {
        var vacancy = SkapaTestVakans();
        vacancy.SattLonespann(Money.SEK(28000m), Money.SEK(34000m));

        vacancy.SattLonespann(null, null);

        Assert.Null(vacancy.Lonespann_Min);
        Assert.Null(vacancy.Lonespann_Max);
    }

    [Fact]
    public void Publicera_tillsatt_vakans_kastar_exception()
    {
        var vacancy = SkapaTestVakans();
        vacancy.Publicera();
        var application = vacancy.TaEmotAnsokan("Test", "test@test.se");
        application.Bedoma(80, "Bra");
        application.BjudInIntervju(new DateTime(2026, 4, 15, 10, 0, 0, DateTimeKind.Utc));
        application.ErbjudTjanst();
        vacancy.Tillsatt(application.Id);

        Assert.Throws<InvalidOperationException>(() => vacancy.Publicera());
    }

    [Fact]
    public void Stang_vakans()
    {
        var vacancy = SkapaTestVakans();
        vacancy.Publicera();

        vacancy.Stang();

        Assert.Equal(VacancyStatus.Stangd, vacancy.Status);
    }

    [Fact]
    public void Stang_icke_publicerad_vakans_kastar_exception()
    {
        var vacancy = SkapaTestVakans();

        Assert.Throws<InvalidOperationException>(() => vacancy.Stang());
    }

    [Fact]
    public void Avsluta_ansokan_satter_status_avslagen()
    {
        var vacancy = SkapaTestVakans();
        vacancy.Publicera();
        var application = vacancy.TaEmotAnsokan("Test", "test@test.se");

        application.Avsluta("Ej tillräcklig erfarenhet");

        Assert.Equal(ApplicationStatus.Avslagen, application.Status);
    }

    [Fact]
    public void OnboardingChecklist_standard_har_6_items()
    {
        var checklist = OnboardingChecklist.Skapa(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 4, 1));

        Assert.Equal(6, checklist.Items.Count);
        Assert.False(checklist.AllaKlara);

        // Verifiera att alla standarduppgifter finns
        var itemBeskrivningar = checklist.Items.Select(i => i.Beskrivning).ToList();
        Assert.Contains("IT-utrustning beställd", itemBeskrivningar);
        Assert.Contains("Behörigheter uppsatta", itemBeskrivningar);
        Assert.Contains("Arbetsplats förberedd", itemBeskrivningar);
        Assert.Contains("Obligatoriska utbildningar bokade (HLR, brandskydd)", itemBeskrivningar);
        Assert.Contains("Välkomstmöte planerat", itemBeskrivningar);
        Assert.Contains("Mentor/fadder tilldelad", itemBeskrivningar);
    }

    [Fact]
    public void OnboardingChecklist_markera_item_klar()
    {
        var checklist = OnboardingChecklist.Skapa(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 4, 1));

        checklist.MarkeraKlar(0);

        Assert.True(checklist.Items[0].Klar);
        Assert.NotNull(checklist.Items[0].KlarVid);
        Assert.False(checklist.AllaKlara); // Alla är inte klara ännu
    }

    [Fact]
    public void OnboardingChecklist_alla_klara()
    {
        var checklist = OnboardingChecklist.Skapa(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 4, 1));

        for (int i = 0; i < checklist.Items.Count; i++)
        {
            checklist.MarkeraKlar(i);
        }

        Assert.True(checklist.AllaKlara);
    }

    [Fact]
    public void OnboardingChecklist_ogiltigt_index_kastar_exception()
    {
        var checklist = OnboardingChecklist.Skapa(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 4, 1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            checklist.MarkeraKlar(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            checklist.MarkeraKlar(100));
    }

    [Fact]
    public void CommunicationTemplate_skapa_satter_korrekta_varden()
    {
        var template = CommunicationTemplate.Skapa(
            "Kallelse till intervju", TemplateType.Kallelse,
            "Intervjukallelse", "Vi vill bjuda in dig till intervju...");

        Assert.Equal("Kallelse till intervju", template.Namn);
        Assert.Equal(TemplateType.Kallelse, template.Typ);
        Assert.Equal("Intervjukallelse", template.Amne);
        Assert.Equal("Vi vill bjuda in dig till intervju...", template.Brodtext);
        Assert.NotEqual(Guid.Empty, template.Id);
    }
}
