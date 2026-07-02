using Microsoft.EntityFrameworkCore;
using RegionHR.IntegrationHub.Framework;
using RegionHR.Infrastructure.Persistence;

namespace RegionHR.Infrastructure.Integrations.Framework;

/// <summary>
/// EF Core-backad <see cref="IIntegrationRunLogStore"/>. Läser/skriver
/// <see cref="IntegrationRunLog"/> via <c>db.Set&lt;IntegrationRunLog&gt;()</c>
/// (entiteten registreras genom sin IEntityTypeConfiguration — ingen ändring i
/// RegionHRDbContext behövs).
/// </summary>
public sealed class EfIntegrationRunLogStore : IIntegrationRunLogStore
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;

    public EfIntegrationRunLogStore(IDbContextFactory<RegionHRDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task SparaAsync(IntegrationKorningsResultat resultat, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Set<IntegrationRunLog>().Add(IntegrationRunLog.Fran(resultat));
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<IntegrationKorningsResultat>> HamtaSenastePerIntegrationAsync(
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Övervakningsvyn: hämta ett rimligt fönster och gruppera på klientsidan
        // (undviker svåröversatt GroupBy-First i EF; tabellen är liten i drift).
        var senaste = await db.Set<IntegrationRunLog>()
            .AsNoTracking()
            .OrderByDescending(x => x.StartadUtc)
            .Take(2000)
            .ToListAsync(ct);

        return senaste
            .GroupBy(x => x.IntegrationKey)
            .Select(g => g.First().TillResultat())
            .ToList();
    }

    public async Task<IReadOnlyList<IntegrationKorningsResultat>> HamtaHistorikAsync(
        string integrationKey, int antal = 20, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rader = await db.Set<IntegrationRunLog>()
            .AsNoTracking()
            .Where(x => x.IntegrationKey == integrationKey)
            .OrderByDescending(x => x.StartadUtc)
            .Take(antal)
            .ToListAsync(ct);

        return rader.Select(x => x.TillResultat()).ToList();
    }
}
