using Xunit;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Payroll.Tests;

/// <summary>
/// Tester för svenska helgdagar, storhelger och OB-kategoribestämning.
/// </summary>
public class SvenskaHelgdagarTests
{
    #region Fasta helgdagar 2025

    [Theory]
    [InlineData(2025, 1, 1)]   // Nyårsdagen
    [InlineData(2025, 1, 6)]   // Trettondedag jul
    [InlineData(2025, 5, 1)]   // Första maj
    [InlineData(2025, 6, 6)]   // Nationaldagen
    [InlineData(2025, 12, 24)] // Julafton
    [InlineData(2025, 12, 25)] // Juldagen
    [InlineData(2025, 12, 26)] // Annandag jul
    [InlineData(2025, 12, 31)] // Nyårsafton
    public void ArHelgdag_FastaHelgdagar2025_ReturnsTrue(int year, int month, int day)
    {
        var datum = new DateOnly(year, month, day);
        Assert.True(SvenskaHelgdagar.ArHelgdag(datum));
    }

    #endregion

    #region Fasta helgdagar 2026

    [Theory]
    [InlineData(2026, 1, 1)]   // Nyårsdagen
    [InlineData(2026, 1, 6)]   // Trettondedag jul
    [InlineData(2026, 5, 1)]   // Första maj
    [InlineData(2026, 6, 6)]   // Nationaldagen
    [InlineData(2026, 12, 24)] // Julafton
    [InlineData(2026, 12, 25)] // Juldagen
    [InlineData(2026, 12, 26)] // Annandag jul
    [InlineData(2026, 12, 31)] // Nyårsafton
    public void ArHelgdag_FastaHelgdagar2026_ReturnsTrue(int year, int month, int day)
    {
        var datum = new DateOnly(year, month, day);
        Assert.True(SvenskaHelgdagar.ArHelgdag(datum));
    }

    #endregion

    #region Rörliga helgdagar 2025

