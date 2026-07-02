using RegionHR.Reporting.Engine;
using Xunit;

namespace RegionHR.Reporting.Tests;

public class CronScheduleTests
{
    [Fact]
    public void TryParse_InvalidExpressions_ReturnNull()
    {
        Assert.Null(CronSchedule.TryParse(null));
        Assert.Null(CronSchedule.TryParse(""));
        Assert.Null(CronSchedule.TryParse("not a cron"));
        Assert.Null(CronSchedule.TryParse("0 6 * *"));      // 4 fält
        Assert.Null(CronSchedule.TryParse("0 6 * * * *"));  // 6 fält
        Assert.Null(CronSchedule.TryParse("99 6 1 * *"));   // minut utanför intervall
    }

    [Fact]
    public void Monthly_MatchesFirstOfMonthAtSix()
    {
        var cron = CronSchedule.TryParse("0 6 1 * *");
        Assert.NotNull(cron);
        Assert.True(cron!.Matchar(new DateTime(2026, 4, 1, 6, 0, 0)));
        Assert.False(cron.Matchar(new DateTime(2026, 4, 2, 6, 0, 0)));
        Assert.False(cron.Matchar(new DateTime(2026, 4, 1, 7, 0, 0)));
    }

    [Fact]
    public void Daily_Keyword_MatchesEveryDayAtSix()
    {
        var cron = CronSchedule.TryParse("Daily");
        Assert.NotNull(cron);
        Assert.True(cron!.Matchar(new DateTime(2026, 3, 15, 6, 0, 0)));
        Assert.True(cron.Matchar(new DateTime(2026, 3, 16, 6, 0, 0)));
        Assert.False(cron.Matchar(new DateTime(2026, 3, 16, 8, 0, 0)));
    }

    [Fact]
    public void Weekly_MatchesOnlyOnMonday()
    {
        var cron = CronSchedule.TryParse("0 6 * * 1");
        Assert.NotNull(cron);

        var d = new DateTime(2026, 3, 1, 6, 0, 0);
        while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);

        Assert.True(cron!.Matchar(d));
        Assert.False(cron.Matchar(d.AddDays(1)));
    }

    [Fact]
    public void StepAndList_AreSupported()
    {
        var cron = CronSchedule.TryParse("*/15 * * * *");
        Assert.NotNull(cron);
        Assert.True(cron!.Matchar(new DateTime(2026, 3, 1, 10, 0, 0)));
        Assert.True(cron.Matchar(new DateTime(2026, 3, 1, 10, 15, 0)));
        Assert.True(cron.Matchar(new DateTime(2026, 3, 1, 10, 45, 0)));
        Assert.False(cron.Matchar(new DateTime(2026, 3, 1, 10, 20, 0)));

        var list = CronSchedule.TryParse("0 6,18 * * *");
        Assert.NotNull(list);
        Assert.True(list!.Matchar(new DateTime(2026, 3, 1, 6, 0, 0)));
        Assert.True(list.Matchar(new DateTime(2026, 3, 1, 18, 0, 0)));
        Assert.False(list.Matchar(new DateTime(2026, 3, 1, 12, 0, 0)));
    }

    [Fact]
    public void NastaEfter_DailyReturnsNextOccurrence()
    {
        var cron = CronSchedule.TryParse("0 6 * * *")!;
        var nasta = cron.NastaEfter(new DateTime(2026, 3, 15, 7, 0, 0));
        Assert.Equal(new DateTime(2026, 3, 16, 6, 0, 0), nasta);
    }

    [Fact]
    public void ArForfallenSedan_RespectsInterval()
    {
        var cron = CronSchedule.TryParse("0 6 * * *")!; // dagligen 06:00

        // Nästa körning (06:00) ligger inom fönstret → förfallen.
        Assert.True(cron.ArForfallenSedan(
            new DateTime(2026, 3, 15, 5, 0, 0),
            new DateTime(2026, 3, 15, 6, 30, 0)));

        // Redan kört idag 06:30 → nästa är imorgon, ej förfallen kl 07:00.
        Assert.False(cron.ArForfallenSedan(
            new DateTime(2026, 3, 15, 6, 30, 0),
            new DateTime(2026, 3, 15, 7, 0, 0)));
    }

    [Fact]
    public void SundayAsSevenIsNormalised()
    {
        var cron = CronSchedule.TryParse("0 6 * * 7"); // 7 = söndag
        Assert.NotNull(cron);

        var d = new DateTime(2026, 3, 1, 6, 0, 0);
        while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(1);

        Assert.True(cron!.Matchar(d));
        Assert.False(cron.Matchar(d.AddDays(1)));
    }
}
