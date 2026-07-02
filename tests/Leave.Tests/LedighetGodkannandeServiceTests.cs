using RegionHR.Leave.Domain;
using RegionHR.Leave.Services;
using Xunit;

namespace RegionHR.Leave.Tests;

/// <summary>
/// Tester för godkännandeflödet: saldodragning (endast semester) och schemapåverkan.
/// Täcker regressionen i revisionen: "godkänd semester drar aldrig saldo, påverkar inte schema".
/// </summary>
public class LedighetGodkannandeServiceTests
{
    private readonly Guid _anstall = Guid.NewGuid();
    private readonly Guid _godkannare = Guid.NewGuid();
    private readonly LedighetGodkannandeService _sut = new();

    // Måndag 2026-03-16 – fredag 2026-03-20 = 5 arbetsdagar.
    private static readonly DateOnly Fran = new(2026, 3, 16);
    private static readonly DateOnly Till = new(2026, 3, 20);

    private LeaveRequest InskickadAnsokan(LeaveType typ, DateOnly? fran = null, DateOnly? till = null)
    {
        var r = LeaveRequest.Skapa(_anstall, typ, fran ?? Fran, till ?? Till, null);
        r.SkickaIn();
        return r;
    }

    private sealed class FakePass : IPaverkbartPass
    {
        public DateOnly Datum { get; init; }
        public bool KanPaverkas { get; init; } = true;
        public bool Markerad { get; private set; }
        public void MarkeraSomFranvaro() => Markerad = true;
    }

    [Fact]
    public void Godkann_Semester_DrarSemestersaldo()
    {
        var ansokan = InskickadAnsokan(LeaveType.Semester);
        var saldo = VacationBalance.SkapaForAr(_anstall, 2026, 35); // 25 dagars tilldelning

        var resultat = _sut.Godkann(ansokan, _godkannare, null, saldo, []);

        Assert.Equal(5, saldo.UttagnaDagar);
        Assert.Equal(20, saldo.TillgangligaDagar);
        Assert.Equal(5, resultat.SemesterdagarDragna);
        Assert.Equal(LeaveRequestStatus.Godkand, ansokan.Status);
    }

    [Fact]
    public void Godkann_VAB_DrarAldrigSemestersaldo()
    {
        var ansokan = InskickadAnsokan(LeaveType.VAB);
        var saldo = VacationBalance.SkapaForAr(_anstall, 2026, 35);

        var resultat = _sut.Godkann(ansokan, _godkannare, null, saldo, []);

        Assert.Equal(0, saldo.UttagnaDagar);
        Assert.Equal(25, saldo.TillgangligaDagar);
        Assert.Null(resultat.SemesterdagarDragna);
        Assert.Equal(LeaveRequestStatus.Godkand, ansokan.Status);
    }

    [Fact]
    public void Godkann_Foraldraledighet_DrarAldrigSemestersaldo()
    {
        var ansokan = InskickadAnsokan(LeaveType.Foraldraledighet);
        var saldo = VacationBalance.SkapaForAr(_anstall, 2026, 35);

        var resultat = _sut.Godkann(ansokan, _godkannare, null, saldo, []);

        Assert.Equal(0, saldo.UttagnaDagar);
        Assert.Null(resultat.SemesterdagarDragna);
    }

    [Fact]
    public void Godkann_Semester_UtanSaldo_Kastar()
    {
        var ansokan = InskickadAnsokan(LeaveType.Semester);

        Assert.Throws<InvalidOperationException>(
            () => _sut.Godkann(ansokan, _godkannare, null, null, []));
    }

    [Fact]
    public void Godkann_Semester_OtillrackligtSaldo_Kastar()
    {
        var ansokan = InskickadAnsokan(LeaveType.Semester); // 5 dagar
        var saldo = VacationBalance.SkapaForAr(_anstall, 2026, 35); // 25 dagar
        saldo.RegistreraUttag(23); // 2 dagar kvar

        Assert.Throws<InvalidOperationException>(
            () => _sut.Godkann(ansokan, _godkannare, null, saldo, []));

        // Saldot får inte ha ändrats av det misslyckade uttaget.
        Assert.Equal(23, saldo.UttagnaDagar);
    }

