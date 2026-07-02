using System.Security.Claims;
using RegionHR.Infrastructure.Persistence;

namespace RegionHR.Web.Services;

/// <summary>
/// HttpContext-baserad <see cref="ICurrentUser"/> för granskningsloggen:
/// läser i första hand den inloggades claims ur <see cref="HttpContext.User"/>
/// (satta av cookie-/OIDC-autentiseringen när Entra är påslaget, se Program.cs).
///
/// Vid demo-inloggning (SITHS/BankID-simulering) bär HTTP-kontexten ingen
/// principal — identiteten lever per Blazor-circuit i <see cref="AuthService"/>
/// (ProtectedSessionStorage). Därför faller vi tillbaka på AuthService så att
/// AuditInterceptor stämplar den FAKTISKA användaren även i demo-läget.
/// Saknas användare i båda källorna returneras null och interceptorn faller
/// tillbaka på "system" (bakgrundsjobb, seeding).
///
/// Registreras scoped tillsammans med AuditInterceptor (se DI-wiring i
/// Program.cs / Infrastructure.DependencyInjection).
/// </summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthService _auth;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor, AuthService auth)
    {
        _httpContextAccessor = httpContextAccessor;
        _auth = auth;
    }

    /// <inheritdoc />
    public string? Id
    {
        get
        {
            var user = AuthenticatedHttpUser();
            if (user is not null)
            {
                // Samma claim-namn som OpenHrAuthStateProvider/EntraClaimsMapper sätter.
                return user.FindFirst("employee_id")?.Value
                    ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? user.FindFirst(ClaimTypes.Name)?.Value;
            }

            // Demo-login: identiteten finns i circuit-sessionen.
            if (_auth.IsLoggedIn)
                return _auth.EmployeeId?.ToString() ?? _auth.UserName;

            return null;
        }
    }

    /// <inheritdoc />
    public string? Namn
    {
        get
        {
            var user = AuthenticatedHttpUser();
            if (user is not null)
                return user.Identity?.Name ?? user.FindFirst(ClaimTypes.Name)?.Value;

            return _auth.IsLoggedIn ? _auth.UserName : null;
        }
    }

    /// <summary>HttpContext-principal om den finns OCH är autentiserad, annars null.</summary>
    private ClaimsPrincipal? AuthenticatedHttpUser()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        return user?.Identity?.IsAuthenticated == true ? user : null;
    }
}
