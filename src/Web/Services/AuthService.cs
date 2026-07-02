using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace RegionHR.Web.Services;

public class AuthService
{
    private readonly ProtectedSessionStorage _storage;

    public string? UserName { get; private set; }
    public string? Role { get; private set; }
    /// <summary>
    /// EmployeeId för den inloggade användaren. Null om inloggad som Admin
    /// (ingen Employee-koppling) eller om ingen matchning hittades.
    /// Mappas via exakt namnmatchning mot Employee-tabellen vid login.
    /// Detta är en demo-auth-begränsning — inte en riktig identitetslösning.
    /// </summary>
    public Guid? EmployeeId { get; private set; }
    /// <summary>
    /// Organisationsenhet (EnhetId) för den inloggade användarens aktiva anställning.
    /// Null för Admin eller anställda utan aktiv enhetskoppling. Används för
    /// enhets-scoping (chef ser bara sin egen enhet) samt "unit_id"-claim.
    /// </summary>
    public Guid? UnitId { get; private set; }
    public bool IsLoggedIn => UserName != null;
    public bool IsInitialized { get; private set; }
    public bool IsDarkMode { get; private set; }

    /// <summary>
    /// Signaleras när den lagrade identiteten ändras (init, login, logout).
    /// <see cref="OpenHrAuthStateProvider"/> lyssnar och speglar om till en
    /// ClaimsPrincipal via NotifyAuthenticationStateChanged.
    /// </summary>
    public event Action? AuthChanged;

    public AuthService(ProtectedSessionStorage storage)
    {
        _storage = storage;
    }

    public async Task InitializeAsync()
    {
        if (IsInitialized) return;
        try
        {
            var nameResult = await _storage.GetAsync<string>("auth_user");
            var roleResult = await _storage.GetAsync<string>("auth_role");
            var empIdResult = await _storage.GetAsync<string>("auth_employee_id");
            var unitIdResult = await _storage.GetAsync<string>("auth_unit_id");
            var darkResult = await _storage.GetAsync<bool>("dark_mode");
            if (nameResult.Success) UserName = nameResult.Value;
            if (roleResult.Success) Role = roleResult.Value;
            if (empIdResult.Success && Guid.TryParse(empIdResult.Value, out var empId)) EmployeeId = empId;
            if (unitIdResult.Success && Guid.TryParse(unitIdResult.Value, out var unitId)) UnitId = unitId;
            if (darkResult.Success) IsDarkMode = darkResult.Value;
        }
        catch { /* First load, no stored values */ }
        IsInitialized = true;
        AuthChanged?.Invoke();
    }

    public async Task LoginAsync(string userName, string role, Guid? employeeId = null, Guid? unitId = null)
    {
        UserName = userName;
        Role = role;
        EmployeeId = employeeId;
        UnitId = unitId;
        await _storage.SetAsync("auth_user", userName);
        await _storage.SetAsync("auth_role", role);
        if (employeeId.HasValue)
            await _storage.SetAsync("auth_employee_id", employeeId.Value.ToString());
        else
            await _storage.DeleteAsync("auth_employee_id");
        if (unitId.HasValue)
            await _storage.SetAsync("auth_unit_id", unitId.Value.ToString());
        else
            await _storage.DeleteAsync("auth_unit_id");
        AuthChanged?.Invoke();
    }

    public async Task LogoutAsync()
    {
        UserName = null;
        Role = null;
        EmployeeId = null;
        UnitId = null;
        await _storage.DeleteAsync("auth_user");
        await _storage.DeleteAsync("auth_role");
        await _storage.DeleteAsync("auth_employee_id");
        await _storage.DeleteAsync("auth_unit_id");
        AuthChanged?.Invoke();
    }

    public async Task SetDarkModeAsync(bool value)
    {
        IsDarkMode = value;
        await _storage.SetAsync("dark_mode", value);
    }

    public bool HasRole(params string[] roles) => Role != null && roles.Contains(Role);
    public bool IsAdmin => Role == "Admin";
    public bool IsHR => Role is "HR" or "Admin";
    public bool IsChef => Role is "Chef" or "HR" or "Admin";
}
