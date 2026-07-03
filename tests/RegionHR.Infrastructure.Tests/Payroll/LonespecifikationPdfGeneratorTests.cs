using RegionHR.Infrastructure.Export;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Infrastructure.Tests.Payroll;

public class LonespecifikationPdfGeneratorTests
{
    private static PayrollResult SkapaResultat()
    {
        var r = PayrollResult.Skapa(
            PayrollRunId.New(), EmployeeId.New(), EmploymentId.New(),
            2026, 6, Money.SEK(38500m), 100m, CollectiveAgreementType.AB);

        r.Brutto = Money.SEK(40000m);
        r.Skatt = Money.SEK(12000m);
        r.Netto = Money.SEK(27700m);
        r.OBTillagg = Money.SEK(1500m);
        r.Arbetsgivaravgifter = Money.SEK(12568m);
        r.Pensionsavgift = Money.SEK(2400m);
        r.Fackavgift = Money.SEK(300m);
        r.SemesterdagarIntjanade = 2;

        r.LaggTillRad(new PayrollResultLine
        {
            LoneartKod = "1010", Benamning = "Grundlön",
            Antal = 1, Sats = Money.SEK(38500m), Belopp = Money.SEK(38500m)
        });
        r.LaggTillRad(new PayrollResultLine
        {
            LoneartKod = "3050", Benamning = "OB-tillägg kväll",
            Antal = 10, Sats = Money.SEK(150m), Belopp = Money.SEK(1500m)
        });
        r.LaggTillRad(new PayrollResultLine
        {
            LoneartKod = "7010", Benamning = "Fackavgift",
            Antal = 1, Sats = Money.SEK(300m), Belopp = Money.SEK(300m), ArAvdrag = true
        });

        return r;
    }

    [Fact]
    public void FromPayrollResult_MapsCoreFieldsAndPeriod()
    {
        var doc = LonespecifikationDokument.FromPayrollResult(
            SkapaResultat(), "Sara Andersson", "199001011234", "Sjuksköterska");

        Assert.Equal("Sara Andersson", doc.Namn);
        Assert.Equal("199001011234", doc.Personnummer);
        Assert.Equal("Sjuksköterska", doc.Befattning);
        Assert.Equal("Juni 2026", doc.Period);
        Assert.Equal("2026-06-25", doc.Utbetalningsdag);
        Assert.Equal(40000m, doc.Brutto);
        Assert.Equal(12000m, doc.Skatt);
        Assert.Equal(27700m, doc.Netto);
        Assert.Equal(1500m, doc.OBTillagg);
        Assert.Equal(300m, doc.Fackavgift);
        Assert.Equal(2400m, doc.Pensionsavgift);
        Assert.Equal(2, doc.SemesterdagarIntjanade);
        Assert.Equal("AB", doc.Kollektivavtal);
    }

    [Fact]
    public void FromPayrollResult_MapsAllLineItemsOrderedByCode()
    {
        var doc = LonespecifikationDokument.FromPayrollResult(
            SkapaResultat(), "Sara Andersson", "199001011234");

        Assert.Equal(3, doc.Rader.Count);
        Assert.Equal("1010", doc.Rader[0].Kod);          // sorterade på löneartskod
        Assert.Equal("3050", doc.Rader[1].Kod);
        Assert.Equal("7010", doc.Rader[2].Kod);
        Assert.True(doc.Rader[2].ArAvdrag);
        Assert.Equal(1500m, doc.Rader[1].Belopp);
    }

    [Fact]
    public void Generate_ProducesValidPdfBytes()
    {
        var doc = LonespecifikationDokument.FromPayrollResult(
            SkapaResultat(), "Sara Andersson", "199001011234", "Sjuksköterska");

        var pdf = new LonespecifikationPdfGenerator().Generate(doc);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 500, "PDF bör innehålla verkligt innehåll");
        // PDF-filer inleds med magic-bytes "%PDF".
        Assert.Equal((byte)'%', pdf[0]);
        Assert.Equal((byte)'P', pdf[1]);
        Assert.Equal((byte)'D', pdf[2]);
        Assert.Equal((byte)'F', pdf[3]);
    }
}
