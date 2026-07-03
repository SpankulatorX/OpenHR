using Microsoft.EntityFrameworkCore;
using RegionHR.Infrastructure.Persistence;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Web.Services;

/// <summary>
/// Enhets-scoping för den inloggade användaren. En chef ska bara se sin egen
/// enhet (inte hela organisationen); HR och Admin ser allt. Anställd ser bara
/// sig själv. Härleder enheten via EmployeeId → aktiv Employment.EnhetId och
/// exponerar hjälpmetoder som chefsvyerna filtrerar sina DbContext-queries mot.
/// </summary>
public sealed class UnitScopeService
{
    private readonly AuthService _auth;
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;

    public UnitScopeService(AuthService auth, IDbContextFactory<RegionHRDbContext> dbFactory)
    {
        _auth = auth;
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Ska den inloggades data begränsas till en enhet? Sant endast för Chef.
    /// Admin/HR ser hela organisationen.
    /// </summary>
    public bool IsUnitScoped => _auth.Role == RouteAccessPolicy.Chef;

    /// <summary>
    /// Enhets-id (EnhetId) för den inloggades aktiva anställning, eller null.
    /// Använder i första hand det cachade <see cref="AuthService.UnitId"/> och
    /// faller tillbaka på en DB-slagning via EmployeeId → aktiv anställning.
    /// </summary>
    public async Task<Guid?> GetCurrentUnitIdAsync()
    {
        if (_auth.UnitId.HasValue)
            return _auth.UnitId;

        if (!_auth.EmployeeId.HasValue)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var idag = DateOnly.FromDateTime(DateTime.Today);
        var employee = await db.Employees
            .Include(e => e.Anstallningar)
            .FirstOrDefaultAsync(e => e.Id.Value == _auth.EmployeeId.Value);
        return employee?.AktivAnstallning(idag)?.EnhetId.Value;
    }

    /// <summary>
    /// Mängden anställd-id (Guid) som den inloggade får se.
    /// Semantik (entydig och fail-closed):
    /// <list type="bullet">
    ///   <item><c>null</c> = ingen scoping ska ske (Admin/HR) → anropande vy visar alla.</item>
    ///   <item>en icke-null mängd = exakt de anställda den inloggade får se. Mängden kan
    ///   vara <b>tom</b> — då ska vyn visa INGET (aldrig "alla").</item>
    /// </list>
    /// En chef vars enhet inte kan härledas får en TOM mängd (fail-closed): hellre
    /// visa inget än att läcka hela organisationen (kritiskt vid ~11 000 anställda).
    /// Frågan körs på SQL-sidan och laddar aldrig hela Employee-tabellen i minnet.
    /// </summary>
    public async Task<HashSet<Guid>?> GetEmployeeIdsInScopeAsync()
    {
        if (!IsUnitScoped)
            return null; // Admin/HR: ingen scoping.

        var unitId = await GetCurrentUnitIdAsync();
        if (unitId is null)
            return new HashSet<Guid>(); // Fail-closed: chef utan härledbar enhet ser inget.

        await using var db = await _dbFactory.CreateDbContextAsync();
        var idag = DateOnly.FromDateTime(DateTime.Today);
        var oid = OrganizationId.From(unitId.Value);

        // Hämta bara id för anställda med en aktiv anställning på chefens enhet.
        // Where-villkoret översätts till SQL (samma mönster som AnstallningService),
        // så vi materialiserar aldrig hela personaltabellen.
        var ids = await db.Employees
            .Where(e => e.Anstallningar.Any(a =>
                a.EnhetId == oid &&
                a.Giltighetsperiod.Start <= idag &&
                (a.Giltighetsperiod.End == null || a.Giltighetsperiod.End >= idag)))
            .Select(e => e.Id)
            .ToListAsync();

        return ids.Select(id => id.Value).ToHashSet();
    }

    /// <summary>
    /// Får den inloggade se en specifik anställd? Admin/HR = alla; Chef = egen enhet;
    /// Anställd = bara sig själv.
    /// </summary>
    public async Task<bool> CanViewEmployeeAsync(Guid employeeId)
    {
        if (_auth.Role is RouteAccessPolicy.HR or RouteAccessPolicy.Admin)
            return true;

        if (_auth.Role == RouteAccessPolicy.Chef)
        {
            var scope = await GetEmployeeIdsInScopeAsync();
            return scope is null || scope.Contains(employeeId);
        }

        // Anställd (eller okänd) → bara sig själv.
        return _auth.EmployeeId.HasValue && _auth.EmployeeId.Value == employeeId;
    }
}
