using Xunit;
using Microsoft.EntityFrameworkCore;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Travel.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Travel.Tests;

/// <summary>
/// Kvitto-metadata på utlägg: <see cref="ExpenseItem.KvittoBildId"/> ska sättas av domänen,
/// persisteras och kunna laddas tillbaka via navigationen <c>Utlagg</c>. Bevisar också att
/// totalbeloppet ackumulerar flera utlägg när kravet laddas med sina utlägg (regression för
/// att inte tappa tidigare utlägg vid ny-utläggs-uppdatering i UI:t).
/// </summary>
public class ExpenseReceiptTests
{
    private static DbContextOptions<RegionHRDbContext> NyaOptions() =>
        new DbContextOptionsBuilder<RegionHRDbContext>()
            .UseInMemoryDatabase($"Travel-Kvitto-{Guid.NewGuid()}")
            .Options;

    // ---------- Domän: KvittoBildId ----------

    [Fact]
    public void LaggTillUtlagg_med_kvitto_satter_KvittoBildId_och_belopp()
    {
        var claim = TravelClaim.Skapa(EmployeeId.New(), "Konferens Göteborg", new DateOnly(2026, 5, 4));

        claim.LaggTillUtlagg("Taxi", Money.SEK(240m), kvittoBildId: "kvitton/2026-05/abc123_taxikvitto.pdf");

        var utlagg = Assert.Single(claim.Utlagg);
        Assert.Equal("Taxi", utlagg.Beskrivning);
        Assert.Equal(240m, utlagg.Belopp.Amount);
        Assert.Equal("kvitton/2026-05/abc123_taxikvitto.pdf", utlagg.KvittoBildId);
        Assert.Equal(240m, claim.TotalBelopp.Amount);
    }

    [Fact]
    public void LaggTillUtlagg_utan_kvitto_ger_null_KvittoBildId()
    {
        var claim = TravelClaim.Skapa(EmployeeId.New(), "Lunchmöte", new DateOnly(2026, 5, 4));

        claim.LaggTillUtlagg("Lunch", Money.SEK(180m));

        Assert.Null(Assert.Single(claim.Utlagg).KvittoBildId);
    }

    [Fact]
    public void Flera_utlagg_ackumuleras_i_totalbeloppet()
    {
        var claim = TravelClaim.Skapa(EmployeeId.New(), "Tjänsteresa", new DateOnly(2026, 5, 4));

        claim.LaggTillUtlagg("Taxi", Money.SEK(240m), "kvitton/2026-05/aaa_taxi.pdf");
        claim.LaggTillUtlagg("Parkering", Money.SEK(95m));
        claim.LaggTillUtlagg("Hotell", Money.SEK(1450m), "kvitton/2026-05/bbb_hotell.jpg");

        Assert.Equal(3, claim.Utlagg.Count);
        Assert.Equal(240m + 95m + 1450m, claim.TotalBelopp.Amount);
    }

    [Fact]
    public void Totalbelopp_summerar_traktamente_mil_och_utlagg()
    {
        var claim = TravelClaim.Skapa(EmployeeId.New(), "Kombinerad resa", new DateOnly(2026, 5, 4));

        claim.SattTraktamente(helaDagar: 2, halvaDagar: 1); // 2*300 + 1*150 = 750
        claim.SattMilersattning(10m);                        // 10 * 25 = 250
        claim.LaggTillUtlagg("Taxi", Money.SEK(240m), "kvitton/2026-05/aaa_taxi.pdf");

        Assert.Equal(750m + 250m + 240m, claim.TotalBelopp.Amount);
    }

    // ---------- Persistens: round-trip via Include(Utlagg) ----------

    [Fact]
    public async Task Utlagg_med_kvittoBildId_persisteras_och_laddas_via_Include()
    {
        var options = NyaOptions();
        Guid id;

        await using (var db = new RegionHRDbContext(options))
        {
            var claim = TravelClaim.Skapa(EmployeeId.New(), "Utlandsresa", new DateOnly(2026, 5, 4));
            claim.LaggTillUtlagg("Flygbiljett", Money.SEK(3200m), "kvitton/2026-05/xyz789_flyg.pdf");
            claim.LaggTillUtlagg("Taxi", Money.SEK(240m)); // utan kvitto
            db.TravelClaims.Add(claim);
            await db.SaveChangesAsync();
            id = claim.Id;
        }

        await using (var verify = new RegionHRDbContext(options))
        {
            var reloaded = await verify.TravelClaims
                .Include(c => c.Utlagg)
                .FirstAsync(c => c.Id == id);

            Assert.Equal(2, reloaded.Utlagg.Count);

            var medKvitto = reloaded.Utlagg.Single(u => u.Beskrivning == "Flygbiljett");
            Assert.Equal("kvitton/2026-05/xyz789_flyg.pdf", medKvitto.KvittoBildId);
            Assert.Equal(3200m, medKvitto.Belopp.Amount);

            var utanKvitto = reloaded.Utlagg.Single(u => u.Beskrivning == "Taxi");
            Assert.Null(utanKvitto.KvittoBildId);

            Assert.Equal(3200m + 240m, reloaded.TotalBelopp.Amount);
        }
    }

    [Fact]
    public async Task Nytt_utlagg_pa_laddat_krav_tappar_inte_tidigare_utlagg_i_totalen()
    {
        // Speglar UI-flödet: ladda kravet MED sina utlägg (Include), lägg till ett nytt utlägg,
        // spara. BeraknaTotal ska då räkna in både gamla och nya utlägg — inte nollställas.
        var options = NyaOptions();
        Guid id;

        await using (var db = new RegionHRDbContext(options))
        {
            var claim = TravelClaim.Skapa(EmployeeId.New(), "Tjänsteresa", new DateOnly(2026, 5, 4));
            claim.LaggTillUtlagg("Taxi", Money.SEK(240m), "kvitton/2026-05/aaa_taxi.pdf");
            db.TravelClaims.Add(claim);
            await db.SaveChangesAsync();
            id = claim.Id;
        }

        // Ladda om kravet MED sina utlägg (Include) och lägg till ett nytt utlägg — mirror UI-flödet.
        // Regressionsvakt mot "nollställ": BeraknaTotal ska räkna in BÅDE det tidigare persisterade
        // utlägget och det nya, inte tappa det gamla. (Verifieras på det omladdade aggregatet; själva
        // den inkrementella barn-persisteringen körs mot Postgres i drift — InMemory-providern modellerar
        // inte cross-context-add av barn i en separat-mappad samling.)
        await using (var db = new RegionHRDbContext(options))
        {
            var claim = await db.TravelClaims.Include(c => c.Utlagg).FirstAsync(c => c.Id == id);
            Assert.Single(claim.Utlagg);                        // det tidigare utlägget kom med vid reload
            Assert.Equal(240m, claim.TotalBelopp.Amount);

            claim.LaggTillUtlagg("Hotell", Money.SEK(1450m), "kvitton/2026-05/bbb_hotell.jpg");

            Assert.Equal(2, claim.Utlagg.Count);                // gamla utlägget finns kvar
            Assert.Equal(240m + 1450m, claim.TotalBelopp.Amount); // totalen ackumuleras, nollställs inte
        }
    }
}
