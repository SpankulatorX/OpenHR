using RegionHR.Core.Domain;
using RegionHR.Recruitment.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Recruitment.Services;

/// <summary>
/// De uppgifter som krävs för att omvandla en tillsatt kandidat till en riktig anställd.
/// Uppgifterna som inte finns på ansökan (personnummer, lön, period m.m.) samlas in i UI:t
/// och valideras sedan av Core-domänen (LAS-regler) när anställningen skapas.
/// </summary>
public sealed record KandidatAnstallningsData(
    Personnummer Personnummer,
    string Fornamn,
    string Efternamn,
    OrganizationId EnhetId,
    EmploymentType Anstallningsform,
    CollectiveAgreementType Kollektivavtal,
    decimal Manadslon,
    decimal Sysselsattningsgrad,
    DateOnly Startdatum,
    DateOnly? Slutdatum,
    string? Befattning,
    string? Epost,
    string? Telefon,
    CollectiveAgreementId? AvtalsId = null);

/// <summary>
/// Resultatet av en tillsätt-till-anställd-konvertering: det nyskapade Employee-aggregatet,
/// id:t på den skapade anställningen samt den onboarding-checklista som kopplats till den.
/// </summary>
public sealed record KandidatAnstallningsResultat(
    Employee Anstalld,
    EmploymentId AnstallningId,
    OnboardingChecklist Onboarding);

/// <summary>
/// Stänger kedjan rekrytering → anställning: tar en kandidat som fått ett erbjudande,
/// tillsätter vakansen och bygger ett riktigt Employee + Employment via Core-domänens
/// publika API, samt en onboarding-checklista kopplad till den nya anställningen.
///
/// Ren funktion utan I/O — persistensen sker i det anropande lagret (RekryteringService)
/// så att hela konverteringen kan sparas atomiskt i en och samma DbContext.
/// </summary>
public static class KandidatKonvertering
{
    public static KandidatAnstallningsResultat TillsattTillAnstalld(
        Vacancy vakans, Guid ansokanId, KandidatAnstallningsData data)
    {
        ArgumentNullException.ThrowIfNull(vakans);
        ArgumentNullException.ThrowIfNull(data);

        var ansokan = vakans.Ansokngar.FirstOrDefault(a => a.Id == ansokanId)
            ?? throw new InvalidOperationException(
                $"Ansökan {ansokanId} finns inte på vakansen \"{vakans.Titel}\".");

        // Systemet är experten: en kandidat måste ha fått ett erbjudande innan tjänsten tillsätts.
        if (ansokan.Status != ApplicationStatus.Erbjudande)
            throw new InvalidOperationException(
                "Kandidaten måste ha fått ett erbjudande (status Erbjudande) innan tjänsten kan tillsättas.");

        // Validera anställningen mot LAS FÖRE någon tillståndsändring, så att ogiltig indata
        // (t.ex. tillsvidare med slutdatum) inte lämnar vakansen halvt tillsatt.
        Employment.Validera(
            data.Anstallningsform,
            Money.SEK(data.Manadslon),
            new Percentage(data.Sysselsattningsgrad),
            data.Startdatum,
            data.Slutdatum);

        // 1. Tillsätt vakansen — domänen sätter ansökan till Anställd och vakansen till Tillsatt.
        vakans.Tillsatt(ansokanId);

        // 2. Skapa Employee + första anställning via Core-domänens publika API (LAS valideras i domänen).
        var anstalld = Employee.Skapa(data.Personnummer, data.Fornamn, data.Efternamn);
        if (!string.IsNullOrWhiteSpace(data.Epost) || !string.IsNullOrWhiteSpace(data.Telefon))
            anstalld.UppdateraKontaktuppgifter(data.Epost, data.Telefon, null);

        var befattning = string.IsNullOrWhiteSpace(data.Befattning) ? vakans.Titel : data.Befattning;
        var anstallning = anstalld.LaggTillAnstallning(
            data.EnhetId,
            data.Anstallningsform,
            data.Kollektivavtal,
            Money.SEK(data.Manadslon),
            new Percentage(data.Sysselsattningsgrad),
            data.Startdatum,
            data.Slutdatum,
            bestaKod: null,
            aidKod: null,
            befattningstitel: befattning,
            avtalsId: data.AvtalsId);

        // 3. Koppla en onboarding-checklista till den nya anställningen.
        var onboarding = OnboardingChecklist.Skapa(anstalld.Id.Value, vakans.Id, data.Startdatum);

        return new KandidatAnstallningsResultat(anstalld, anstallning.Id, onboarding);
    }
}
