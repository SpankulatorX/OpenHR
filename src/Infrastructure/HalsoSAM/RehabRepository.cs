using Microsoft.EntityFrameworkCore;
using RegionHR.HalsoSAM.Domain;
using RegionHR.HalsoSAM.Services;
using RegionHR.Infrastructure.Persistence;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.HalsoSAM;

/// <summary>
/// EF Core-implementation av <see cref="IRehabRepository"/>. Persisterar direkt
/// (SaveChanges i Add/Update/Delete) eftersom rehabärenden hanteras som egna
/// transaktioner via <see cref="RegionHR.HalsoSAM.Services.RehabService"/>.
/// </summary>
public sealed class RehabRepository : IRehabRepository
{
    private readonly RegionHRDbContext _db;

    public RehabRepository(RegionHRDbContext db) => _db = db;

    public async Task<RehabCase?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _db.RehabCases
            .Include(r => r.Uppfoljningar)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<RehabCase>> GetByEmployeeAsync(EmployeeId anstallId, CancellationToken ct)
    {
        return await _db.RehabCases
            .Include(r => r.Uppfoljningar)
            .Where(r => r.AnstallId == anstallId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RehabCase>> GetByStatusAsync(RehabStatus status, CancellationToken ct)
    {
        return await _db.RehabCases
            .Include(r => r.Uppfoljningar)
            .Where(r => r.Status == status)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RehabCase>> GetAktivaAsync(CancellationToken ct)
    {
        return await _db.RehabCases
            .Include(r => r.Uppfoljningar)
            .Where(r => r.Status != RehabStatus.Avslutad)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RehabCase>> GetAllAsync(CancellationToken ct)
    {
        return await _db.RehabCases
            .Include(r => r.Uppfoljningar)
            .ToListAsync(ct);
    }

    public async Task AddAsync(RehabCase rehabCase, CancellationToken ct)
    {
        await _db.RehabCases.AddAsync(rehabCase, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(RehabCase rehabCase, CancellationToken ct)
    {
        _db.RehabCases.Update(rehabCase);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.RehabCases.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is not null)
        {
            _db.RehabCases.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
    }
}
