using RegionHR.Core.Domain;
using RegionHR.Recruitment.Domain;
using RegionHR.Recruitment.Services;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Recruitment.Tests;

/// <summary>
/// Tester för det som stänger rekryteringskedjan: en tillsatt kandidat ska bli en riktig
/// anställd (Employee + Employment) med en kopplad onboarding-checklista.
/// </summary>
public class KandidatKonverteringTests
{
    private static readonly OrganizationId Enhet = OrganizationId.New();

    private static (Vacancy vakans, Application ansokan) VakansMedErbjudandeKandidat(
        EmploymentType form = EmploymentType.Tillsvidare)
    {
        var vakans = Vacancy.Skapa(
            Enhet, "Sjuksköterska", "Akutmottagningen söker sjuksköterska.",
            form, new DateOnly(2026, 5, 1));
        vakans.Publicera(externt: true, platsbanken: false);
        var ansokan = vakans.TaEmotAnsokan("Maria Andersson", "maria.andersson@mail.se", "cv-1");
        ansokan.Bedoma(90, "Stark kandidat");
        ansokan.BjudInIntervju(new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc));
        ansokan.ErbjudTjanst();
        return (vakans, ansokan);
    }

    private static KandidatAnstallningsData Data(
        EmploymentType form = EmploymentType.Tillsvidare,
        DateOnly? slutdatum = null,
        string? befattning = "Sjuksköterska") =>
        new(
            Personnummer.CreateValidated("199001011234"),
            "Maria", "Andersson",
            Enhet, form, CollectiveAgreementType.AB,
            Manadslon: 32000m, Sysselsattningsgrad: 100m,
            Startdatum: new DateOnly(2026, 5, 1), Slutdatum: slutdatum,
            Befattning: befattning, Epost: "maria.andersson@mail.se", Telefon: "070-1234567");

    [Fact]
    public void Tillsatt_kandidat_blir_riktig_anstalld()
    {
        var (vakans, ansokan) = VakansMedErbjudandeKandidat();

        var resultat = KandidatKonvertering.TillsattTillAnstalld(vakans, ansokan.Id, Data());

        Assert.Equal("Maria", resultat.Anstalld.Fornamn);
        Assert.Equal("Andersson", resultat.Anstalld.Efternamn);
        Assert.Equal("maria.andersson@mail.se", resultat.Anstalld.Epost);
        Assert.Equal("070-1234567", resultat.Anstalld.Telefon);
        Assert.Single(resultat.Anstalld.Anstallningar);
    }

    [Fact]
    public void Anstallningen_far_vakansens_enhet_form_lon_och_grad()
    {
        var (vakans, ansokan) = VakansMedErbjudandeKandidat();

        var resultat = KandidatKonvertering.TillsattTillAnstalld(vakans, ansokan.Id, Data());
        var anstallning = resultat.Anstalld.Anstallningar.Single();

        Assert.Equal(resultat.AnstallningId, anstallning.Id);
        Assert.Equal(Enhet, anstallning.EnhetId);
        Assert.Equal(EmploymentType.Tillsvidare, anstallning.Anstallningsform);
        Assert.Equal(CollectiveAgreementType.AB, anstallning.Kollektivavtal);
        Assert.Equal(32000m, anstallning.Manadslon.Amount);
        Assert.Equal(100m, anstallning.Sysselsattningsgrad.Value);
        Assert.Equal(new DateOnly(2026, 5, 1), anstallning.Giltighetsperiod.Start);
        Assert.Null(anstallning.Giltighetsperiod.End);
        Assert.Equal("Sjuksköterska", anstallning.Befattningstitel);
    }

    [Fact]
    public void Vakansen_markeras_tillsatt_och_ansokan_anstalld()
    {
        var (vakans, ansokan) = VakansMedErbjudandeKandidat();

        KandidatKonvertering.TillsattTillAnstalld(vakans, ansokan.Id, Data());

        Assert.Equal(VacancyStatus.Tillsatt, vakans.Status);
        Assert.Equal(ansokan.Id, vakans.TillsattAnsokanId);
        Assert.Equal(ApplicationStatus.Anstalld, ansokan.Status);
    }

    [Fact]
    public void Onboarding_checklista_skapas_och_kopplas_till_anstallningen()
    {
        var (vakans, ansokan) = VakansMedErbjudandeKandidat();

        var resultat = KandidatKonvertering.TillsattTillAnstalld(vakans, ansokan.Id, Data());

        Assert.Equal(6, resultat.Onboarding.Items.Count);
        Assert.False(resultat.Onboarding.AllaKlara);
        // Checklistan pekar på den nya anställde (EmployeeId.Value) och vakansen.
        Assert.Equal(resultat.Anstalld.Id.Value, resultat.Onboarding.AnstallId);
        Assert.Equal(vakans.Id, resultat.Onboarding.VakansId);
    }

    [Fact]
    public void Befattning_faller_tillbaka_pa_vakansens_titel_nar_den_saknas()
    {
        var (vakans, ansokan) = VakansMedErbjudandeKandidat();

        var resultat = KandidatKonvertering.TillsattTillAnstalld(
            vakans, ansokan.Id, Data(befattning: null));

        Assert.Equal(vakans.Titel, resultat.Anstalld.Anstallningar.Single().Befattningstitel);
    }

    [Fact]
    public void Tillsattning_utan_erbjudande_kastar_och_lamnar_vakansen_orord()
    {
        var vakans = Vacancy.Skapa(
            Enhet, "Sjuksköterska", "Beskrivning", EmploymentType.Tillsvidare, new DateOnly(2026, 5, 1));
        vakans.Publicera();
        var ansokan = vakans.TaEmotAnsokan("Erik Johansson", "erik@mail.se"); // status Mottagen

        Assert.Throws<InvalidOperationException>(() =>
            KandidatKonvertering.TillsattTillAnstalld(vakans, ansokan.Id, Data()));

        Assert.Equal(VacancyStatus.Publicerad, vakans.Status);
        Assert.Equal(ApplicationStatus.Mottagen, ansokan.Status);
    }

    [Fact]
    public void Okand_ansokan_kastar_exception()
    {
        var (vakans, _) = VakansMedErbjudandeKandidat();

        Assert.Throws<InvalidOperationException>(() =>
            KandidatKonvertering.TillsattTillAnstalld(vakans, Guid.NewGuid(), Data()));
    }

    [Fact]
    public void Ogiltig_las_kombination_avvisas_utan_att_tillsatta_vakansen()
    {
        var (vakans, ansokan) = VakansMedErbjudandeKandidat();

        // Tillsvidare med slutdatum bryter mot LAS → ska kastas av domänvalideringen.
        var ogiltig = Data(form: EmploymentType.Tillsvidare, slutdatum: new DateOnly(2026, 12, 31));

        Assert.Throws<ArgumentException>(() =>
            KandidatKonvertering.TillsattTillAnstalld(vakans, ansokan.Id, ogiltig));

        // Ingen halv-tillsättning: vakansen och ansökan är oförändrade.
        Assert.Equal(VacancyStatus.Publicerad, vakans.Status);
        Assert.Equal(ApplicationStatus.Erbjudande, ansokan.Status);
    }

    [Fact]
    public void Vikariat_utan_slutdatum_avvisas()
    {
        var (vakans, ansokan) = VakansMedErbjudandeKandidat(EmploymentType.Vikariat);

        var ogiltig = Data(form: EmploymentType.Vikariat, slutdatum: null);

        Assert.Throws<ArgumentException>(() =>
            KandidatKonvertering.TillsattTillAnstalld(vakans, ansokan.Id, ogiltig));
    }
}
