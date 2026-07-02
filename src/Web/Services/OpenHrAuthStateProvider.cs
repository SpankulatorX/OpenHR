using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace RegionHR.Web.Services;

/// <summary>
/// Speglar <see cref="AuthService"/>:s lagrade identitet (persisterad i
/// ProtectedSessionStorage) till en <see cref="ClaimsPrincipal"/> så att Blazors
/// auth-infrastruktur (CascadingAuthenticationState, AuthorizeRouteView, AuthorizeView,
/// [Authorize]) fungerar mot demo-inloggningen — utan att kräva riktig e-legitimation.
///
/// Claims: Name = UserName, Role = Role, "employee_id" = EmployeeId, "unit_id" = UnitId.
/// När AuthService signalerar <see cref="AuthService.AuthChanged"/> (init/login/logout)
/// notifieras Blazor via <see cref="AuthenticationStateProvider.NotifyAuthenticationStateChanged"/>.
/// </summary>
public sealed class OpenHrAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private const string AuthenticationType = "OpenHR";

    private readonly AuthService _auth;

    public OpenHrAuthStateProvider(AuthService auth)
    {
        _auth = auth;
        _auth.AuthChanged += OnAuthChanged;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
        => Task.FromResult(new AuthenticationState(BuildPrincipal()));

    private void OnAuthChanged()
        => NotifyAuthenticationStateChanged(
            Task.FromResult(new AuthenticationState(BuildPrincipal())));

    private ClaimsPrincipal BuildPrincipal()
    {
        // Oinloggad (eller ännu ej initierad) → anonym principal.
        var userName = _auth.UserName;
        if (!_auth.IsLoggedIn || string.IsNullOrEmpty(userName))
            return new ClaimsPrincipal(new ClaimsIdentity());

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userName),
        };

        var role = _auth.Role;
        if (!string.IsNullOrEmpty(role))
            claims.Add(new Claim(ClaimTypes.Role, role));

        if (_auth.EmployeeId.HasValue)
        {
            var empId = _auth.EmployeeId.Value.ToString();
            claims.Add(new Claim("employee_id", empId));
            // Kompatibilitet: befintlig sida (MinSida/TotalRewards) läser "EmployeeId".
            claims.Add(new Claim("EmployeeId", empId));
        }

        if (_auth.UnitId.HasValue)
            claims.Add(new Claim("unit_id", _auth.UnitId.Value.ToString()));

        // AuthenticationType != null ⇒ IsAuthenticated == true.
        var identity = new ClaimsIdentity(claims, AuthenticationType, ClaimTypes.Name, ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    public void Dispose() => _auth.AuthChanged -= OnAuthChanged;
}
