using Microsoft.EntityFrameworkCore;
using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Persistence;

namespace RegionHR.Web.Services.Oidc;

/// <summary>
/// Resultatet av att koppla en federerad Entra-identitet till OpenHR:s egna register — allt som
/// behövs för att spegla in identiteten i <see cref="AuthService"/> (och därmed till en
/// ClaimsPrincipal via <see cref="OpenHrAuthStateProvider"/>).
/// </summary>
/// <param name="DisplayName">Visningsnamn som visas i UI:t.</param>
/// <param name="Role">OpenHR-roll (redan mappad av <see cref="EntraClaimsMapper"/>).</param>
/// <param name="EmployeeId">Kopplad Employee, eller null (t.ex. ren admin utan personalpost).</param>
/// <param name="UnitId">Aktiv enhet för self-service-scoping, eller null.</param>
/// <param name="MatchedEmployee">Hur kopplingen gjordes — för loggning/felsökning.</param>
public sealed record ResolvedAccount(
    string DisplayName,
    string Role,
    Guid? EmployeeId,
    Guid? UnitId,
    AccountMatch MatchedEmployee);

/// <summary>Hur en federerad identitet kopplades till en Employee-post.</summary>
public enum AccountMatch
{
    /// <summary>Ingen personalpost hittades (rollen står ändå, t.ex. Admin).</summary>
    None,
    /// <summary>Kopplad via e-post (<c>Employee.Epost</c>).</summary>
    Email,
    /// <summary>Kopplad via exakt för- och efternamn.</summary>
    Name
}

/// <summary>
/// Bryggan mellan en federerad Entra-identitet och OpenHR:s personalregister. Speglar exakt den
/// koppling demo-inloggningen redan gör (namn → Employee → aktiv enhet), men föredrar e-post som
/// nyckel eftersom Entra alltid bär UPN/e-post medan namn kan kollidera.
///
/// <para>
/// Medvetet fri från <see cref="AuthService"/>-beroende: den skriver inte till ProtectedSessionStorage
/// (som kräver JS-interop/en levande krets). Anroparen — completion-sidan — tar
/// <see cref="ResolvedAccount"/> och kallar <see cref="AuthService.LoginAsync"/> inne i den
/// interaktiva kretsen, precis som Login-sidan gör. Det gör klassen helt enhetstestbar.
/// </para>
/// </summary>
public sealed class OidcAccountLinker
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;

    public OidcAccountLinker(IDbContextFactory<RegionHRDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Kopplar en <see cref="EntraIdentity"/> till en OpenHR-Employee och löser aktiv enhet.
    /// Kastar aldrig på grund av DB-problem — faller tillbaka på enbart roll (utan personalkoppling),
    /// exakt som demo-login gör, så att federerad admin utan personalpost ändå kommer in.
    /// </summary>
    /// <param name="unitClaimHint">
    /// Ev. enhets-GUID läst ur en claim (<see cref="OidcOptions.UnitClaimType"/>). Används endast
    /// om personalmatchningen inte själv kunde lösa enhet.
    /// </param>
    public async Task<ResolvedAccount> ResolveAsync(
        EntraIdentity identity,
        Guid? unitClaimHint = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        Guid? employeeId = null;
        Guid? unitId = null;
        var match = AccountMatch.None;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            var employee = await FindByEmailAsync(db, identity.Email, cancellationToken);
            if (employee is not null)
            {
                match = AccountMatch.Email;
            }
            else
            {
                employee = await FindByNameAsync(db, identity.DisplayName, cancellationToken);
                if (employee is not null)
                    match = AccountMatch.Name;
            }

            if (employee is not null)
            {
                employeeId = employee.Id.Value;
                var idag = DateOnly.FromDateTime(DateTime.Today);
                unitId = employee.AktivAnstallning(idag)?.EnhetId.Value;
            }
        }
        catch
        {
            // DB otillgänglig — fortsätt med enbart roll (som demo-login vid DB-fel).
        }

        unitId ??= unitClaimHint;

        return new ResolvedAccount(identity.DisplayName, identity.Role, employeeId, unitId, match);
    }

    private static async Task<Employee?> FindByEmailAsync(
        RegionHRDbContext db, string? email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var normalized = email.Trim();
        // Client-side jämförelse (skiftlägesokänsligt) — Epost är fritext och antalet anställda litet.
        var candidates = await db.Employees
            .Include(e => e.Anstallningar)
            .Where(e => e.Epost != null)
            .ToListAsync(ct);

        return candidates.FirstOrDefault(e =>
            string.Equals(e.Epost, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Employee?> FindByNameAsync(
        RegionHRDbContext db, string displayName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        var parts = displayName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return null;

        var fornamn = parts[0];
        var efternamn = parts[1];
        return await db.Employees
            .Include(e => e.Anstallningar)
            .FirstOrDefaultAsync(e => e.Fornamn == fornamn && e.Efternamn == efternamn, ct);
    }
}
