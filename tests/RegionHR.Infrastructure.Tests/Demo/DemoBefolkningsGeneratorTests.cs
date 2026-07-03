using RegionHR.Infrastructure.Persistence.Demo;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Infrastructure.Tests.Demo;

/// <summary>
/// Enhetstester för den procedurella demo-befolkningsgeneratorns rena delar
/// (personnummer, viktfördelning, organisationsträd) — kräver ingen databas.
/// </summary>
public class DemoBefolkningsGeneratorTests
{
    private static readonly DateOnly Idag = new(2026, 7, 3);

    [Fact]
    public void PersonnummerGenerator_producerar_giltiga_unika_konskonsekventa_nummer()
    {
        var gen = new PersonnummerGenerator(new Random(12345));
        var sedda = new HashSet<string>();

        for (var i = 0; i < 5000; i++)
        {
            var arKvinna = i % 2 == 0;
            var alder = 20 + i % 48; // 20..67

            var pnr = gen.NastaUnikt(alder, arKvinna, Idag);
            var tolv = (string)pnr;

            // Format: exakt 12 siffror och kan re-parsas av värdesobjektet (Luhn + datum).
            Assert.Equal(12, tolv.Length);
            _ = new Personnummer(tolv); // kastar om ogiltigt

            // Unikhet.
            Assert.True(sedda.Add(tolv), $"Dubblett genererad: {tolv}");

            // Kön konsekvent med begärt kön.
            Assert.Equal(arKvinna ? "Kvinna" : "Man", pnr.LegalGender);

            // Ålder inom demo-intervallet.
            Assert.Equal(alder, Idag.Year - pnr.BirthDate.Year);
        }

        Assert.Equal(5000, gen.AntalGenererade);
    }

    [Fact]
    public void BeraknaLuhnKontrollsiffra_ger_giltigt_personnummer()
    {
        // YYMMDDNNN för 1985-03-15 + födelsenummer 238.
        var kontroll = PersonnummerGenerator.BeraknaLuhnKontrollsiffra("850315238");

        Assert.Equal(4, kontroll);
        _ = new Personnummer("19850315238" + kontroll); // kastar om Luhn inte stämmer
    }

    [Fact]
    public void Reserverade_personnummer_ateranvands_aldrig()
    {
        const string reserverat = "198503152384"; // motsvarar en handplockad demo-användare
        var gen = new PersonnummerGenerator(new Random(1), [reserverat]);

        for (var i = 0; i < 2000; i++)
        {
            var pnr = gen.NastaUnikt(30 + i % 30, i % 2 == 0, Idag);
            Assert.NotEqual(reserverat, (string)pnr);
        }
    }

    [Fact]
    public void BeraknaFordelning_summerar_exakt_till_antal()
    {
        var (_, bemanningsbara) = DemoOrganisation.Bygg(OrganizationId.New(), OrganizationId.New());

        var fordelning = DemoBefolkningsGenerator.BeraknaFordelning(bemanningsbara, 11_000);

        Assert.Equal(bemanningsbara.Count, fordelning.Length);
        Assert.Equal(11_000, fordelning.Sum());
        Assert.All(fordelning, n => Assert.True(n >= 0));
    }

    [Fact]
    public void DemoOrganisation_bygger_realistiskt_trad_med_unika_kostnadsstallen()
    {
        var (enheter, bemanningsbara) = DemoOrganisation.Bygg(OrganizationId.New(), OrganizationId.New());

        Assert.True(enheter.Count > 100, $"Förväntade >100 enheter, fick {enheter.Count}");
        Assert.True(bemanningsbara.Count > 80, $"Förväntade >80 bemanningsbara, fick {bemanningsbara.Count}");

        // Kostnadsställen måste vara unika (inget krockar med varandra).
        var ks = enheter.Select(e => e.Kostnadsstalle).ToList();
        Assert.Equal(ks.Count, ks.Distinct().Count());
    }
}
