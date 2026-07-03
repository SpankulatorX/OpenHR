using RegionHR.LAS.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.LAS.Tests;

public class LASAccumulationTests
{
    [Fact]
    public void NyAckumulering_StartarPaNoll()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        Assert.Equal(0, acc.AckumuleradeDagar);
        Assert.Equal(LASStatus.UnderGrans, acc.Status);
    }

    [Fact]
    public void LaggTillPeriod_AckumulerarDagar()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        var start = DateOnly.FromDateTime(DateTime.Today.AddMonths(-3));
        var slut = start.AddDays(89); // 90 dagar

        acc.LaggTillPeriod(start, slut);

        Assert.Equal(90, acc.AckumuleradeDagar);
    }

    [Fact]
    public void SAVA_Over10Manader_GerAlarm()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-310));
        var slut = DateOnly.FromDateTime(DateTime.Today);

        acc.LaggTillPeriod(start, slut);

        Assert.Equal(LASStatus.NaraGrans, acc.Status);
    }

    [Fact]
    public void SAVA_Over11Manader_GerKritisktAlarm()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-340));
        var slut = DateOnly.FromDateTime(DateTime.Today);

        acc.LaggTillPeriod(start, slut);

        Assert.Equal(LASStatus.KritiskNara, acc.Status);
    }

    [Fact]
    public void SAVA_Over12Manader_TriggrarKonvertering()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-370));
        var slut = DateOnly.FromDateTime(DateTime.Today);

        acc.LaggTillPeriod(start, slut);

        Assert.Equal(LASStatus.KonverteradTillTillsvidare, acc.Status);
        Assert.NotNull(acc.KonverteringsDatum);
    }

    [Fact]
    public void Foretradesratt_SAVA_Beviljas_Over9Manader()
    {
        // SAVA: företrädesrätt efter 9 månader (~274 dagar) i en 3-årsperiod
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-290));
        var slut = DateOnly.FromDateTime(DateTime.Today.AddDays(-10));

        acc.LaggTillPeriod(start, slut);
        acc.SattForetradesratt(slut);

        Assert.True(acc.HarForetradesratt);
        Assert.NotNull(acc.ForetradesrattUtgar);
    }

    [Fact]
    public void Foretradesratt_SAVA_Beviljas_Ej_Under9Manader()
    {
        // SAVA: under 9 månader (~274 dagar) → ingen företrädesrätt
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-200));
        var slut = DateOnly.FromDateTime(DateTime.Today.AddDays(-10));

        acc.LaggTillPeriod(start, slut);
        acc.SattForetradesratt(slut);

        Assert.False(acc.HarForetradesratt);
    }

    [Fact]
    public void EjLAS_ForTillsvidare_KastarException()
    {
        Assert.Throws<ArgumentException>(() =>
            LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.Tillsvidare));
    }

    [Fact]
    public void Vikariat_Over2Ar_TriggrarKonvertering()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.Vikariat);
        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-740));
        var slut = DateOnly.FromDateTime(DateTime.Today);

        acc.LaggTillPeriod(start, slut);

        Assert.Equal(LASStatus.KonverteradTillTillsvidare, acc.Status);
    }

    // ── Off-by-one: lagen säger "mer än" — konvertering först när gränsen PASSERAS ──

    [Fact]
    public void SAVA_Exakt365Dagar_KonverterasInte()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        var slut = DateOnly.FromDateTime(DateTime.Today);
        var start = slut.AddDays(-364); // exakt 365 dagar

        acc.LaggTillPeriod(start, slut);

        Assert.Equal(365, acc.AckumuleradeSavaDagar);
        Assert.Equal(LASStatus.KritiskNara, acc.Status); // nära gränsen, men INTE konverterad
        Assert.Null(acc.KonverteringsDatum);
    }

    [Fact]
    public void SAVA_366Dagar_GransenPasserad_Konverteras()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        var slut = DateOnly.FromDateTime(DateTime.Today);
        var start = slut.AddDays(-365); // 366 dagar — mer än 365

        acc.LaggTillPeriod(start, slut);

        Assert.Equal(LASStatus.KonverteradTillTillsvidare, acc.Status);
    }

    [Fact]
    public void Vikariat_Exakt730Dagar_KonverterasInte()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.Vikariat);
        var slut = DateOnly.FromDateTime(DateTime.Today);
        var start = slut.AddDays(-729); // exakt 730 dagar

        acc.LaggTillPeriod(start, slut);

        Assert.Equal(730, acc.AckumuleradeVikariatDagar);
        Assert.NotEqual(LASStatus.KonverteradTillTillsvidare, acc.Status);
    }

    [Fact]
    public void Foretradesratt_BeviljasEj_VidExakt274Dagar()
    {
        // Lagen säger "mer än" 9 månader — exakt 274 dagar räcker INTE.
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        var slut = DateOnly.FromDateTime(DateTime.Today.AddDays(-10));
        var start = slut.AddDays(-273); // exakt 274 dagar

        acc.LaggTillPeriod(start, slut);
        acc.SattForetradesratt(slut);

        Assert.False(acc.HarForetradesratt);
    }

    // ── SAVA- och vikariatstid ackumuleras separat mot sina respektive gränser ──

    [Fact]
    public void BlandadeFormer_AckumulerasSeparat_MotRespektiveGrans()
    {
        // 200 SAVA-dagar + 400 vikariatsdagar får inte summeras till 600 mot någon
        // enskild gräns: SAVA 200 ≤ 365 och vikariat 400 ≤ 730 → ingen konvertering.
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        var idag = DateOnly.FromDateTime(DateTime.Today);

        acc.LaggTillPeriod(idag.AddDays(-799), idag.AddDays(-600), form: EmploymentType.SAVA);     // 200 dagar
        acc.LaggTillPeriod(idag.AddDays(-500), idag.AddDays(-101), form: EmploymentType.Vikariat); // 400 dagar

        Assert.Equal(200, acc.AckumuleradeSavaDagar);
        Assert.Equal(400, acc.AckumuleradeVikariatDagar);
        Assert.Equal(600, acc.AckumuleradeDagar);
        Assert.NotEqual(LASStatus.KonverteradTillTillsvidare, acc.Status);
    }

    [Fact]
    public void Foretradesratt_Vikariatstid_BedomsMotVikariatskravet()
    {
        // Vikariatstid bedöms mot vikariatskravet (mer än 365 dagar), inte SAVA-kravet
        // (274) — 300 vikariatsdagar ger alltså INTE företrädesrätt.
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        var slut = DateOnly.FromDateTime(DateTime.Today.AddDays(-10));

        acc.LaggTillPeriod(slut.AddDays(-299), slut, form: EmploymentType.Vikariat); // 300 dagar
        acc.SattForetradesratt(slut);

        Assert.False(acc.HarForetradesratt);
    }

    [Fact]
    public void LaggTillPeriod_MedOgiltigForm_KastarException()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        var idag = DateOnly.FromDateTime(DateTime.Today);

        Assert.Throws<ArgumentException>(() =>
            acc.LaggTillPeriod(idag.AddDays(-10), idag, form: EmploymentType.Tillsvidare));
    }

    [Fact]
    public void Omberakna_AvkonverterarInte_NarFonstretGliderVidare()
    {
        // Den återkommande omberäkningen (dagligt jobb) flyttar 5-årsfönstret framåt.
        // En redan konverterad ackumulering får INTE "avkonverteras" när perioderna
        // så småningom glider ut ur fönstret — konvertering är enkelriktad.
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        var idag = DateOnly.FromDateTime(DateTime.Today);
        acc.LaggTillPeriod(idag.AddDays(-370), idag); // 371 dagar > 365 → konverterad
        Assert.Equal(LASStatus.KonverteradTillTillsvidare, acc.Status);

        acc.Omberakna(idag.AddYears(6)); // fönstret har glidit förbi alla perioder

        Assert.Equal(LASStatus.KonverteradTillTillsvidare, acc.Status);
        Assert.NotNull(acc.KonverteringsDatum);
    }
}
