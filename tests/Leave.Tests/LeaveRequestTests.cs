using RegionHR.Leave.Domain;
using Xunit;

namespace RegionHR.Leave.Tests;

public class LeaveRequestTests
{
    private readonly Guid _anstallId = Guid.NewGuid();

    [Fact]
    public void Skapa_BerknarArbetsdagarKorrekt()
    {
        // Mndag 2026-03-16 till fredag 2026-03-20 = 5 arbetsdagar
        var request = LeaveRequest.Skapa(
            _anstallId, LeaveType.Semester,
            new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 20), null);

        Assert.Equal(5, request.AntalDagar);
    }

    [Fact]
    public void Skapa_ExkluderarHelgerFranArbetsdagar()
    {
        // Mndag 2026-03-16 till sndag 2026-03-22 = 5 arbetsdagar (lr+sn exkluderas)
        var request = LeaveRequest.Skapa(
            _anstallId, LeaveType.Semester,
            new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 22), null);

        Assert.Equal(5, request.AntalDagar);
    }

    [Fact]
    public void Skapa_TvVeckorGer10Arbetsdagar()
    {
        // Mndag 2026-03-16 till fredag 2026-03-27 = 10 arbetsdagar
        var request = LeaveRequest.Skapa(
            _anstallId, LeaveType.Semester,
            new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 27), null);

        Assert.Equal(10, request.AntalDagar);
    }

    [Fact]
    public void Skapa_SatterStatusTillUtkast()
    {
        var request = LeaveRequest.Skapa(
            _anstallId, LeaveType.Semester,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 5), null);

        Assert.Equal(LeaveRequestStatus.Utkast, request.Status);
    }

    [Fact]
    public void Skapa_KastarVidOgiltigtDatumintervall()
    {
        Assert.Throws<ArgumentException>(() =>
            LeaveRequest.Skapa(
                _anstallId, LeaveType.Semester,
                new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 1), null));
    }

    [Fact]
    public void SkickaIn_GarFranUtkastTillInskickad()
    {
        var request = LeaveRequest.Skapa(
            _anstallId, LeaveType.Semester,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 5), null);

        request.SkickaIn();

        Assert.Equal(LeaveRequestStatus.Inskickad, request.Status);
    }

    [Fact]
    public void Godkann_GarFranInskickadTillGodkand()
    {
        var request = LeaveRequest.Skapa(
            _anstallId, LeaveType.Semester,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 5), null);
        var godkannare = Guid.NewGuid();

        request.SkickaIn();
        request.Godkann(godkannare, "Godknt");

        Assert.Equal(LeaveRequestStatus.Godkand, request.Status);
        Assert.Equal(godkannare, request.GodkandAv);
        Assert.NotNull(request.GodkandVid);
        Assert.Equal("Godknt", request.Kommentar);
    }

    [Fact]
    public void Avvisa_SatterKommentarOchStatus()
    {
        var request = LeaveRequest.Skapa(
            _anstallId, LeaveType.Sjukfranvaro,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 5), null);
        var godkannare = Guid.NewGuid();

        request.SkickaIn();
        request.Avvisa(godkannare, "Resursbrist under perioden");

        Assert.Equal(LeaveRequestStatus.Avslagen, request.Status);
        Assert.Equal("Resursbrist under perioden", request.Kommentar);
        Assert.Equal(godkannare, request.GodkandAv);
    }

    [Fact]
    public void Avvisa_KastarUtanKommentar()
    {
        var request = LeaveRequest.Skapa(
            _anstallId, LeaveType.Semester,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 5), null);
        var godkannare = Guid.NewGuid();

        request.SkickaIn();

        Assert.Throws<ArgumentException>(() => request.Avvisa(godkannare, ""));
        Assert.Throws<ArgumentException>(() => request.Avvisa(godkannare, " "));
    }

    [Fact]
    public void Aterkalla_FranUtkast()
    {
        var request = LeaveRequest.Skapa(
            _anstallId, LeaveType.Semester,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 5), null);

        request.Aterkalla();

        Assert.Equal(LeaveRequestStatus.Aterkallad, request.Status);
    }

    [Fact]
    public void Aterkalla_FranInskickad()
    {
        var request = LeaveRequest.Skapa(
            _anstallId, LeaveType.Semester,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 5), null);

        request.SkickaIn();
        request.Aterkalla();

        Assert.Equal(LeaveRequestStatus.Aterkallad, request.Status);
    }

    [Fact]
    public void Aterkalla_KastarFranGodkand()
    {
        var request = LeaveRequest.Skapa(
            _anstallId, LeaveType.Semester,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 5), null);
        request.SkickaIn();
        request.Godkann(Guid.NewGuid(), null);

        Assert.Throws<InvalidOperationException>(() => request.Aterkalla());
    }

    [Fact]
    public void Aterkalla_KastarFranAvslagen()
    {
        var request = LeaveRequest.Skapa(
            _anstallId, LeaveType.Semester,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 5), null);
        request.SkickaIn();
        request.Avvisa(Guid.NewGuid(), "Nej");

        Assert.Throws<InvalidOperationException>(() => request.Aterkalla());
    }

    [Fact]
    public void Godkann_KastarFranUtkast()
    {
        var request = LeaveRequest.Skapa(
            _anstallId, LeaveType.Semester,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 5), null);

        Assert.Throws<InvalidOperationException>(() => request.Godkann(Guid.NewGuid(), null));
    }
}

