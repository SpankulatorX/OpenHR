using System.Globalization;
using System.Xml.Linq;

namespace RegionHR.Infrastructure.Integrations;

/// <summary>
/// Enkel ISO 20022 pain.001.001.03-generator för löneutbetalningar. Innehåller de
/// obligatoriska elementen (GrpHdr, PmtInf, CdtTrfTxInf). För fullständig lönefil med
/// clearingnummer, kategori (SALA) och laddningsbar utdata används
/// <see cref="RegionHR.IntegrationHub.Adapters.Nordea.NordeaPaymentFileGenerator"/>.
/// </summary>
public class NordeaPainGenerator
{
    private const string PAIN_001_NS = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.03";

    public string GeneratePain001(Pain001Data data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var ns = XNamespace.Get(PAIN_001_NS);
        var msgId = $"OPENHR-{DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
        var total = data.Betalningar.Sum(b => b.Belopp);

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "Document",
                new XElement(ns + "CstmrCdtTrfInitn",
                    new XElement(ns + "GrpHdr",
                        new XElement(ns + "MsgId", msgId),
                        new XElement(ns + "CreDtTm",
                            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                        new XElement(ns + "NbOfTxs", data.Betalningar.Count.ToString(CultureInfo.InvariantCulture)),
                        new XElement(ns + "CtrlSum", Amount(total)),
                        new XElement(ns + "InitgPty",
                            new XElement(ns + "Nm", data.AvsandareNamn))),
                    new XElement(ns + "PmtInf",
                        new XElement(ns + "PmtInfId", msgId),
                        new XElement(ns + "PmtMtd", "TRF"),
                        new XElement(ns + "NbOfTxs", data.Betalningar.Count.ToString(CultureInfo.InvariantCulture)),
                        new XElement(ns + "CtrlSum", Amount(total)),
                        new XElement(ns + "PmtTpInf",
                            new XElement(ns + "CtgyPurp",
                                new XElement(ns + "Cd", "SALA"))),
                        new XElement(ns + "ReqdExctnDt",
                            DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new XElement(ns + "Dbtr",
                            new XElement(ns + "Nm", data.AvsandareNamn)),
                        new XElement(ns + "DbtrAcct",
                            new XElement(ns + "Id",
                                new XElement(ns + "IBAN", data.AvsandareKonto))),
                        new XElement(ns + "DbtrAgt",
                            new XElement(ns + "FinInstnId",
                                new XElement(ns + "BIC", "NDEASESS"))),
                        new XElement(ns + "ChrgBr", "SLEV"),
                        data.Betalningar.Select(b =>
                            new XElement(ns + "CdtTrfTxInf",
                                new XElement(ns + "PmtId",
                                    new XElement(ns + "EndToEndId",
                                        string.IsNullOrWhiteSpace(b.Referens) ? "NOTPROVIDED" : b.Referens)),
                                new XElement(ns + "Amt",
                                    new XElement(ns + "InstdAmt", new XAttribute("Ccy", "SEK"), Amount(b.Belopp))),
                                new XElement(ns + "Cdtr",
                                    new XElement(ns + "Nm", b.Namn)),
                                new XElement(ns + "CdtrAcct",
                                    new XElement(ns + "Id",
                                        new XElement(ns + "IBAN", b.IBAN))),
                                new XElement(ns + "RmtInf",
                                    new XElement(ns + "Ustrd", b.Referens))))))));
        return doc.ToString();
    }

    private static string Amount(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture);
}

public record Pain001Data(string AvsandareNamn, string AvsandareKonto, List<BetalningData> Betalningar);
public record BetalningData(string Namn, string IBAN, decimal Belopp, string Referens);