    [Fact]
    public void Godkann_MarkerarOverlappandePassSomFranvaro()
    {
        var ansokan = InskickadAnsokan(LeaveType.VAB);
        var inom1 = new FakePass { Datum = new DateOnly(2026, 3, 16) }; // första dagen
        var inom2 = new FakePass { Datum = new DateOnly(2026, 3, 18) }; // mitt i
        var pass = new IPaverkbartPass[] { inom1, inom2 };

        var resultat = _sut.Godkann(ansokan, _godkannare, null, null, pass);

        Assert.True(inom1.Markerad);
        Assert.True(inom2.Markerad);
        Assert.Equal(2, resultat.BerordaPass);
    }

    [Fact]
    public void Godkann_LamnarPassUtanforPeriodenOrort()
    {
        var ansokan = InskickadAnsokan(LeaveType.Semester);
        var saldo = VacationBalance.SkapaForAr(_anstall, 2026, 35);
        var fore = new FakePass { Datum = new DateOnly(2026, 3, 13) }; // fredagen innan
        var efter = new FakePass { Datum = new DateOnly(2026, 3, 23) }; // måndagen efter
        var pass = new IPaverkbartPass[] { fore, efter };

        var resultat = _sut.Godkann(ansokan, _godkannare, null, saldo, pass);

        Assert.False(fore.Markerad);
        Assert.False(efter.Markerad);
        Assert.Equal(0, resultat.BerordaPass);
    }

    [Fact]
    public void Godkann_HopparOverPassSomInteKanPaverkas()
    {
        var ansokan = InskickadAnsokan(LeaveType.VAB);
        // Överlappar perioden men är redan avbokat/bytt/avslutat -> ska inte röras.
        var last = new FakePass { Datum = new DateOnly(2026, 3, 17), KanPaverkas = false };
        var pass = new IPaverkbartPass[] { last };

        var resultat = _sut.Godkann(ansokan, _godkannare, null, null, pass);

        Assert.False(last.Markerad);
        Assert.Equal(0, resultat.BerordaPass);
    }

    [Fact]
    public void Godkann_EjInskickad_Kastar()
    {
        // Utkast (aldrig inskickad) kan inte godkännas.
        var ansokan = LeaveRequest.Skapa(_anstall, LeaveType.Semester, Fran, Till, null);
        var saldo = VacationBalance.SkapaForAr(_anstall, 2026, 35);

        Assert.Throws<InvalidOperationException>(
            () => _sut.Godkann(ansokan, _godkannare, null, saldo, []));

        // Statusen får inte ha förbrukat saldo.
        Assert.Equal(0, saldo.UttagnaDagar);
    }

    [Fact]
    public void Godkann_KombineratSemesterOchSchema()
    {
        var ansokan = InskickadAnsokan(LeaveType.Semester);
        var saldo = VacationBalance.SkapaForAr(_anstall, 2026, 41); // 31 dagar
        var pass = new IPaverkbartPass[]
        {
            new FakePass { Datum = new DateOnly(2026, 3, 16) },
            new FakePass { Datum = new DateOnly(2026, 3, 20) },
            new FakePass { Datum = new DateOnly(2026, 3, 27) } // utanför perioden
        };

        var resultat = _sut.Godkann(ansokan, _godkannare, "Beviljad", saldo, pass);

        Assert.Equal(5, resultat.SemesterdagarDragna);
        Assert.Equal(2, resultat.BerordaPass);
        Assert.Equal(26, saldo.TillgangligaDagar); // 31 - 5
        Assert.Equal(LeaveRequestStatus.Godkand, ansokan.Status);
    }
}
