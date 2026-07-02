using Xunit;
using Microsoft.EntityFrameworkCore;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Travel.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Travel.Tests;

/// <summary>
/// Bevisar persistens-buggen och dess fix: attest måste läsa entiteten i SAMMA
/// DbContext som anropar SaveChanges. En detached kopia (laddad i en annan
/// context) persisteras aldrig.
/// </summary>
public class TravelClaimPersistensTests
{
    private static DbContextOptions<RegionHRDbContext> NyaOptions() =>
        new DbContextOptionsBuilder<RegionHRDbContext>()
            .UseInMemoryDatabase($"Travel-Persistens-{Guid.NewGuid()}")
            .Options;

    private static async Task<Guid> SeedaInskickatKravAsync(DbContextOptions<RegionHRDbContext> options)
    {
        await using var db = new RegionHRDbContext(options);
        var claim = TravelClaim.Skapa(EmployeeId.New(), "Tjänsteresa Stockholm", new DateOnly(2026, 6, 1));
        claim.SkickaIn();
        db.TravelClaims.Add(claim);
        await db.SaveChangesAsync();
        return claim.Id;
    }

    [Fact]
    public async Task Attest_pa_detached_entitet_persisteras_INTE()
    {
        // Detta reproducerar den ursprungliga buggen (godkännande försvann).
        var options = NyaOptions();
        var id = await SeedaInskickatKravAsync(options);

        TravelClaim detached;
        await using (var laddContext = new RegionHRDbContext(options))
        {
            detached = await laddContext.TravelClaims.FirstAsync(c => c.Id == id);
        } // laddContext disposed → detached spåras inte längre någonstans

        await using (var sparContext = new RegionHRDbContext(options))
        {
            detached.Attestera(EmployeeId.New(), "Eva Nilsson", attestantArHR: false);
            await sparContext.SaveChangesAsync(); // sparContext spårar inte detached → 0 ändringar
        }

        await using var verifyContext = new RegionHRDbContext(options);
        var reloaded = await verifyContext.TravelClaims.FirstAsync(c => c.Id == id);
        Assert.Equal(TravelClaimStatus.Inskickad, reloaded.Status); // godkännandet gick förlorat
    }

    [Fact]
    public async Task Attest_i_samma_context_persisteras()
    {
        // Den korrekta fixen: läs → ändra → spara i samma context.
        var options = NyaOptions();
        var id = await SeedaInskickatKravAsync(options);

        await using (var db = new RegionHRDbContext(options))
        {
            var claim = await db.TravelClaims.FirstAsync(c => c.Id == id);
            claim.Attestera(EmployeeId.New(), "Eva Nilsson", attestantArHR: false);
            await db.SaveChangesAsync();
        }

        await using var verifyContext = new RegionHRDbContext(options);
        var reloaded = await verifyContext.TravelClaims.FirstAsync(c => c.Id == id);
        Assert.Equal(TravelClaimStatus.Godkand, reloaded.Status);
        Assert.Equal("Eva Nilsson", reloaded.AttesteradAv);
        Assert.NotNull(reloaded.AttesteradVid);
    }

    [Fact]
    public async Task Avvisning_i_samma_context_persisterar_status_och_anledning()
    {
        var options = NyaOptions();
        var id = await SeedaInskickatKravAsync(options);

        await using (var db = new RegionHRDbContext(options))
        {
            var claim = await db.TravelClaims.FirstAsync(c => c.Id == id);
            claim.Avvisa(EmployeeId.New(), "Eva Nilsson", "Kvitto saknas");
            await db.SaveChangesAsync();
        }

        await using var verifyContext = new RegionHRDbContext(options);
        var reloaded = await verifyContext.TravelClaims.FirstAsync(c => c.Id == id);
        Assert.Equal(TravelClaimStatus.Avslagen, reloaded.Status);
        Assert.Equal("Kvitto saknas", reloaded.AvvisningsAnledning);
    }

    [Fact]
    public async Task Utbetalning_flode_lonekorning_kan_plocka_godkanda_krav()
    {
        var options = NyaOptions();
        var id = await SeedaInskickatKravAsync(options);

        // Chef attesterar.
        await using (var db = new RegionHRDbContext(options))
        {
            var claim = await db.TravelClaims.FirstAsync(c => c.Id == id);
            claim.Attestera(EmployeeId.New(), "Eva Nilsson", attestantArHR: false);
            await db.SaveChangesAsync();
        }

        // Lönekörningens pickup-fråga: alla klara-för-utbetalning.
        await using (var payrollContext = new RegionHRDbContext(options))
        {
            var klara = await payrollContext.TravelClaims
                .Where(c => c.Status == TravelClaimStatus.Godkand)
                .ToListAsync();
            Assert.Contains(klara, c => c.Id == id);

            // Efter utbetalning markeras kravet.
            var claim = klara.First(c => c.Id == id);
            claim.MarkeraSomUtbetald();
            await payrollContext.SaveChangesAsync();
        }

        await using var verifyContext = new RegionHRDbContext(options);
        var reloaded = await verifyContext.TravelClaims.FirstAsync(c => c.Id == id);
        Assert.Equal(TravelClaimStatus.Utbetald, reloaded.Status);

        // Kravet ska inte längre dyka upp i pickup-frågan.
        var kvarvarande = await verifyContext.TravelClaims
            .Where(c => c.Status == TravelClaimStatus.Godkand)
            .ToListAsync();
        Assert.DoesNotContain(kvarvarande, c => c.Id == id);
    }
}
