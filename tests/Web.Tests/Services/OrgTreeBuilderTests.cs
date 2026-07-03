using RegionHR.SharedKernel.Domain;
using RegionHR.Web.Services;

namespace RegionHR.Web.Tests.Services;

/// <summary>
/// Enhetstest för det rena in-minne-trädbygget (<see cref="AnstallningService.ByggHierarki"/>)
/// som ersatte den gamla ".Include(Underenheter).Where(root)"-frågan (den laddade bara roten
/// + en nivå barn). Bygget måste koppla ALLA nivåer och aldrig tappa en enhet.
/// </summary>
public class OrgTreeBuilderTests
{
    private static OrgTreeNode Nod(Guid id, string namn, Guid? parent, int antalDirekt = 0) =>
        new(OrganizationId.From(id), namn, OrganizationUnitType.Enhet, "10", parent, antalDirekt);

    private static int RaknaNaabara(IEnumerable<OrgTreeNode> rotter)
    {
        var summa = 0;
        foreach (var n in rotter)
            summa += 1 + RaknaNaabara(n.Barn);
        return summa;
    }

    [Fact]
    public void ByggHierarki_KopplarAllaFyraNivaer()
    {
        var region = Guid.NewGuid();
        var forvaltning = Guid.NewGuid();
        var klinik = Guid.NewGuid();
        var underenhet = Guid.NewGuid();

        var platta = new[]
        {
            Nod(region, "Region Örebro län", null),
            Nod(forvaltning, "Universitetssjukhuset", region),
            Nod(klinik, "Kardiologkliniken", forvaltning),
            Nod(underenhet, "Avdelning 32", klinik),
        };

        var rotter = AnstallningService.ByggHierarki(platta);

        // Exakt EN rot, och hela kedjan region → förvaltning → klinik → underenhet är nåbar.
        var rot = Assert.Single(rotter);
        Assert.Equal("Region Örebro län", rot.Namn);

        var f = Assert.Single(rot.Barn);
        Assert.Equal("Universitetssjukhuset", f.Namn);

        var k = Assert.Single(f.Barn);
        Assert.Equal("Kardiologkliniken", k.Namn);

        var u = Assert.Single(k.Barn);
        Assert.Equal("Avdelning 32", u.Namn);

        // Ingen enhet tappas: alla 4 är nåbara från roten.
        Assert.Equal(4, RaknaNaabara(rotter));
    }

    [Fact]
    public void ByggHierarki_BehandlarDinglandeForalderSomRot_SaIngenTappas()
    {
        var saknadForalder = Guid.NewGuid(); // finns INTE i urvalet
        var barn = Guid.NewGuid();

        var platta = new[]
        {
            Nod(barn, "Föräldralös enhet", saknadForalder),
        };

        var rotter = AnstallningService.ByggHierarki(platta);

        var rot = Assert.Single(rotter);
        Assert.Equal("Föräldralös enhet", rot.Namn);
        Assert.Equal(1, RaknaNaabara(rotter));
    }

    [Fact]
    public void ByggHierarki_StoderFleraRotter()
    {
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();
        var barnTillR1 = Guid.NewGuid();

        var platta = new[]
        {
            Nod(r1, "Rot A", null),
            Nod(r2, "Rot B", null),
            Nod(barnTillR1, "Barn under A", r1),
        };

        var rotter = AnstallningService.ByggHierarki(platta);

        Assert.Equal(2, rotter.Count);
        Assert.Equal(3, RaknaNaabara(rotter));
        var rotA = Assert.Single(rotter, x => x.Namn == "Rot A");
        Assert.Single(rotA.Barn);
        Assert.Empty(Assert.Single(rotter, x => x.Namn == "Rot B").Barn);
    }

    [Fact]
    public void ByggHierarki_RullarUppAntalAnstalldaOverSubtradet()
    {
        var region = Guid.NewGuid();
        var forvaltning = Guid.NewGuid();
        var klinikA = Guid.NewGuid();
        var klinikB = Guid.NewGuid();

        var platta = new[]
        {
            Nod(region, "Region", null, antalDirekt: 0),
            Nod(forvaltning, "Förvaltning", region, antalDirekt: 2),
            Nod(klinikA, "Klinik A", forvaltning, antalDirekt: 5),
            Nod(klinikB, "Klinik B", forvaltning, antalDirekt: 3),
        };

        var rotter = AnstallningService.ByggHierarki(platta);
        var rot = Assert.Single(rotter);

        // Totalt = 0 + 2 + 5 + 3 = 10 i hela subträdet; direkt i roten = 0.
        Assert.Equal(10, rot.AntalAnstalldaTotalt);
        Assert.Equal(0, rot.AntalAnstalldaDirekt);

        var f = Assert.Single(rot.Barn);
        Assert.Equal(10, f.AntalAnstalldaTotalt); // 2 + 5 + 3
        Assert.Equal(2, f.AntalAnstalldaDirekt);
        Assert.Equal(2, f.AntalUnderenheter);

        var kA = Assert.Single(f.Barn, x => x.Namn == "Klinik A");
        Assert.Equal(5, kA.AntalAnstalldaTotalt); // löv → totalt == direkt
        Assert.Equal(5, kA.AntalAnstalldaDirekt);
    }

    [Fact]
    public void ByggHierarki_IgnorererSjalvreferens_UtanAttTappaNoden()
    {
        var id = Guid.NewGuid();
        // En enhet som (felaktigt) pekar på sig själv får inte bli sitt eget barn eller försvinna.
        var platta = new[] { Nod(id, "Självreferens", id) };

        var rotter = AnstallningService.ByggHierarki(platta);

        var rot = Assert.Single(rotter);
        Assert.Empty(rot.Barn);
        Assert.Equal(1, RaknaNaabara(rotter));
    }
}
