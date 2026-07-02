using Microsoft.EntityFrameworkCore;
using RegionHR.Infrastructure.Persistence;
using RegionHR.LAS.Services;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.LAS;

/// <summary>
/// EF Core-implementation av <see cref="IEmploymentLookup"/>. Slår upp en anställnings
/// form och giltighetsperiod direkt via <see cref="RegionHRDbContext"/> så att
/// LAS auto-kedjan kan bygga en LAS-period från ett <c>EmploymentCreatedEvent</c>
/// (som bara bär anställnings-id, inte datumen).
///
/// Använder den scoped:ade DbContext:en — samma instans som skrev anställningen — så
/// att uppslaget sker inom samma enhet av arbete som domänevent-dispatchen.
/// </summary>
public sealed class EmploymentLookup : IEmploymentLookup
{
    private readonly RegionHRDbContext _db;

    public EmploymentLookup(RegionHRDbContext db) => _db = db;

    public async Task<AnstallningsPeriod?> GetEmploymentAsync(EmploymentId employmentId, CancellationToken ct = default)
    {
        var anstallning = await _db.Employments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employmentId, ct);

        if (anstallning is null)
            return null;

        return new AnstallningsPeriod(
            anstallning.AnstallId,
            anstallning.Anstallningsform,
            anstallning.Giltighetsperiod.Start,
            anstallning.Giltighetsperiod.End);
    }
}
