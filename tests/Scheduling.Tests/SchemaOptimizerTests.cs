using RegionHR.Infrastructure.Scheduling;
using Xunit;

namespace RegionHR.Scheduling.Tests;

/// <summary>
/// Tester för att SchemaOptimizer.ViloRegelBrott faktiskt räknas via
/// ArbetstidslagenValidator och inte längre är hårdkodat till 0.
/// </summary>
public class SchemaOptimizerTests
{
    private readonly SchemaOptimizer _sut = new();

    [Fact]
    public void Optimera_RenaDagpass_GerNollVilobrott()
    {
        // 1 person, endast dagpass mån-ons → 15h vila mellan pass, ingen veckovilebrist.
        var request = new SchemaRequest(
            Period: (new DateOnly(2025, 3, 17), new DateOnly(2025, 3, 19)),
            TillgangligPersonal: ["Anna"],
            PassTyper: [new PassTyp("Dag", new TimeSpan(7, 0, 0), new TimeSpan(16, 0, 0), 1)]);

        var forslag = _sut.Optimera(request);

        Assert.Equal(3, forslag.TotalPass);
        Assert.Equal(0, forslag.ViloRegelBrott);
    }

    [Fact]
    public void Optimera_DagOchNattSammaPerson_DetekterarVilobrott()
    {
        // 1 person tvingas ta både dag- och nattpass varje dag → dygnsvila under 11h.
        var request = new SchemaRequest(
            Period: (new DateOnly(2025, 3, 17), new DateOnly(2025, 3, 19)),
            TillgangligPersonal: ["Anna"],
            PassTyper:
            [
                new PassTyp("Dag", new TimeSpan(7, 0, 0), new TimeSpan(16, 0, 0), 1),
                new PassTyp("Natt", new TimeSpan(21, 0, 0), new TimeSpan(7, 0, 0), 1)
            ]);

        var forslag = _sut.Optimera(request);

        Assert.Equal(6, forslag.TotalPass);
        Assert.True(forslag.ViloRegelBrott > 0,
            $"Förväntade minst ett vilobrott men fick {forslag.ViloRegelBrott}.");
    }

    [Fact]
    public void Optimera_TvaPersonerDelarPass_FarreVilobrott()
    {
        // Med två personer fördelas dag/natt växelvis → färre (helst inga) dygnsvilebrott
        // jämfört med en ensam person.
        var enPerson = new SchemaRequest(
            Period: (new DateOnly(2025, 3, 17), new DateOnly(2025, 3, 19)),
            TillgangligPersonal: ["Anna"],
            PassTyper:
            [
                new PassTyp("Dag", new TimeSpan(7, 0, 0), new TimeSpan(16, 0, 0), 1),
                new PassTyp("Natt", new TimeSpan(21, 0, 0), new TimeSpan(7, 0, 0), 1)
            ]);

        var tvaPersoner = enPerson with { TillgangligPersonal = ["Anna", "Bertil"] };

        var brottEn = _sut.Optimera(enPerson).ViloRegelBrott;
        var brottTva = _sut.Optimera(tvaPersoner).ViloRegelBrott;

        Assert.True(brottTva < brottEn,
            $"Två personer ({brottTva}) borde ge färre vilobrott än en ({brottEn}).");
    }
}