public class SickLeaveNotificationTests
{
    private readonly Guid _anstallId = Guid.NewGuid();

    [Fact]
    public void Skapa_StartarPaDag1()
    {
        var sjuk = SickLeaveNotification.Skapa(_anstallId, new DateOnly(2026, 3, 16));

        Assert.Equal(1, sjuk.SjukDag);
        Assert.False(sjuk.LakarintygKravs);
        Assert.False(sjuk.FKAnmalanKravs);
        Assert.False(sjuk.LakarintygInlamnat);
        Assert.False(sjuk.FKAnmalanGjord);
    }

    [Fact]
    public void UppdateraDag_SatterLakarintygFlaggaDag8()
    {
        var sjuk = SickLeaveNotification.Skapa(_anstallId, new DateOnly(2026, 3, 16));

        sjuk.UppdateraDag(7);
        Assert.False(sjuk.LakarintygKravs);

        sjuk.UppdateraDag(8);
        Assert.True(sjuk.LakarintygKravs);
        Assert.False(sjuk.FKAnmalanKravs);
    }

    [Fact]
    public void UppdateraDag_SatterFKAnmalanFlaggaDag15()
    {
        var sjuk = SickLeaveNotification.Skapa(_anstallId, new DateOnly(2026, 3, 16));

        sjuk.UppdateraDag(14);
        Assert.False(sjuk.FKAnmalanKravs);

        sjuk.UppdateraDag(15);
        Assert.True(sjuk.FKAnmalanKravs);
        Assert.True(sjuk.LakarintygKravs); // Ocks true vid dag 15
    }

    [Fact]
    public void UppdateraDag_KastarVidOgiltigtDagnummer()
    {
        var sjuk = SickLeaveNotification.Skapa(_anstallId, new DateOnly(2026, 3, 16));

        Assert.Throws<ArgumentOutOfRangeException>(() => sjuk.UppdateraDag(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => sjuk.UppdateraDag(-1));
    }

    [Fact]
    public void BeraknaSjukDag_Dag1PaStartdatum()
    {
        var sjuk = SickLeaveNotification.Skapa(_anstallId, new DateOnly(2026, 3, 16));

        Assert.Equal(1, sjuk.BeraknaSjukDag(new DateOnly(2026, 3, 16)));
    }

    [Fact]
    public void BeraknaSjukDag_HarledsUrStartdatum()
    {
        var sjuk = SickLeaveNotification.Skapa(_anstallId, new DateOnly(2026, 3, 16));

        Assert.Equal(8, sjuk.BeraknaSjukDag(new DateOnly(2026, 3, 23)));
        Assert.Equal(15, sjuk.BeraknaSjukDag(new DateOnly(2026, 3, 30)));
    }

    [Fact]
    public void SynkroniseraDag_SatterFlaggorUtifranDagensDatum()
    {
        var sjuk = SickLeaveNotification.Skapa(_anstallId, new DateOnly(2026, 3, 16));

        // Dag 8: lkarintyg krvs, FK nnu inte.
        sjuk.SynkroniseraDag(new DateOnly(2026, 3, 23));
        Assert.Equal(8, sjuk.SjukDag);
        Assert.True(sjuk.LakarintygKravs);
        Assert.False(sjuk.FKAnmalanKravs);

        // Dag 15: bda flaggorna.
        sjuk.SynkroniseraDag(new DateOnly(2026, 3, 30));
        Assert.Equal(15, sjuk.SjukDag);
        Assert.True(sjuk.FKAnmalanKravs);
    }

    [Fact]
    public void Avsluta_SatterSlutDatumOchFryserDagraknignen()
    {
        var sjuk = SickLeaveNotification.Skapa(_anstallId, new DateOnly(2026, 3, 16));

        sjuk.Avsluta(new DateOnly(2026, 3, 20)); // dag 5

        Assert.True(sjuk.ArAvslutad);
        Assert.Equal(new DateOnly(2026, 3, 20), sjuk.SlutDatum);
        Assert.Equal(5, sjuk.SjukDag);
        Assert.False(sjuk.LakarintygKravs);

        // Dagrkningen rknar inte vidare efter avslut.
        Assert.Equal(5, sjuk.BeraknaSjukDag(new DateOnly(2026, 4, 30)));
        sjuk.SynkroniseraDag(new DateOnly(2026, 4, 30));
        Assert.Equal(5, sjuk.SjukDag);
    }

    [Fact]
    public void Avsluta_KastarOmRedanAvslutad()
    {
        var sjuk = SickLeaveNotification.Skapa(_anstallId, new DateOnly(2026, 3, 16));
        sjuk.Avsluta(new DateOnly(2026, 3, 20));

        Assert.Throws<InvalidOperationException>(() => sjuk.Avsluta(new DateOnly(2026, 3, 21)));
    }

    [Fact]
    public void Avsluta_KastarOmSlutForeStart()
    {
        var sjuk = SickLeaveNotification.Skapa(_anstallId, new DateOnly(2026, 3, 16));

        Assert.Throws<ArgumentException>(() => sjuk.Avsluta(new DateOnly(2026, 3, 15)));
    }
}
