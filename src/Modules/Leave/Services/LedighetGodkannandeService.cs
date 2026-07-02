using RegionHR.Leave.Domain;

namespace RegionHR.Leave.Services;

/// <summary>
/// Resultat av ett ledighetsgodkännande.
/// </summary>
/// <param name="SemesterdagarDragna">
/// Antal semesterdagar som drogs från semestersaldot, eller null om ledigheten inte var semester.
/// </param>
/// <param name="BerordaPass">Antal schemapass som markerades som frånvaro.</param>
public sealed record LedighetGodkannandeResultat(int? SemesterdagarDragna, int BerordaPass);

/// <summary>
/// Domäntjänst som utför ett ledighetsgodkännande konsekvent enligt svensk praxis.
/// Systemet är experten och binder ihop de tre effekter som annars glöms bort:
///
///   1. Statusövergång Inskickad -> Godkänd (via <see cref="LeaveRequest.Godkann"/>).
///   2. Semestersaldo dras — men <b>endast</b> för semester. VAB, föräldraledighet,
///      sjukfrånvaro, tjänstledighet m.fl. påverkar aldrig semestersaldot.
///   3. Överlappande, påverkbara schemapass markeras som frånvaro så att lönekörningen
///      (som läser godkänd frånvaro separat) och schemavyn stämmer och inte dubbelräknar.
///
/// Notifiering av den anställde och databaspersistens hanteras av det anropande webblagret
/// (LedighetService), eftersom de korsar modulgränser.
/// </summary>
public sealed class LedighetGodkannandeService
{
    /// <summary>
    /// Godkänner en ledighetsansökan och applicerar saldo- och schemakonsekvenser.
    /// </summary>
    /// <param name="request">Ansökan som ska godkännas (måste ha status Inskickad).</param>
    /// <param name="godkannare">Id för den som godkänner.</param>
    /// <param name="kommentar">Valfri kommentar.</param>
    /// <param name="semestersaldo">
    /// Semestersaldot för perioden. Krävs (icke-null) endast när ledigheten är semester;
    /// ignoreras för övriga frånvarotyper.
    /// </param>
    /// <param name="schemapass">Schemapass i eller kring perioden som ska prövas för avbokning.</param>
    public LedighetGodkannandeResultat Godkann(
        LeaveRequest request,
        Guid godkannare,
        string? kommentar,
        VacationBalance? semestersaldo,
        IReadOnlyList<IPaverkbartPass> schemapass)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(schemapass);

        // 1. Statusövergång — kastar om ansökan inte är Inskickad. Görs först så att
        //    saldo/schema aldrig påverkas för en ansökan som inte får godkännas.
        request.Godkann(godkannare, kommentar);

        // 2. Endast semester drar från semestersaldot.
        int? dragna = null;
        if (request.Typ == LeaveType.Semester)
        {
            if (semestersaldo is null)
                throw new InvalidOperationException(
                    "Semesteransökan kan inte godkännas utan ett semestersaldo för perioden.");

            // Kastar om saldot inte räcker — godkännandet avbryts då i sin helhet.
            semestersaldo.RegistreraUttag(request.AntalDagar);
            dragna = request.AntalDagar;
        }

        // 3. Markera överlappande, påverkbara pass som frånvaro.
        var berorda = 0;
        foreach (var pass in schemapass)
        {
            if (pass.KanPaverkas
                && pass.Datum >= request.FranDatum
                && pass.Datum <= request.TillDatum)
            {
                pass.MarkeraSomFranvaro();
                berorda++;
            }
        }

        return new LedighetGodkannandeResultat(dragna, berorda);
    }
}
