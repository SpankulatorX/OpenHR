namespace RegionHR.Reporting.Domain;

public enum ReportType
{
    Personalrostter,
    Loneregister,
    Franvarostatistik,
    Overtidsrapport,
    LASStatus,
    Bemanningsanalys,
    SjukfranvaroKPI,
    KostnadPerEnhet,
    AdHoc
}

public class ReportDefinition
{
    public Guid Id { get; private set; }
    public string Namn { get; private set; } = default!;
    public string Beskrivning { get; private set; } = default!;
    public ReportType Typ { get; private set; }
    public string? ParameterSchema { get; private set; }
    public bool ArSchemalagd { get; private set; }
    public string? CronExpression { get; private set; }
    public string? MottagareEpost { get; private set; }

    // Report template / self-service builder extensions (Phase B1)
    public string? Datakalla { get; private set; } // Logiskt datakällenamn (Anstallda/Lonekorngar/...)
    public string? Kolumner { get; private set; } // JSON: column definitions
    public string? Filter { get; private set; } // JSON: filter definitions
    public string? Gruppering { get; private set; }
    public string? VisualiseringsTyp { get; private set; } // Table/Bar/Line/Pie

    private ReportDefinition() { }

    public static ReportDefinition Skapa(string namn, string beskrivning, ReportType typ)
    {
        return new ReportDefinition
        {
            Id = Guid.NewGuid(),
            Namn = namn,
            Beskrivning = beskrivning,
            Typ = typ,
            ArSchemalagd = false
        };
    }

    public void SattSchemalagd(string cronExpression, string mottagareEpost)
    {
        CronExpression = cronExpression;
        MottagareEpost = mottagareEpost;
        ArSchemalagd = true;
    }

    public void SattRapportmall(string? datakalla, string? kolumner, string? filter, string? gruppering, string? visualiseringsTyp)
    {
        Datakalla = datakalla;
        Kolumner = kolumner;
        Filter = filter;
        Gruppering = gruppering;
        VisualiseringsTyp = visualiseringsTyp;
    }
}