    [Fact]
    public void ArHelgdag_Pask2025_CorrectDates()
    {
        // Påskdagen 2025: 20 april
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2025, 4, 18)));  // Långfredagen
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2025, 4, 19)));  // Påskafton
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2025, 4, 20)));  // Påskdagen
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2025, 4, 21)));  // Annandag påsk
    }

    [Fact]
    public void ArHelgdag_KristiHimmelsfardsdag2025_Correct()
    {
        // 39 dagar efter påskdagen (20 april) = 29 maj 2025
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2025, 5, 29)));
    }

    [Fact]
    public void ArHelgdag_Pingstdagen2025_Correct()
    {
        // 49 dagar efter påskdagen (20 april) = 8 juni 2025
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2025, 6, 8)));
    }

    [Fact]
    public void ArHelgdag_Midsommar2025_CorrectDates()
    {
        // Midsommardagen 2025: lördagen 20-26 juni = 21 juni 2025
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2025, 6, 20)));  // Midsommarafton
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2025, 6, 21)));  // Midsommardagen
    }

    [Fact]
    public void ArHelgdag_AllaHelgonsDag2025_Correct()
    {
        // Alla helgons dag 2025: lördagen 31 okt - 6 nov = 1 november 2025
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2025, 11, 1)));
    }

    #endregion

    #region Rörliga helgdagar 2026

    [Fact]
    public void ArHelgdag_Pask2026_CorrectDates()
    {
        // Påskdagen 2026: 5 april
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2026, 4, 3)));   // Långfredagen
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2026, 4, 4)));   // Påskafton
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2026, 4, 5)));   // Påskdagen
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2026, 4, 6)));   // Annandag påsk
    }

    [Fact]
    public void ArHelgdag_KristiHimmelsfardsdag2026_Correct()
    {
        // 39 dagar efter 5 april = 14 maj 2026
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2026, 5, 14)));
    }

    [Fact]
    public void ArHelgdag_Midsommar2026_CorrectDates()
    {
        // Midsommardagen 2026: lördagen 20-26 juni = 20 juni 2026
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2026, 6, 19)));  // Midsommarafton
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2026, 6, 20)));  // Midsommardagen
    }

    [Fact]
    public void ArHelgdag_AllaHelgonsDag2026_Correct()
    {
        // Alla helgons dag 2026: lördagen 31 okt - 6 nov = 31 oktober 2026
        Assert.True(SvenskaHelgdagar.ArHelgdag(new DateOnly(2026, 10, 31)));
    }

    #endregion

    #region Icke-helgdagar

    [Theory]
    [InlineData(2025, 3, 10)]  // Vanlig måndag
    [InlineData(2025, 7, 15)]  // Vanlig tisdag
    [InlineData(2025, 10, 8)]  // Vanlig onsdag
    public void ArHelgdag_VanligVardag_ReturnsFalse(int year, int month, int day)
    {
        var datum = new DateOnly(year, month, day);
        Assert.False(SvenskaHelgdagar.ArHelgdag(datum));
    }

    #endregion

    #region Storhelg

    [Fact]
    public void ArStorhelg_Julafton_ReturnsTrue()
    {
        Assert.True(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 12, 24)));
    }

    [Fact]
    public void ArStorhelg_Juldagen_ReturnsTrue()
    {
        Assert.True(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 12, 25)));
    }

    [Fact]
    public void ArStorhelg_AnnandagJul_ReturnsTrue()
    {
        Assert.True(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 12, 26)));
    }

    [Fact]
    public void ArStorhelg_Nyarsafton_ReturnsTrue()
    {
        Assert.True(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void ArStorhelg_Nyarsdagen_ReturnsTrue()
    {
        Assert.True(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 1, 1)));
    }

    [Fact]
    public void ArStorhelg_Paskafton_ReturnsTrue()
    {
        // Påskafton 2025: 19 april
        Assert.True(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 4, 19)));
    }

    [Fact]
    public void ArStorhelg_Paskdagen_ReturnsTrue()
    {
        Assert.True(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 4, 20)));
    }

    [Fact]
    public void ArStorhelg_AnnandagPask_ReturnsTrue()
    {
        Assert.True(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 4, 21)));
    }

    [Fact]
    public void ArStorhelg_Midsommarafton_ReturnsTrue()
    {
        // Midsommarafton 2025: 20 juni
        Assert.True(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 6, 20)));
    }

    [Fact]
    public void ArStorhelg_Midsommardagen_ReturnsTrue()
    {
        Assert.True(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 6, 21)));
    }

    [Fact]
    public void ArStorhelg_KristiHimmelsfardsdag_ReturnsFalse()
    {
        // Kristi himmelsfärdsdag 2025: 29 maj — helgdag (O-tilläggstid B) men INTE storhelg
        // enligt AB 25 § 21 (ingår inte i O-tilläggstid A).
        Assert.False(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 5, 29)));
    }

    [Fact]
    public void ArStorhelg_Langfredagen_ReturnsTrue()
    {
        // Långfredagen 2025: 18 april — ingår i O-tilläggstid A (AB 25 § 21).
        Assert.True(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 4, 18)));
    }

    [Fact]
    public void ArStorhelg_SkartorsdagFran18_ReturnsTrue()
    {
        // Skärtorsdagen 2025: 17 april. A-tiden börjar kl. 18.00 dagen före långfredagen.
        var skartorsdag = new DateOnly(2025, 4, 17);
        Assert.True(SvenskaHelgdagar.ArStorhelg(skartorsdag, new TimeOnly(18, 0)));
        Assert.True(SvenskaHelgdagar.ArStorhelg(skartorsdag, new TimeOnly(21, 30)));
        Assert.False(SvenskaHelgdagar.ArStorhelg(skartorsdag, new TimeOnly(12, 0)));
        // Dygnsnivå: skärtorsdagen som helhet är inte storhelg.
        Assert.False(SvenskaHelgdagar.ArStorhelg(skartorsdag));
    }

    [Fact]
    public void ArStorhelg_SondagenEfterMidsommar_ReturnsTrue()
    {
        // Midsommardagen 2025: 21 juni (lördag). A-tiden löper t.o.m. söndagen 22 juni
        // (och till kl. 07.00 på måndagen).
        Assert.True(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 6, 22)));
    }

    [Fact]
    public void ArStorhelg_MandagEfterMidsommarTill07_ReturnsTrue()
    {
        var mandag = new DateOnly(2025, 6, 23);
        Assert.True(SvenskaHelgdagar.ArStorhelg(mandag, new TimeOnly(6, 30)));
        Assert.False(SvenskaHelgdagar.ArStorhelg(mandag, new TimeOnly(7, 0)));
        Assert.False(SvenskaHelgdagar.ArStorhelg(mandag));
    }

    [Fact]
    public void ArStorhelg_VanligHelgdag_ReturnsFalse()
    {
        // Nationaldagen är helgdag men inte storhelg
        Assert.False(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 6, 6)));
        // Trettondedag jul
        Assert.False(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 1, 6)));
        // Första maj
        Assert.False(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 5, 1)));
    }

    [Fact]
    public void ArStorhelg_VanligVardag_ReturnsFalse()
    {
        Assert.False(SvenskaHelgdagar.ArStorhelg(new DateOnly(2025, 3, 10)));
    }

    #endregion

    #region OB-kategori

    [Fact]
    public void BeraknaOBKategori_VardagDagtid_Ingen()
    {
        // Måndag 09:00 → Ingen
        var datum = new DateOnly(2025, 3, 10); // Måndag
        Assert.Equal(OBCategory.Ingen, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(9, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_VardagKvall_VardagKvall()
    {
        // Måndag 19:00 → VardagKväll
        var datum = new DateOnly(2025, 3, 10); // Måndag
        Assert.Equal(OBCategory.VardagKvall, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(19, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_VardagNatt_VardagNatt()
    {
        // Måndag 23:00 → VardagNatt
        var datum = new DateOnly(2025, 3, 10); // Måndag
        Assert.Equal(OBCategory.VardagNatt, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(23, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_MandagTidigMorgon_Helg()
    {
        // Måndag 05:00 → O-tilläggstid B (helgtiden löper till kl. 07 på måndagen, AB 25 § 21)
        var datum = new DateOnly(2025, 3, 10); // Måndag
        Assert.Equal(OBCategory.Helg, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(5, 0)));
        Assert.Equal(OBCategory.Helg, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(6, 30)));
    }

    [Fact]
    public void BeraknaOBKategori_TisdagTidigMorgon_VardagNatt()
    {
        // Tisdag 05:00 → VardagNatt (O-tilläggstid C, före 06:00)
        var datum = new DateOnly(2025, 3, 11); // Tisdag
        Assert.Equal(OBCategory.VardagNatt, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(5, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_FredagKvallEfterAB25_Helg()
    {
        // AB 25 (fr.o.m. 2025-04-01): fredag 17:00–24:00 är O-tilläggstid B (helg), inte D (kväll)
        var fredag = new DateOnly(2025, 5, 9); // Fredag efter 2025-04-01
        Assert.Equal(OBCategory.Helg, SvenskaHelgdagar.BeraknaOBKategori(fredag, new TimeOnly(17, 0)));
        Assert.Equal(OBCategory.Helg, SvenskaHelgdagar.BeraknaOBKategori(fredag, new TimeOnly(20, 0)));
        Assert.Equal(OBCategory.Helg, SvenskaHelgdagar.BeraknaOBKategori(fredag, new TimeOnly(23, 0)));
        // Före 17:00 på fredag: dagtid, ingen OB
        Assert.Equal(OBCategory.Ingen, SvenskaHelgdagar.BeraknaOBKategori(fredag, new TimeOnly(16, 59)));
    }

    [Fact]
    public void BeraknaOBKategori_FredagKvallForeAB25_HelgFran19()
    {
        // Före 2025-04-01: fredagshelgen börjar kl. 19:00
        var fredag = new DateOnly(2025, 3, 14); // Fredag före 2025-04-01
        Assert.Equal(OBCategory.Ingen, SvenskaHelgdagar.BeraknaOBKategori(fredag, new TimeOnly(17, 30)));
        Assert.Equal(OBCategory.Helg, SvenskaHelgdagar.BeraknaOBKategori(fredag, new TimeOnly(19, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_Lordag_Helg()
    {
        // Lördag 14:00 → Helg
        var datum = new DateOnly(2025, 3, 8); // Lördag
        Assert.Equal(OBCategory.Helg, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(14, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_Sondag_Helg()
    {
        // Söndag 10:00 → Helg
        var datum = new DateOnly(2025, 3, 9); // Söndag
        Assert.Equal(OBCategory.Helg, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(10, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_Julafton_Storhelg()
    {
        // Julafton 14:00 → Storhelg
        var datum = new DateOnly(2025, 12, 24);
        Assert.Equal(OBCategory.Storhelg, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(14, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_Nyarsafton_Storhelg()
    {
        var datum = new DateOnly(2025, 12, 31);
        Assert.Equal(OBCategory.Storhelg, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(20, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_Paskafton_Storhelg()
    {
        var datum = new DateOnly(2025, 4, 19); // Påskafton 2025
        Assert.Equal(OBCategory.Storhelg, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(8, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_Midsommarafton_Storhelg()
    {
        var datum = new DateOnly(2025, 6, 20); // Midsommarafton 2025
        Assert.Equal(OBCategory.Storhelg, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(15, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_KristiHimmelsfardsdag_Helg()
    {
        // Kristi himmelsfärdsdag är helgdag → O-tilläggstid B, INTE storhelg (AB 25 § 21)
        var datum = new DateOnly(2025, 5, 29); // Kristi himmelsfärdsdag 2025
        Assert.Equal(OBCategory.Helg, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(12, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_Langfredagen_Storhelg()
    {
        var datum = new DateOnly(2025, 4, 18); // Långfredagen 2025
        Assert.Equal(OBCategory.Storhelg, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(10, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_SkartorsdagKvall_Storhelg()
    {
        // Skärtorsdagen från kl. 18.00 ingår i storhelgen (AB 25 § 21)
        var datum = new DateOnly(2025, 4, 17);
        Assert.Equal(OBCategory.Storhelg, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(19, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_MandagEfterMidsommarFore07_Storhelg()
    {
        // Storhelgen efter midsommar löper till kl. 07.00 på måndagen
        var datum = new DateOnly(2025, 6, 23);
        Assert.Equal(OBCategory.Storhelg, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(6, 0)));
        Assert.Equal(OBCategory.Ingen, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(9, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_VanligHelgdagForsaMaj_Helg()
    {
        // Första maj 2025 = torsdag, helgdag men inte storhelg
        var datum = new DateOnly(2025, 5, 1);
        Assert.Equal(OBCategory.Helg, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(14, 0)));
    }

    [Fact]
    public void BeraknaOBKategori_Nationaldagen_Helg()
    {
        // Nationaldagen 2025 = fredag, helgdag men inte storhelg
        var datum = new DateOnly(2025, 6, 6);
        Assert.Equal(OBCategory.Helg, SvenskaHelgdagar.BeraknaOBKategori(datum, new TimeOnly(10, 0)));
    }

    #endregion

    #region HelgdagarForAr

    [Fact]
    public void HelgdagarForAr_2025_ContainsAllExpected()
    {
        var helgdagar = SvenskaHelgdagar.HelgdagarForAr(2025);

        // 8 fasta + 4 påsk + 1 Kristi himmelsfärd + 1 pingstdagen
        // + 2 midsommar + 1 alla helgons dag = 17
        Assert.Equal(17, helgdagar.Count);

        // Fasta
        Assert.Contains(new DateOnly(2025, 1, 1), helgdagar);
        Assert.Contains(new DateOnly(2025, 1, 6), helgdagar);
        Assert.Contains(new DateOnly(2025, 5, 1), helgdagar);
        Assert.Contains(new DateOnly(2025, 6, 6), helgdagar);
        Assert.Contains(new DateOnly(2025, 12, 24), helgdagar);
        Assert.Contains(new DateOnly(2025, 12, 25), helgdagar);
        Assert.Contains(new DateOnly(2025, 12, 26), helgdagar);
        Assert.Contains(new DateOnly(2025, 12, 31), helgdagar);

        // Rörliga 2025 (påsk 20 april)
        Assert.Contains(new DateOnly(2025, 4, 18), helgdagar); // Långfredagen
        Assert.Contains(new DateOnly(2025, 4, 19), helgdagar); // Påskafton
        Assert.Contains(new DateOnly(2025, 4, 20), helgdagar); // Påskdagen
        Assert.Contains(new DateOnly(2025, 4, 21), helgdagar); // Annandag påsk
        Assert.Contains(new DateOnly(2025, 5, 29), helgdagar); // Kristi himmelsfärd
        Assert.Contains(new DateOnly(2025, 6, 8), helgdagar);  // Pingstdagen
        Assert.Contains(new DateOnly(2025, 6, 20), helgdagar); // Midsommarafton
        Assert.Contains(new DateOnly(2025, 6, 21), helgdagar); // Midsommardagen
        Assert.Contains(new DateOnly(2025, 11, 1), helgdagar); // Alla helgons dag
    }

    [Fact]
    public void HelgdagarForAr_ArSorterade()
    {
        var helgdagar = SvenskaHelgdagar.HelgdagarForAr(2025);
        for (int i = 1; i < helgdagar.Count; i++)
        {
            Assert.True(helgdagar[i] >= helgdagar[i - 1],
                $"Helgdagar är inte sorterade: {helgdagar[i - 1]} efter {helgdagar[i]}");
        }
    }

    #endregion

    #region Påskberäkning

    [Theory]
    [InlineData(2024, 3, 31)]  // 2024: 31 mars
    [InlineData(2025, 4, 20)]  // 2025: 20 april
    [InlineData(2026, 4, 5)]   // 2026: 5 april
    [InlineData(2027, 3, 28)]  // 2027: 28 mars
    [InlineData(2028, 4, 16)]  // 2028: 16 april
    public void BeraknaPaskdagen_KandaAr_CorrectDate(int year, int month, int day)
    {
        var pask = SvenskaHelgdagar.BeraknaPaskdagen(year);
        Assert.Equal(new DateOnly(year, month, day), pask);
    }

    #endregion

    #region Midsommar

    [Theory]
    [InlineData(2024, 6, 22)]  // 2024: 22 juni (lördag)
    [InlineData(2025, 6, 21)]  // 2025: 21 juni (lördag)
    [InlineData(2026, 6, 20)]  // 2026: 20 juni (lördag)
    public void BeraknaMidsommardagen_CorrectDates(int year, int month, int day)
    {
        var midsommar = SvenskaHelgdagar.BeraknaMidsommardagen(year);
        Assert.Equal(new DateOnly(year, month, day), midsommar);
        Assert.Equal(DayOfWeek.Saturday, midsommar.DayOfWeek);
    }

    #endregion
}
