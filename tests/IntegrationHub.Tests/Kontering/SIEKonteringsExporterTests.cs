using System.Globalization;
using System.Text;
using RegionHR.IntegrationHub.Adapters.Raindance;
using RegionHR.IntegrationHub.Adapters.SIE;
using Xunit;

namespace RegionHR.IntegrationHub.Tests.Kontering;

/// <summary>
/// Verifierar SIE typ 4-exportens filstruktur: obligatoriska poster, kontoplan,
/// dimensioner, verifikation och balanserade transaktioner.
/// </summary>
public class SIEKonteringsExporterTests
{
    private readonly RaindanceKonteringsGenerator _gen = new();
    private readonly SIEKonteringsExporter _exporter = new();

    private static SIEExportInput Input() => new()
    {
        OrganisationsNummer = "232100-0016",
        Foretagsnamn = "Region Örebro län",
        GenereringsDatum = new DateOnly(2026, 4, 12)
    };

    private string GenerateSie()
    {
        var run = KonteringTestData.SkapaBalanseradKorning(2026, 3);
        var rader = _gen.GenerateEntries(run);
        return _exporter.GenerateSie(run, Input(), rader);
    }

    [Fact]
    public void Header_HasMandatorySieRecords()
    {
        var sie = GenerateSie();

        Assert.Contains("#FLAGGA 0", sie);
        Assert.Contains("#SIETYP 4", sie);
        Assert.Contains("#FORMAT PC8", sie);
        Assert.Contains("#PROGRAM \"OpenHR\" \"1.0\"", sie);
        Assert.Contains("#GEN 20260412", sie);
        Assert.Contains("#ORGNR 232100-0016", sie);
        Assert.Contains("#FNAMN \"Region Örebro län\"", sie);
        Assert.Contains("#RAR 0 20260101 20261231", sie);
    }

    [Fact]
    public void Accounts_AreEmittedForEachUsedAccountWithName()
    {
        var run = KonteringTestData.SkapaBalanseradKorning();
        var rader = _gen.GenerateEntries(run);
        var sie = _exporter.GenerateSie(run, Input(), rader);

        var usedAccounts = rader.Select(r => r.Konto).Distinct().ToList();
        foreach (var konto in usedAccounts)
        {
            Assert.Contains("#KONTO " + konto + " \"", sie);
        }

        // Antal #KONTO-poster = antal distinkta konton.
        var kontoCount = CountLines(sie, "#KONTO ");
        Assert.Equal(usedAccounts.Count, kontoCount);

        // Kända kontonamn.
        Assert.Contains("#KONTO 5010 \"Löner\"", sie);
        Assert.Contains("#KONTO 2710 \"Personalens källskatt\"", sie);
    }

    [Fact]
    public void Dimensions_DeclareKostnadsstalleAndObjects()
    {
        var sie = GenerateSie();

        Assert.Contains("#DIM 1 \"Kostnadsställe\"", sie);
        Assert.Contains("#OBJEKT 1 \"1000\" \"Kostnadsställe 1000\"", sie);
        Assert.Contains("#OBJEKT 1 \"2000\" \"Kostnadsställe 2000\"", sie);
    }

    [Fact]
    public void Verification_HasHeaderAndBraces()
    {
        var sie = GenerateSie();

        // Verdatum = sista dagen i mars 2026.
        Assert.Contains("#VER \"L\" \"1\" 20260331 \"Lönekörning 2026-03\" 20260412", sie);
        Assert.Contains("{", sie);
        Assert.Contains("}", sie);
    }

    [Fact]
    public void Transactions_SumToZero_BalancedVoucher()
    {
        var sie = GenerateSie();

        var transRader = ExtractTrans(sie);
        Assert.NotEmpty(transRader);

        var summa = transRader.Sum(t => t.Amount);
        Assert.Equal(0m, summa);
    }

    [Fact]
    public void Transactions_CarryKostnadsstalleDimensionObject()
    {
        var sie = GenerateSie();

        var transRader = ExtractTrans(sie);
        Assert.All(transRader, t =>
        {
            Assert.True(t.Kostnadsstalle is KonteringTestData.Kst1 or KonteringTestData.Kst2,
                $"Transaktion saknar giltigt kostnadsställe: {t.Kostnadsstalle}");
        });
    }

    [Fact]
    public void Transactions_DebitPositiveCreditNegative()
    {
        var run = KonteringTestData.SkapaBalanseradKorning();
        var rader = _gen.GenerateEntries(run);
        var sie = _exporter.GenerateSie(run, Input(), rader);

        var trans = ExtractTrans(sie);

        // Lönekostnad 5010 ska bokföras positivt (debet).
        Assert.All(trans.Where(t => t.Konto == "5010"), t => Assert.True(t.Amount > 0));
        // Personalskatt 2710 ska bokföras negativt (kredit).
        Assert.All(trans.Where(t => t.Konto == "2710"), t => Assert.True(t.Amount < 0));
    }

    [Fact]
    public void GenerateSieBytes_RoundTripsAsLatin1()
    {
        var run = KonteringTestData.SkapaBalanseradKorning();
        var rader = _gen.GenerateEntries(run);
        var input = Input();

        var bytes = _exporter.GenerateSieBytes(run, input, rader);
        var text = _exporter.GenerateSie(run, input, rader);

        Assert.Equal(text, Encoding.Latin1.GetString(bytes));
        // 'ö' i "Örebro" ska kodas som Latin-1 0xF6, inte multibyte-UTF-8.
        Assert.Contains((byte)0xF6, bytes);
    }

    [Fact]
    public void UsesCrlfLineEndings()
    {
        var sie = GenerateSie();
        Assert.Contains("\r\n", sie);
        // Ingen ensam LF utan föregående CR.
        for (var i = 0; i < sie.Length; i++)
        {
            if (sie[i] == '\n')
                Assert.True(i > 0 && sie[i - 1] == '\r', $"Ensam LF vid position {i}");
        }
    }

    private static int CountLines(string content, string prefix) =>
        content.Split('\n').Count(l => l.TrimStart().StartsWith(prefix, StringComparison.Ordinal));

    private static List<TransRad> ExtractTrans(string sie)
    {
        var result = new List<TransRad>();
        foreach (var raw in sie.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("#TRANS ", StringComparison.Ordinal))
                continue;

            // #TRANS <konto> {1 "1000"} <belopp> <datum> "<text>"
            var afterTrans = line.Substring("#TRANS ".Length).TrimStart();
            var konto = afterTrans.Substring(0, afterTrans.IndexOf(' ')).Trim();

            var braceStart = afterTrans.IndexOf('{');
            var braceEnd = afterTrans.IndexOf('}');
            var objekt = afterTrans.Substring(braceStart + 1, braceEnd - braceStart - 1).Trim();
            string? kst = null;
            var q1 = objekt.IndexOf('"');
            if (q1 >= 0)
            {
                var q2 = objekt.IndexOf('"', q1 + 1);
                if (q2 > q1)
                    kst = objekt.Substring(q1 + 1, q2 - q1 - 1);
            }

            var rest = afterTrans.Substring(braceEnd + 1).Trim();
            var amountToken = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            var amount = decimal.Parse(amountToken, CultureInfo.InvariantCulture);

            result.Add(new TransRad(konto, kst, amount));
        }
        return result;
    }

    private sealed record TransRad(string Konto, string? Kostnadsstalle, decimal Amount);
}
