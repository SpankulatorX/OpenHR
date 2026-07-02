using System.Globalization;
using System.Xml.Linq;

namespace RegionHR.IntegrationHub.Adapters.Nordea;

/// <summary>
/// Genererar ISO 20022 pain.001.001.03-betalningsfiler för löneutbetalningar via Nordea.
/// Alla belopp och datum formateras med invariant kultur så att XML blir giltig
/// oavsett serverns lokala inställningar (svensk kultur ger annars decimalkomma).
/// </summary>
public sealed class NordeaPaymentFileGenerator
{
    private const string PAIN_001_NS = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.03";

    public string Generate(PaymentBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var ns = XNamespace.Get(PAIN_001_NS);

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "Document",
                new XElement(ns + "CstmrCdtTrfInitn",
                    GenerateGroupHeader(ns, batch),
                    GeneratePaymentInfo(ns, batch)
                )
            )
        );

        using var writer = new Utf8StringWriter();
        doc.Save(writer);
        return writer.ToString();
    }

    private static XElement GenerateGroupHeader(XNamespace ns, PaymentBatch batch)
    {
        return new XElement(ns + "GrpHdr",
            new XElement(ns + "MsgId", batch.MessageId),
            new XElement(ns + "CreDtTm", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
            new XElement(ns + "NbOfTxs", batch.Payments.Count.ToString(CultureInfo.InvariantCulture)),
            new XElement(ns + "CtrlSum", Amount(batch.Payments.Sum(p => p.Amount))),
            new XElement(ns + "InitgPty",
                new XElement(ns + "Nm", batch.InitiatorName),
                new XElement(ns + "Id",
                    new XElement(ns + "OrgId",
                        new XElement(ns + "Othr",
                            new XElement(ns + "Id", batch.OrganizationNumber)
                        )
                    )
                )
            )
        );
    }

    private static XElement GeneratePaymentInfo(XNamespace ns, PaymentBatch batch)
    {
        return new XElement(ns + "PmtInf",
            new XElement(ns + "PmtInfId", $"SALARY-{batch.Period}"),
            new XElement(ns + "PmtMtd", "TRF"),            // Transfer
            new XElement(ns + "NbOfTxs", batch.Payments.Count.ToString(CultureInfo.InvariantCulture)),
            new XElement(ns + "CtrlSum", Amount(batch.Payments.Sum(p => p.Amount))),
            new XElement(ns + "PmtTpInf",
                new XElement(ns + "SvcLvl",
                    new XElement(ns + "Cd", "NURG")         // Non-urgent
                ),
                new XElement(ns + "CtgyPurp",
                    new XElement(ns + "Cd", "SALA")         // Salary
                )
            ),
            new XElement(ns + "ReqdExctnDt", batch.ExecutionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XElement(ns + "Dbtr",
                new XElement(ns + "Nm", batch.DebtorName),
                new XElement(ns + "PstlAdr",
                    new XElement(ns + "Ctry", "SE")
                )
            ),
            new XElement(ns + "DbtrAcct",
                new XElement(ns + "Id",
                    new XElement(ns + "IBAN", batch.DebtorIBAN)
                ),
                new XElement(ns + "Ccy", "SEK")
            ),
            new XElement(ns + "DbtrAgt",
                new XElement(ns + "FinInstnId",
                    new XElement(ns + "BIC", "NDEASESS")    // Nordea Sweden
                )
            ),
            new XElement(ns + "ChrgBr", "SLEV"),            // Following service level (obligatoriskt i bankprofil)
            batch.Payments.Select(p => GenerateTransaction(ns, p))
        );
    }

    private static XElement GenerateTransaction(XNamespace ns, SalaryPayment payment)
    {
        return new XElement(ns + "CdtTrfTxInf",
            new XElement(ns + "PmtId",
                new XElement(ns + "EndToEndId", payment.PaymentId)
            ),
            new XElement(ns + "Amt",
                new XElement(ns + "InstdAmt",
                    new XAttribute("Ccy", "SEK"),
                    Amount(payment.Amount)
                )
            ),
            new XElement(ns + "CdtrAgt",
                new XElement(ns + "FinInstnId",
                    new XElement(ns + "ClrSysMmbId",
                        new XElement(ns + "ClrSysId",
                            new XElement(ns + "Cd", "SESBA")  // Svenskt clearingnummersystem
                        ),
                        new XElement(ns + "MmbId", payment.ClearingNumber)
                    )
                )
            ),
            new XElement(ns + "Cdtr",
                new XElement(ns + "Nm", payment.RecipientName)
            ),
            new XElement(ns + "CdtrAcct",
                new XElement(ns + "Id",
                    new XElement(ns + "Othr",
                        new XElement(ns + "Id", payment.AccountNumber)
                    )
                )
            ),
            new XElement(ns + "RmtInf",
                new XElement(ns + "Ustrd", $"Lön {payment.Period}")
            )
        );
    }

    private static string Amount(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>StringWriter som rapporterar UTF-8 så att XML-deklarationen blir korrekt.</summary>
    private sealed class Utf8StringWriter : StringWriter
    {
        public Utf8StringWriter() : base(CultureInfo.InvariantCulture) { }
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    }
}

public sealed class PaymentBatch
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N")[..16];
    public string Period { get; set; } = string.Empty;          // "2025-03"
    public string OrganizationNumber { get; set; } = string.Empty;
    public string InitiatorName { get; set; } = string.Empty;
    public string DebtorName { get; set; } = string.Empty;
    public string DebtorIBAN { get; set; } = string.Empty;
    public DateOnly ExecutionDate { get; set; }
    public List<SalaryPayment> Payments { get; set; } = [];
}

public sealed class SalaryPayment
{
    public string PaymentId { get; set; } = Guid.NewGuid().ToString("N")[..16];
    public string RecipientName { get; set; } = string.Empty;
    public string ClearingNumber { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Period { get; set; } = string.Empty;
}
