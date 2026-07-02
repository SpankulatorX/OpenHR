using Xunit;
using System.Globalization;
using System.Xml.Linq;
using RegionHR.IntegrationHub.Adapters.Nordea;

namespace RegionHR.IntegrationHub.Tests;

/// <summary>
/// Verifierar obligatoriska/ISO 20022-krav i pain.001.001.03-filen som den befintliga
/// strukturtestet inte täcker: ChargeBearer, clearingsystem, IBAN, belopp med invariant
/// formatering och EndToEndId.
/// </summary>
public class NordeaPain001SpecTests
{
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.03";

    private static PaymentBatch CreateBatch() => new()
    {
        MessageId = "MSG-1",
        Period = "2026-03",
        OrganizationNumber = "2321000016",
        InitiatorName = "Region Örebro län",
        DebtorName = "Region Örebro län",
        DebtorIBAN = "SE4550000000058398257466",
        ExecutionDate = new DateOnly(2026, 3, 25),
        Payments =
        [
            new SalaryPayment
            {
                PaymentId = "E2E-1",
                RecipientName = "Erik Johansson",
                ClearingNumber = "3300",
                AccountNumber = "1234567890",
                Amount = 28500.50m,
                Period = "2026-03"
            }
        ]
    };

    private static XDocument Generate() =>
        XDocument.Parse(new NordeaPaymentFileGenerator().Generate(CreateBatch()));

    [Fact]
    public void PmtInf_HasChargeBearerSlev()
    {
        var pmtInf = Generate().Descendants(Ns + "PmtInf").Single();
        Assert.Equal("SLEV", pmtInf.Element(Ns + "ChrgBr")?.Value);
    }

    [Fact]
    public void DebtorAccount_HasIban()
    {
        var dbtrAcct = Generate().Descendants(Ns + "DbtrAcct").Single();
        Assert.Equal("SE4550000000058398257466", dbtrAcct.Descendants(Ns + "IBAN").Single().Value);
    }

    [Fact]
    public void Transaction_HasEndToEndIdAndClearingSystem()
    {
        var tx = Generate().Descendants(Ns + "CdtTrfTxInf").Single();

        Assert.Equal("E2E-1", tx.Descendants(Ns + "EndToEndId").Single().Value);

        var clrSys = tx.Descendants(Ns + "ClrSysMmbId").Single();
        Assert.Equal("SESBA", clrSys.Descendants(Ns + "Cd").Single().Value);
        Assert.Equal("3300", clrSys.Element(Ns + "MmbId")?.Value);

        Assert.Equal("Erik Johansson", tx.Descendants(Ns + "Cdtr").Single().Element(Ns + "Nm")?.Value);
        Assert.Equal("1234567890",
            tx.Descendants(Ns + "CdtrAcct").Single().Descendants(Ns + "Othr").Single().Element(Ns + "Id")?.Value);
    }

    [Fact]
    public void Amount_UsesInvariantDecimalPointRegardlessOfCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // Svensk kultur använder decimalkomma — filen måste ändå ha punkt.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
            var doc = Generate();

            var instdAmt = doc.Descendants(Ns + "InstdAmt").Single();
            Assert.Equal("SEK", instdAmt.Attribute("Ccy")?.Value);
            Assert.Equal("28500.50", instdAmt.Value);
            Assert.DoesNotContain(",", instdAmt.Value);

            var ctrlSum = doc.Descendants(Ns + "GrpHdr").Single().Element(Ns + "CtrlSum")?.Value;
            Assert.Equal("28500.50", ctrlSum);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Document_IsValidXmlWithSalaryCategory()
    {
        var doc = Generate();
        Assert.Equal(Ns + "Document", doc.Root?.Name);
        Assert.Equal("SALA", doc.Descendants(Ns + "CtgyPurp").Single().Element(Ns + "Cd")?.Value);
        Assert.Equal("TRF", doc.Descendants(Ns + "PmtInf").Single().Element(Ns + "PmtMtd")?.Value);
    }
}
