using RegionHR.HalsoSAM.Domain;
using RegionHR.HalsoSAM.Services;
using Xunit;

namespace RegionHR.HalsoSAM.Tests;

/// <summary>
/// Verifierar den automatiska rehab-triggningens signaldetektering:
/// rätt trigger OCH rätt sjukfallsstart (dag 1) så att kedjan förankras korrekt.
/// </summary>
public class RehabSignalTests
{
    private readonly SickLeaveMonitor _monitor = new();

    [Fact]
    public void AnalyseraSignal_Langtidssjuk_GerDag1FranPeriodstart()
    {
        var start = new DateOnly(2026, 2, 1);
        var perioder = new List<SjukfranvaroPeriod>
        {
            new() { StartDatum = start, SlutDatum = start.AddDays(20) } // 21 dagar
        };

        var signal = _monitor.AnalyseraSignal(perioder);

        Assert.NotNull(signal);
        Assert.Equal(RehabTrigger.FjortonSammanhangandeDagar, signal!.Trigger);
        Assert.Equal(start, signal.SjukfallDag1);
    }

    [Fact]
    public void AnalyseraSignal_LangstaPeriodensStart_ValjsSomDag1()
    {
        var kortStart = DateOnly.FromDateTime(DateTime.Today.AddDays(-40));
        var langStart = DateOnly.FromDateTime(DateTime.Today.AddDays(-25));
        var perioder = new List<SjukfranvaroPeriod>
        {
            new() { StartDatum = kortStart, SlutDatum = kortStart.AddDays(3) },   // 4 dagar
            new() { StartDatum = langStart, SlutDatum = langStart.AddDays(19) }   // 20 dagar (kvalificerar)
        };

        var signal = _monitor.AnalyseraSignal(perioder);

        Assert.NotNull(signal);
        Assert.Equal(RehabTrigger.FjortonSammanhangandeDagar, signal!.Trigger);
        Assert.Equal(langStart, signal.SjukfallDag1);
    }

    [Fact]
    public void AnalyseraSignal_SexTillfallen_GerDag1FranSenasteTillfallet()
    {
        var perioder = Enumerable.Range(0, 6)
            .Select(i =>
            {
                var s = DateOnly.FromDateTime(DateTime.Today.AddMonths(-i - 1));
                return new SjukfranvaroPeriod { StartDatum = s, SlutDatum = s.AddDays(2) };
            })
            .ToList();

        var signal = _monitor.AnalyseraSignal(perioder);

        Assert.NotNull(signal);
        Assert.Equal(RehabTrigger.SexTillfallenTolvManader, signal!.Trigger);
        // Senaste tillfället (i = 0) ska bli dag 1.
        var forvantad = perioder.OrderByDescending(p => p.StartDatum).First().StartDatum;
        Assert.Equal(forvantad, signal.SjukfallDag1);
    }

    [Fact]
    public void AnalyseraSignal_UnderTrosklar_GerNull()
    {
        var perioder = new List<SjukfranvaroPeriod>
        {
            new()
            {
                StartDatum = DateOnly.FromDateTime(DateTime.Today.AddMonths(-2)),
                SlutDatum = DateOnly.FromDateTime(DateTime.Today.AddMonths(-2).AddDays(2))
            }
        };

        Assert.Null(_monitor.AnalyseraSignal(perioder));
    }

    [Fact]
    public void AnalyseraSignal_TomInput_GerNull()
    {
        Assert.Null(_monitor.AnalyseraSignal([]));
    }

    [Fact]
    public void Signal_KanFoljasHelaVagen_TillKorrektMilstolpe()
    {
        // Signal -> RehabCase.Skapa(...dag1) -> milstolpar förankrade i dag 1.
        var start = new DateOnly(2026, 4, 1);
        var perioder = new List<SjukfranvaroPeriod>
        {
            new() { StartDatum = start, SlutDatum = start.AddDays(16) }
        };

        var signal = _monitor.AnalyseraSignal(perioder);
        Assert.NotNull(signal);

        var rc = RehabCase.Skapa(RegionHR.SharedKernel.Domain.EmployeeId.New(), signal!.Trigger, signal.SjukfallDag1);

        Assert.Equal(start, rc.SjukfallDag1);
        var ankare = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        Assert.Equal(ankare.AddDays(90), rc.Uppfoljning90Dagar);
    }
}
