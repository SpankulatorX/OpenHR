using Microsoft.EntityFrameworkCore;
using RegionHR.Infrastructure.Persistence;
using RegionHR.LAS.Domain;
using RegionHR.LAS.Services;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.LAS;

/// <summary>
/// EF Core-implementation av <see cref="ILASRepository"/>.
///
/// Till skillnad från de generiska repositoryna (som förlitar sig på IUnitOfWork)
/// persisterar detta repository själv i Add/Update. Det gör att LASService kan
/// anropas direkt från Blazor-sidor utan att sidan behöver hantera en enhet av arbete,
/// och utan att LASService konstruktorsignatur ändras.
/// </summary>
public sealed class LASRepository : ILASRepository
{
    private readonly RegionHRDbContext _db;

    public LASRepository(RegionHRDbContext db) => _db = db;

    public async Task<LASAccumulation?> GetByEmployeeAsync(EmployeeId id, CancellationToken ct)
    {
        return await _db.LASAccumulations
            .Include(a => a.Perioder)
            .Include(a => a.Handelser)
            .FirstOrDefaultAsync(a => a.AnstallId == id, ct);
    }

    public async Task<IReadOnlyList<LASAccumulation>> GetAllaAktiva(CancellationToken ct)
    {
        return await _db.LASAccumulations
            .Include(a => a.Perioder)
            .Include(a => a.Handelser)
            .OrderByDescending(a => a.AckumuleradeDagar)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LASAccumulation>> GetByStatus(LASStatus status, CancellationToken ct)
    {
        return await _db.LASAccumulations
            .Include(a => a.Perioder)
            .Include(a => a.Handelser)
            .Where(a => a.Status == status)
            .OrderByDescending(a => a.AckumuleradeDagar)
            .ToListAsync(ct);
    }

    public async Task AddAsync(LASAccumulation acc, CancellationToken ct)
    {
        await _db.LASAccumulations.AddAsync(acc, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(LASAccumulation acc, CancellationToken ct)
    {
        // Ackumuleringen laddas spårad via Get* ovan, så EF upptäcker ändrade,
        // tillagda och borttagna perioder/händelser automatiskt.
        if (_db.Entry(acc).State == EntityState.Detached)
            _db.LASAccumulations.Update(acc);

        // En borttagen period/händelse (t.ex. HR-korrigering) kan, om FK:n är valfri,
        // annars bara få sin FK nollad och lämna en föräldralös rad. Markera sådana
        // frånkopplade barn som raderade så att korrigeringen faktiskt persisteras.
        _db.ChangeTracker.DetectChanges();
        RaderaFrankoppladeBarn<LASPeriod>();
        RaderaFrankoppladeBarn<LASEvent>();

        await _db.SaveChangesAsync(ct);
    }

    private void RaderaFrankoppladeBarn<TChild>() where TChild : class
    {
        foreach (var entry in _db.ChangeTracker.Entries<TChild>())
        {
            if (entry.State == EntityState.Modified &&
                entry.Metadata.FindProperty("accumulation_id") is not null &&
                entry.Property("accumulation_id").CurrentValue is null)
            {
                entry.State = EntityState.Deleted;
            }
        }
    }
}
