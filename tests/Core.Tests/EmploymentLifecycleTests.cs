using RegionHR.Core.Contracts;
using RegionHR.Core.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Core.Tests;

/// <summary>
/// Tester för anställningens livscykel (anställ / ändra / avsluta) och den
/// LAS-baserade valideringen. Systemet ska vara experten: felaktiga
/// kombinationer avvisas i domänen, inte i UI:t.
/// </summary>
public class EmploymentLifecycleTests
{
    private static Employee NyAnstalld() =>
        Employee.Skapa(new Personnummer("198112289874"), "Test", "Testsson");

    private const string Pnr = "198112289874";

    // ---------- Anställ (skapa) ----------

    [Fact]
    public void Anstall_Tillsvidare_UtanSlutdatum_Lyckas()
    {
        var e = NyAnstalld();
        var a = e.LaggTillAnstallning(
            OrganizationId.New(), EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(32000m), Percentage.FullTime, new DateOnly(2026, 1, 1),
            befattningstitel: "Sjuksköterska");

        Assert.Single(e.Anstallningar);
        Assert.Equal("Sjuksköterska", a.Befattningstitel);
        Assert.True(a.Giltighetsperiod.IsOpenEnded);
    }

    [Fact]
    public void Anstall_Tillsvidare_MedSlutdatum_Kastar()
    {
        var e = NyAnstalld();
        var ex = Assert.Throws<ArgumentException>(() => e.LaggTillAnstallning(
            OrganizationId.New(), EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(32000m), Percentage.FullTime, new DateOnly(2026, 1, 1),
            slutdatum: new DateOnly(2026, 12, 31)));
        Assert.Contains("tillsvidare", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(EmploymentType.Vikariat)]
    [InlineData(EmploymentType.SAVA)]
    [InlineData(EmploymentType.Provanstallning)]
    [InlineData(EmploymentType.Sasongsanstallning)]
    public void Anstall_Tidsbegransad_UtanSlutdatum_Kastar(EmploymentType form)
    {
        var e = NyAnstalld();
        Assert.Throws<ArgumentException>(() => e.LaggTillAnstallning(
            OrganizationId.New(), form, CollectiveAgreementType.AB,
            Money.SEK(30000m), Percentage.FullTime, new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Anstall_Provanstallning_HogstSexManader_Lyckas()
    {
        var e = NyAnstalld();
        var a = e.LaggTillAnstallning(
            OrganizationId.New(), EmploymentType.Provanstallning, CollectiveAgreementType.AB,
            Money.SEK(30000m), Percentage.FullTime, new DateOnly(2026, 1, 1),
            slutdatum: new DateOnly(2026, 7, 1)); // exakt 6 mån
        Assert.True(a.ArProvanstallning);
    }

    [Fact]
    public void Anstall_Provanstallning_OverSexManader_Kastar_LAS6()
    {
        var e = NyAnstalld();
        var ex = Assert.Throws<ArgumentException>(() => e.LaggTillAnstallning(
            OrganizationId.New(), EmploymentType.Provanstallning, CollectiveAgreementType.AB,
            Money.SEK(30000m), Percentage.FullTime, new DateOnly(2026, 1, 1),
            slutdatum: new DateOnly(2026, 8, 1))); // 7 mån
        Assert.Contains("6", ex.Message);
    }

    [Fact]
    public void Anstall_UtanLon_Kastar()
    {
        var e = NyAnstalld();
        Assert.Throws<ArgumentException>(() => e.LaggTillAnstallning(
            OrganizationId.New(), EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.Zero, Percentage.FullTime, new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Anstall_Timavlonad_UtanManadslon_Lyckas()
    {
        var e = NyAnstalld();
        var a = e.LaggTillAnstallning(
            OrganizationId.New(), EmploymentType.Timavlonad, CollectiveAgreementType.AB,
            Money.Zero, Percentage.FullTime, new DateOnly(2026, 1, 1));
        Assert.Equal(0m, a.Manadslon.Amount);
    }

    [Fact]
    public void Anstall_SlutdatumForeStart_Kastar()
    {
        var e = NyAnstalld();
        Assert.Throws<ArgumentException>(() => e.LaggTillAnstallning(
            OrganizationId.New(), EmploymentType.Vikariat, CollectiveAgreementType.AB,
            Money.SEK(30000m), Percentage.FullTime, new DateOnly(2026, 6, 1),
            slutdatum: new DateOnly(2026, 1, 1)));
    }

    // ---------- Ändra ----------

    [Fact]
    public void AndraLon_ViaAggregatet_UppdaterarOchSpararAndradAv()
    {
        var e = NyAnstalld();
        var a = e.LaggTillAnstallning(
            OrganizationId.New(), EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(30000m), Percentage.FullTime, new DateOnly(2026, 1, 1));

        e.AndraAnstallningsLon(a.Id, Money.SEK(34000m), "karl.berg");

        Assert.Equal(34000m, a.Manadslon.Amount);
        Assert.Equal("karl.berg", a.UpdatedBy);
        Assert.Contains(e.DomainEvents, ev => ev is SalaryChangedEvent);
    }

    [Fact]
    public void AndraSysselsattningsgrad_ViaAggregatet_Uppdaterar()
    {
        var e = NyAnstalld();
        var a = e.LaggTillAnstallning(
            OrganizationId.New(), EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(30000m), Percentage.FullTime, new DateOnly(2026, 1, 1));

        e.AndraAnstallningsSysselsattningsgrad(a.Id, new Percentage(75m), "eva.nilsson");
        Assert.Equal(75m, a.Sysselsattningsgrad.Value);
    }

    [Fact]
    public void SattBefattning_ViaAggregatet_Uppdaterar()
    {
        var e = NyAnstalld();
        var a = e.LaggTillAnstallning(
            OrganizationId.New(), EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(30000m), Percentage.FullTime, new DateOnly(2026, 1, 1));

        e.SattAnstallningsBefattning(a.Id, "Avdelningschef", "eva.nilsson");
        Assert.Equal("Avdelningschef", a.Befattningstitel);
    }

    [Fact]
    public void Andra_OkandAnstallning_Kastar()
    {
        var e = NyAnstalld();
        Assert.Throws<InvalidOperationException>(() =>
            e.AndraAnstallningsLon(EmploymentId.New(), Money.SEK(1m), "x"));
    }

    // ---------- Avsluta ----------

    [Fact]
    public void Avsluta_SatterSlutdatum_OchEvent()
    {
        var e = NyAnstalld();
        var a = e.LaggTillAnstallning(
            OrganizationId.New(), EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(30000m), Percentage.FullTime, new DateOnly(2026, 1, 1));

        e.AvslutaAnstallning(a.Id, new DateOnly(2026, 12, 31), "karl.berg");

        Assert.Equal(new DateOnly(2026, 12, 31), a.Giltighetsperiod.End);
        Assert.True(a.ArAvslutad(new DateOnly(2027, 1, 1)));
        Assert.Contains(e.DomainEvents, ev => ev is EmploymentEndedEvent);
    }

    [Fact]
    public void Avsluta_SlutdatumForeStart_Kastar()
    {
        var e = NyAnstalld();
        var a = e.LaggTillAnstallning(
            OrganizationId.New(), EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(30000m), Percentage.FullTime, new DateOnly(2026, 6, 1));

        Assert.Throws<ArgumentException>(() =>
            e.AvslutaAnstallning(a.Id, new DateOnly(2026, 1, 1), "x"));
    }

    // ---------- LAS 6c § anställningsavtal ----------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 3)]
    [InlineData(6, 4)]
    [InlineData(8, 5)]
    [InlineData(10, 6)]
    [InlineData(25, 6)]
    public void LagstadgadUppsagningstid_FoljerLAS11(int ar, int forvantatManader)
    {
        Assert.Equal(forvantatManader, AnstallningsavtalGenerator.LagstadgadUppsagningstidManader(ar));
    }

    [Fact]
    public void Skapa6cInformation_InnehallerAllaObligatoriskaAvsnitt()
    {
        var u = new AnstallningsavtalUppgifter(
            ArbetsgivareNamn: "Region Örebro län",
            ArbetsgivareAdress: "Box 1613, 701 16 Örebro",
            ArbetstagareNamn: "Test Testsson",
            ArbetstagarePersonnummerMaskerat: "19811228-****",
            EnhetNamn: "CIVA",
            Arbetsplats: "Universitetssjukhuset Örebro",
            Befattningstitel: "Sjuksköterska",
            Anstallningsform: EmploymentType.Tillsvidare,
            Manadslon: 34000m,
            Sysselsattningsgrad: 100m,
            Tilltradesdag: new DateOnly(2026, 3, 1),
            Slutdatum: null,
            Kollektivavtal: CollectiveAgreementType.AB,
            KollektivavtalNamn: "Allmänna bestämmelser (AB)");

        var avsnitt = AnstallningsavtalGenerator.Skapa6cInformation(u);

        // 9 obligatoriska avsnitt enligt LAS 6 c §
        Assert.Equal(9, avsnitt.Count);
        var text = AnstallningsavtalGenerator.Skapa6cText(u);
        Assert.Contains("Tillträdesdag", text);
        Assert.Contains("Sjuksköterska", text);
        Assert.Contains("34", text);            // begynnelselön
        Assert.Contains("kollektivavtal", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LAS 11", text);        // uppsägningstid
    }

    [Fact]
    public void Skapa6cInformation_Provanstallning_NamnerLAS6OchProvotid()
    {
        var u = new AnstallningsavtalUppgifter(
            "Region Örebro län", "Box 1613, 701 16 Örebro",
            "Test Testsson", "19811228-****",
            "CIVA", "USÖ", "Undersköterska",
            EmploymentType.Provanstallning, 28000m, 100m,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30),
            CollectiveAgreementType.AB, "AB");

        var text = AnstallningsavtalGenerator.Skapa6cText(u);
        Assert.Contains("LAS 6", text);
        Assert.Contains("Prövotiden", text);
    }
}
