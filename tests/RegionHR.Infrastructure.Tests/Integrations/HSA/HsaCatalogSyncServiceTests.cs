using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Integrations.HSA;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Infrastructure.Tests.Integrations.HSA;

public class HsaCatalogSyncServiceTests
{
    // Konstrueras utan DbContextFactory eftersom testerna kör den rena entitetssynken
    // (SynkaEntiteterAsync) som inte rör databasen. DB-vägen (SynkaAsync) täcks E2E.
    private readonly HsaCatalogSyncService _service =
        new(dbFactory: null!, adapter: new SandboxHsaCatalogAdapter());

    private static OrganizationUnit NyEnhet(string namn, string kostnadsstalle) =>
        OrganizationUnit.Skapa(namn, OrganizationUnitType.Enhet, kostnadsstalle, new DateOnly(2026, 1, 1));

    private static Employee NyMedarbetare(string pnr, string fornamn) =>
        Employee.Skapa(Personnummer.CreateValidated(pnr), fornamn, "Testsson");

    [Fact]
    public async Task Synka_KopplarHsaIdPaAllaSaknade()
    {
        var enheter = new List<OrganizationUnit>
        {
            NyEnhet("Akutmottagningen", "2011"),
            NyEnhet("Vårdcentralen", "3020")
        };
        var medarbetare = new List<Employee>
        {
            NyMedarbetare("198112289874", "Anna"),
            NyMedarbetare("198503152383", "Björn")
        };

        var result = await _service.SynkaEntiteterAsync(enheter, medarbetare);

        Assert.True(result.Success);
        Assert.True(result.IsSandbox);
        Assert.Equal(2, result.EnheterUppdaterade);
        Assert.Equal(2, result.PersonerUppdaterade);
        Assert.All(enheter, e => Assert.False(string.IsNullOrWhiteSpace(e.HsaId)));
        Assert.All(medarbetare, m => Assert.False(string.IsNullOrWhiteSpace(m.HsaId)));
    }

    [Fact]
    public async Task Synka_ArIdempotent_RorInteRedanSynkade()
    {
        var enheter = new List<OrganizationUnit> { NyEnhet("Akutmottagningen", "2011") };
        var medarbetare = new List<Employee> { NyMedarbetare("197806221211", "Cecilia") };

        var first = await _service.SynkaEntiteterAsync(enheter, medarbetare);
        var hsaIdEfterForsta = enheter[0].HsaId;

        var second = await _service.SynkaEntiteterAsync(enheter, medarbetare);

        Assert.Equal(1, first.EnheterUppdaterade);
        Assert.Equal(0, second.EnheterUppdaterade);
        Assert.Equal(0, second.PersonerUppdaterade);
        // Redan satt id ändras inte vid ny synk.
        Assert.Equal(hsaIdEfterForsta, enheter[0].HsaId);
    }

    [Fact]
    public async Task Synka_TommaListor_LyckasUtanUppdateringar()
    {
        var result = await _service.SynkaEntiteterAsync([], []);

        Assert.True(result.Success);
        Assert.Equal(0, result.EnheterTotalt);
        Assert.Equal(0, result.EnheterUppdaterade);
        Assert.Equal(0, result.PersonerUppdaterade);
    }
}
