using System.Text;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Payroll.Tests;

/// <summary>
/// Domäntester för facktillhörighet (medlemskap) och generatorn som bygger uppdateringsfilen
/// till fackförbund.
/// </summary>
public sealed class FacktillhorighetTests
{
    private static readonly EmployeeId Anstalld = EmployeeId.New();
    private static readonly DateOnly Start = new(2026, 1, 1);

    [Fact]
    public void Skapa_SetsAllFields()
    {
        var f = Facktillhorighet.Skapa(
            Anstalld, "Vårdförbundet", Start, FacktillhorighetRoll.Skyddsombud,
            fackforbundKod: "VF", medlemsnummer: "12345", avtalsomrade: "HÖK", registreradAv: "HR");

        Assert.Equal("Vårdförbundet", f.Fackforbund);
        Assert.Equal("VF", f.FackforbundKod);
        Assert.Equal("12345", f.Medlemsnummer);
        Assert.Equal("HÖK", f.Avtalsomrade);
        Assert.Equal(FacktillhorighetRoll.Skyddsombud, f.Roll);
        Assert.Null(f.Slutdatum);
    }

    [Fact]
    public void Skapa_EmptyForbund_Throws() =>
        Assert.Throws<ArgumentException>(() => Facktillhorighet.Skapa(Anstalld, "  ", Start));

    [Fact]
    public void Skapa_SlutBeforeStart_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Facktillhorighet.Skapa(Anstalld, "Kommunal", Start, slutdatum: new DateOnly(2025, 12, 1)));

    [Fact]
    public void Skapa_BlankOptionalFields_BecomeNull()
    {
        var f = Facktillhorighet.Skapa(Anstalld, "Vision", Start, medlemsnummer: "   ", fackforbundKod: "");
        Assert.Null(f.Medlemsnummer);
        Assert.Null(f.FackforbundKod);
    }

    [Fact]
    public void ArAktivPer_RespectsInterval()
    {
        var f = Facktillhorighet.Skapa(Anstalld, "Kommunal", new DateOnly(2026, 3, 1),
            slutdatum: new DateOnly(2026, 5, 31));
        Assert.True(f.ArAktivPer(new DateOnly(2026, 4, 15)));
        Assert.False(f.ArAktivPer(new DateOnly(2026, 2, 1)));
        Assert.False(f.ArAktivPer(new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void ArAktivUnder_OpenEnded_IsActiveAfterStart()
    {
        var f = Facktillhorighet.Skapa(Anstalld, "Kommunal", new DateOnly(2026, 1, 1));
        Assert.True(f.ArAktivUnder(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
        Assert.False(f.ArAktivUnder(new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void Avsluta_SetsSlutdatum_AndRejectsBeforeStart()
    {
        var f = Facktillhorighet.Skapa(Anstalld, "Kommunal", Start);
        f.Avsluta(new DateOnly(2026, 6, 30));
        Assert.Equal(new DateOnly(2026, 6, 30), f.Slutdatum);
        Assert.Throws<ArgumentException>(() => f.Avsluta(new DateOnly(2025, 1, 1)));
    }

    [Fact]
    public void SattRoll_ChangesRole()
    {
        var f = Facktillhorighet.Skapa(Anstalld, "Kommunal", Start);
        Assert.Equal(FacktillhorighetRoll.Medlem, f.Roll);
        f.SattRoll(FacktillhorighetRoll.Huvudskyddsombud);
        Assert.Equal(FacktillhorighetRoll.Huvudskyddsombud, f.Roll);
    }

    // === Filgenerator ===

    [Fact]
    public void FilGenerator_BuildsHeaderRowsAndTrailer()
    {
        var pnr = (string)Personnummer.CreateValidated("198001011234");
        var input = new FacktillhorighetFilInput(
            "232100000164", "Region Örebro län", new DateOnly(2026, 2, 1),
            new List<FacktillhorighetRad>
            {
                new(pnr, "Åsa Öberg", "Kommunal", "KOM", "9911", FacktillhorighetRoll.Medlem, true,
                    new DateOnly(2026, 1, 1), null),
                new((string)Personnummer.CreateValidated("197505054321"), "Erik Ek", "Vision", "VIS", null,
                    FacktillhorighetRoll.Fortroendevald, false, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 31))
            });

        var gen = new FacktillhorighetFilGenerator();
        var content = gen.ByggInnehall(input);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Header + kolumnrubrik + 2 rader + trailer = 5 rader
        Assert.Equal(5, lines.Length);
        Assert.StartsWith("#H;232100000164;", lines[0]);
        Assert.Contains("Personnummer;Namn;Forbund", lines[1]);
        Assert.Contains(pnr, lines[2]);
        Assert.Contains("AKTIV", lines[2]);
        Assert.Contains("MEDLEM", lines[2]);
        Assert.Contains("AVSLUTAD", lines[3]);
        Assert.Contains("FORTROENDEVALD", lines[3]);
        Assert.Equal("#S;2", lines[4]);
    }

    [Fact]
    public void FilGenerator_EncodesLatin1_PreservingSwedishChars()
    {
        var input = new FacktillhorighetFilInput(
            "232100000164", "Region Örebro län", new DateOnly(2026, 2, 1),
            new List<FacktillhorighetRad>
            {
                new((string)Personnummer.CreateValidated("198001011234"), "Åsa Öberg", "Kommunal", null, null,
                    FacktillhorighetRoll.Medlem, true, new DateOnly(2026, 1, 1), null)
            });

        var fil = new FacktillhorighetFilGenerator().Generera(input);
        var decoded = Encoding.Latin1.GetString(fil.Content);

        Assert.EndsWith(".csv", fil.FileName);
        Assert.Equal("text/csv", fil.MimeType);
        Assert.Contains("Åsa Öberg", decoded);
        Assert.Contains("Örebro", decoded);
    }

    [Fact]
    public void FilGenerator_SanitizesSeparatorInFreeText()
    {
        var input = new FacktillhorighetFilInput(
            "232100000164", "Region Örebro län", new DateOnly(2026, 2, 1),
            new List<FacktillhorighetRad>
            {
                new((string)Personnummer.CreateValidated("198001011234"), "Namn; med semikolon", "Kommunal", null, null,
                    FacktillhorighetRoll.Medlem, true, new DateOnly(2026, 1, 1), null)
            });

        var content = new FacktillhorighetFilGenerator().ByggInnehall(input);
        var dataLine = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)[2];

        // Namnfältet får inte introducera en extra kolumn.
        Assert.DoesNotContain("Namn; med", dataLine);
        Assert.Contains("Namn  med semikolon", dataLine);
    }
}
