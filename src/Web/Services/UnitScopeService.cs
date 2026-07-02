using Microsoft.EntityFrameworkCore;
using RegionHR.Infrastructure.Persistence;

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
    /// Returnerar <c>null</c> när ingen scoping ska ske (Admin/HR, eller en chef
    /// utan enhetskoppling) — anropande vy visar då alla. För en chef med känd
    /// enhet returneras de anställda vars aktiva anställning ligger på den enheten.
    /// </summary>
    public async Task<HashSet<Guid>?> GetEmployeeIdsInScopeAsync()
    {
        if (!IsUnitScoped)
            return null;

        var unitId = await GetCurrentUnitIdAsync();
        if (unitId is null)
            return null; // Chef utan enhetskoppling → behåll nuvarande beteende (visa alla).

        await using var db = await _dbFactory.CreateDbContextAsync();
        var idag = DateOnly.FromDateTime(DateTime.Today);
        var employees = await db.Employees.Include(e => e.Anstallningar).ToListAsync();

        return employees
            .Where(e => e.AktivAnstallning(idag)?.EnhetId.Value == unitId.Value)
            .Select(e => e.Id.Value)
            .ToHashSet();
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
