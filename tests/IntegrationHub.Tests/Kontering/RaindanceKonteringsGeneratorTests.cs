using System.Globalization;
using RegionHR.IntegrationHub.Adapters.Raindance;
using Xunit;

namespace RegionHR.IntegrationHub.Tests.Kontering;

/// <summary>
/// Verifierar Raindance-konteringsgeneratorn: balans, gruppering per kostnadsställe,
/// rätt konton och CSV-filstruktur.
/// </summary>
public class RaindanceKonteringsGeneratorTests
{
    private readonly RaindanceKonteringsGenerator _gen = new();

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Fact]
    public void GenerateEntries_ProducesBalancedVoucher()
    {
        var run = KonteringTestData.SkapaBalanseradKorning();

        var rader = _gen.GenerateEntries(run);

        var debet = rader.Sum(r => r.Debet.Amount);
        var kredit = rader.Sum(r => r.Kredit.Amount);
        Assert.Equal(debet, kredit);
        Assert.True(_gen.ValidateBalance(rader));
    }

    [Fact]
    public void GenerateEntries_GroupsPerKostnadsstalle()
    {
        var run = KonteringTestData.SkapaBalanseradKorning();

        var rader = _gen.GenerateEntries(run);

        var kst = rader.Select(r => r.Kostnadsstalle).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(new[] { KonteringTestData.Kst1, KonteringTestData.Kst2 }, kst);

        // Varje kostnadsställe balanserar för sig.
        foreach (var group in rader.GroupBy(r => r.Kostnadsstalle))
        {
            Assert.Equal(group.Sum(r => r.Debet.Amount), group.Sum(r => r.Kredit.Amount));
        }
    }

    [Fact]
    public void GenerateEntries_PostsSalaryCostAsDebitAndTaxLiabilityAsCredit()
    {
        var run = KonteringTestData.SkapaBalanseradKorning();

        var rader = _gen.GenerateEntries(run);

        // Lönekostnad 5010 (debet) på KST 1000 = grundlön 30000.
        var loner = rader.Single(r => r.Konto == "5010" && r.Kostnadsstalle == KonteringTestData.Kst1);
        Assert.Equal(30000m, loner.Debet.Amount);
        Assert.Equal(0m, loner.Kredit.Amount);

        // OB 5020 (debet) på KST 1000 = 2000.
        var ob = rader.Single(r => r.Konto == "5020" && r.Kostnadsstalle == KonteringTestData.Kst1);
        Assert.Equal(2000m, ob.Debet.Amount);

        // Personalskatt 2710 (kredit) på KST 1000 = 9600.
        var skatt = rader.Single(r => r.Konto == "2710" && r.Kostnadsstalle == KonteringTestData.Kst1);
        Assert.Equal(9600m, skatt.Kredit.Amount);
        Assert.Equal(0m, skatt.Debet.Amount);

        // Löneskuld 2920 (kredit) på KST 1000 = netto 22400 (inga avdrag).
        var loneskuld = rader.Single(r => r.Konto == "2920" && r.Kostnadsstalle == KonteringTestData.Kst1);
        Assert.Equal(22400m, loneskuld.Kredit.Amount);
    }

    [Fact]
    public void GenerateFile_HasHeaderAndSemicolonColumns()
    {
        var run = KonteringTestData.SkapaBalanseradKorning();
        var rader = _gen.GenerateEntries(run);

        var csv = _gen.GenerateFile(rader);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                       .Select(l => l.TrimEnd('\r'))
                       .ToList();

        Assert.Equal("Kostnadsstalle;Konto;Debet;Kredit;Text;Period", lines[0]);
        Assert.Equal(rader.Count + 1, lines.Count); // header + en rad per kontering

        foreach (var dataLine in lines.Skip(1))
        {
            var cols = dataLine.Split(';');
            Assert.Equal(6, cols.Length);
        }
    }

    [Fact]
    public void GenerateFile_FormatsAmountsWithInvariantDecimalPoint()
    {
        var run = KonteringTestData.SkapaBalanseradKorning();
        var rader = _gen.GenerateEntries(run);

        var csv = _gen.GenerateFile(rader);
        var dataLines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                           .Select(l => l.TrimEnd('\r'))
                           .Skip(1);

        foreach (var line in dataLines)
        {
            var cols = line.Split(';');
            // Debet + Kredit ska vara F2 med punkt-decimal och parsebara invariant.
            Assert.Matches(@"^\d+\.\d{2}$", cols[2]);
            Assert.Matches(@"^\d+\.\d{2}$", cols[3]);
            Assert.True(decimal.TryParse(cols[2], NumberStyles.Number, Inv, out _));
            Assert.True(decimal.TryParse(cols[3], NumberStyles.Number, Inv, out _));
        }
    }

    [Fact]
    public void GenerateFile_TotalDebitEqualsTotalCredit()
    {
        var run = KonteringTestData.SkapaBalanseradKorning();
        var rader = _gen.GenerateEntries(run);

        var csv = _gen.GenerateFile(rader);
        var dataLines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                           .Select(l => l.TrimEnd('\r'))
                           .Skip(1);

        decimal debet = 0, kredit = 0;
        foreach (var line in dataLines)
        {
            var cols = line.Split(';');
            debet += decimal.Parse(cols[2], Inv);
            kredit += decimal.Parse(cols[3], Inv);
        }
        Assert.Equal(debet, kredit);
    }
}
