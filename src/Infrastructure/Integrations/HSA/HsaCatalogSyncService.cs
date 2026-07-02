using Microsoft.EntityFrameworkCore;
using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Persistence;

namespace RegionHR.Infrastructure.Integrations.HSA;

/// <summary>
/// Kör en katalogsynk mot HSA: kopplar HSA-id på organisatoriska enheter och medarbetare
/// som ännu saknar det. Med <see cref="SandboxHsaCatalogAdapter"/> sker detta mot
/// deterministisk demodata (ingen skarp Inera-anslutning).
/// </summary>
public sealed class HsaCatalogSyncService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;
    private readonly IHsaCatalogAdapter _adapter;

    public HsaCatalogSyncService(IDbContextFactory<RegionHRDbContext> dbFactory, IHsaCatalogAdapter adapter)
    {
        _dbFactory = dbFactory;
        _adapter = adapter;
    }

    /// <summary>Laddar enheter och medarbetare från databasen och synkar HSA-id, sparar sedan.</summary>
    public async Task<HsaSyncResult> SynkaAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var enheter = await db.OrganizationUnits.ToListAsync(ct);
        var medarbetare = await db.Employees.ToListAsync(ct);

        var result = await SynkaEntiteterAsync(enheter, medarbetare, ct);

        if (result.Success && (result.EnheterUppdaterade > 0 || result.PersonerUppdaterade > 0))
            await db.SaveChangesAsync(ct);

        return result;
    }

    /// <summary>
    /// Ren synk-logik som opererar på redan inlästa entiteter (testbar utan databas).
    /// Endast entiteter som saknar HSA-id uppdateras — synk är idempotent.
    /// </summary>
    public async Task<HsaSyncResult> SynkaEntiteterAsync(
        IReadOnlyList<OrganizationUnit> enheter,
        IReadOnlyList<Employee> medarbetare,
        CancellationToken ct = default)
    {
        var meddelanden = new List<string>();

        try
        {
            var status = await _adapter.GetStatusAsync(ct);
            if (!status.IsReachable)
            {
                return new HsaSyncResult(
                    Success: false, IsSandbox: _adapter.IsSandbox,
                    EnheterTotalt: enheter.Count, EnheterUppdaterade: 0,
                    PersonerTotalt: medarbetare.Count, PersonerUppdaterade: 0,
                    Meddelanden: meddelanden,
                    Fel: "HSA-katalogen är inte nåbar.");
            }

            if (_adapter.IsSandbox)
                meddelanden.Add("Demo-synk: HSA-id genererades lokalt (ingen skarp Inera-anslutning).");

            var enheterUppdaterade = 0;
            foreach (var enhet in enheter)
            {
                if (!string.IsNullOrWhiteSpace(enhet.HsaId))
                    continue;

                var sokterm = !string.IsNullOrWhiteSpace(enhet.Kostnadsstalle)
                    ? $"{enhet.Namn} ({enhet.Kostnadsstalle})"
                    : enhet.Namn;

                var traff = await _adapter.SlaUppEnhetAsync(sokterm, ct);
                if (traff is not null)
                {
                    enhet.SattHsaId(traff.HsaId);
                    enheterUppdaterade++;
                }
            }

            var personerUppdaterade = 0;
            foreach (var person in medarbetare)
            {
                if (!string.IsNullOrWhiteSpace(person.HsaId))
                    continue;

                var traff = await _adapter.SlaUppPersonAsync(person.Personnummer.ToString(), ct);
                if (traff is not null)
                {
                    person.SattHsaId(traff.HsaId);
                    personerUppdaterade++;
                }
            }

            meddelanden.Add($"{enheterUppdaterade} av {enheter.Count} enheter fick HSA-id.");
            meddelanden.Add($"{personerUppdaterade} av {medarbetare.Count} medarbetare fick HSA-id.");

            return new HsaSyncResult(
                Success: true, IsSandbox: _adapter.IsSandbox,
                EnheterTotalt: enheter.Count, EnheterUppdaterade: enheterUppdaterade,
                PersonerTotalt: medarbetare.Count, PersonerUppdaterade: personerUppdaterade,
                Meddelanden: meddelanden);
        }
        catch (Exception ex)
        {
            return new HsaSyncResult(
                Success: false, IsSandbox: _adapter.IsSandbox,
                EnheterTotalt: enheter.Count, EnheterUppdaterade: 0,
                PersonerTotalt: medarbetare.Count, PersonerUppdaterade: 0,
                Meddelanden: meddelanden,
                Fel: ex.Message);
        }
    }
}
