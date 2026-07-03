using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RegionHR.Payroll.Domain;

namespace RegionHR.Infrastructure.Export;

/// <summary>
/// Genererar en fullständig lönespecifikation som PDF via QuestPDF direkt ur ett
/// <see cref="PayrollResult"/> (löneresultatet per anställd och period). Till skillnad från
/// <see cref="PdfGenerator.GenerateLonespecifikation"/> — som bara tar sammanfattade totaler —
/// tar den här generatorn med de detaljerade löneraderna (<see cref="PayrollResultLine"/>),
/// alla tillägg, avdrag, arbetsgivarens kostnader och semestersaldo.
///
/// Klassen är tillståndslös och kan instansieras direkt med <c>new</c>; QuestPDF-licensen
/// sätts i den statiska konstruktorn.
/// </summary>
public class LonespecifikationPdfGenerator
{
    public const string ArbetsgivareNamn = PdfGenerator.ArbetsgivareNamn;
    public const string ArbetsgivareOrgnr = PdfGenerator.ArbetsgivareOrgnr;
    public const string ArbetsgivareAdress = PdfGenerator.ArbetsgivareAdress;

    static LonespecifikationPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(LonespecifikationDokument data)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2.0f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(header =>
                {
                    header.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("LÖNESPECIFIKATION").FontSize(18).Bold().FontColor(Colors.Teal.Darken3);
                            c.Item().Text($"{ArbetsgivareNamn} (org.nr {ArbetsgivareOrgnr}) — OpenHR")
                                .FontSize(9).FontColor(Colors.Grey.Darken1);
                        });
                        row.ConstantItem(150).AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text(data.Period).FontSize(12).SemiBold();
                            c.Item().AlignRight().Text($"Utbetalas {data.Utbetalningsdag}")
                                .FontSize(9).FontColor(Colors.Grey.Darken1);
                        });
                    });
                    header.Item().PaddingTop(6).LineHorizontal(1.2f).LineColor(Colors.Teal.Darken3);
                });

                page.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(6);

                    // ---- Anställningsuppgifter ----
                    UppgiftsTabell(col,
                        ("Namn", data.Namn),
                        ("Personnummer", data.Personnummer),
                        ("Befattning", string.IsNullOrWhiteSpace(data.Befattning) ? "—" : data.Befattning),
                        ("Sysselsättningsgrad", $"{data.Sysselsattningsgrad:0.#} %"),
                        ("Kollektivavtal", string.IsNullOrWhiteSpace(data.Kollektivavtal) ? "—" : data.Kollektivavtal),
                        ("Period", data.Period));

                    // ---- Detaljerade lönerader ----
                    if (data.Rader.Count > 0)
                    {
                        Sektionsrubrik(col, "Specifikation");
                        RadTabell(col, data.Rader);
                    }

                    // ---- Sammanställning: inkomster ----
                    Sektionsrubrik(col, "Inkomster");
                    var inkomster = new List<(string, decimal, bool)>
                    {
                        ("Grundlön", data.Grundlon, false)
                    };
                    AddOm(inkomster, "OB-tillägg", data.OBTillagg);
                    AddOm(inkomster, "Övertidstillägg", data.Overtidstillagg);
                    AddOm(inkomster, "Jourtillägg", data.JourTillagg);
                    AddOm(inkomster, "Beredskapstillägg", data.BeredskapsTillagg);
                    AddOm(inkomster, "Sjuklön", data.Sjuklon);
                    AddOm(inkomster, "Semesterlön", data.Semesterlon);
                    AddOm(inkomster, "Semestertillägg", data.Semestertillagg);
                    AddOm(inkomster, "Föräldralön (utfyllnad)", data.ForaldraloneUtfyllnad);
                    inkomster.Add(("Bruttolön", data.Brutto, true));
                    BeloppTabell(col, inkomster.ToArray());

                    // ---- Sammanställning: avdrag ----
                    Sektionsrubrik(col, "Avdrag");
                    var avdrag = new List<(string, decimal, bool)>
                    {
                        ("Preliminärskatt", -data.Skatt, false)
                    };
                    AddOm(avdrag, "Karensavdrag", -data.Karensavdrag);
                    AddOm(avdrag, "Löneutmätning (KFM)", -data.Loneutmatning);
                    AddOm(avdrag, "Fackavgift", -data.Fackavgift);
                    AddOm(avdrag, "Övriga avdrag", -data.OvrigaAvdrag);
                    avdrag.Add(("Nettolön (utbetalas)", data.Netto, true));
                    BeloppTabell(col, avdrag.ToArray());

                    // ---- Arbetsgivarens kostnader ----
                    Sektionsrubrik(col, "Arbetsgivarens kostnader");
                    BeloppTabell(col,
                        ("Arbetsgivaravgifter", data.Arbetsgivaravgifter, false),
                        ("Pensionsavsättning (AKAP-KR)", data.Pensionsavgift, false));

                    // ---- Semester ----
                    if (data.SemesterdagarIntjanade > 0 || data.SemesterdagarUttagna > 0)
                    {
                        Sektionsrubrik(col, "Semester");
                        UppgiftsTabell(col,
                            ("Intjänade dagar (perioden)", data.SemesterdagarIntjanade.ToString()),
                            ("Uttagna dagar (perioden)", data.SemesterdagarUttagna.ToString()));
                    }

                    col.Item().PaddingTop(10).Text(
                            "Lönespecifikationen är en sammanställning av periodens löneutbetalning. " +
                            "Kontrollera uppgifterna och kontakta HR/löneenheten vid frågor.")
                        .FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                    t.Span($"{ArbetsgivareNamn} — genererad {DateTime.Today:yyyy-MM-dd} i OpenHR — sida ");
                    t.CurrentPageNumber();
                    t.Span(" av ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void AddOm(List<(string, decimal, bool)> lista, string text, decimal belopp)
    {
        if (belopp != 0m) lista.Add((text, belopp, false));
    }

    // ---------- Interna byggstenar ----------

    private static void Sektionsrubrik(ColumnDescriptor col, string rubrik)
    {
        col.Item().PaddingTop(8).Text(rubrik).FontSize(11).SemiBold().FontColor(Colors.Teal.Darken3);
        col.Item().LineHorizontal(0.6f).LineColor(Colors.Grey.Lighten2);
    }

    private static void UppgiftsTabell(ColumnDescriptor col, params (string Rubrik, string Varde)[] rader)
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(180);
                c.RelativeColumn();
            });

            foreach (var (rubrik, varde) in rader)
            {
                table.Cell().PaddingVertical(2).Text(rubrik).SemiBold();
                table.Cell().PaddingVertical(2).Text(varde);
            }
        });
    }

    private static void BeloppTabell(ColumnDescriptor col, params (string Text, decimal Belopp, bool ArSumma)[] rader)
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn();
                c.ConstantColumn(130);
            });

            foreach (var (text, belopp, arSumma) in rader)
            {
                if (arSumma)
                {
                    table.Cell().BorderTop(0.8f).PaddingVertical(3).Text(text.ToUpperInvariant()).SemiBold();
                    table.Cell().BorderTop(0.8f).PaddingVertical(3).AlignRight().Text(Kr(belopp)).SemiBold();
                }
                else
                {
                    table.Cell().PaddingVertical(2).Text(text);
                    table.Cell().PaddingVertical(2).AlignRight().Text(Kr(belopp));
                }
            }
        });
    }

    private static void RadTabell(ColumnDescriptor col, IReadOnlyList<LonespecRad> rader)
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(55);   // Löneart
                c.RelativeColumn();     // Benämning
                c.ConstantColumn(55);   // Antal
                c.ConstantColumn(75);   // Á-pris
                c.ConstantColumn(85);   // Belopp
            });

            table.Header(header =>
            {
                header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(2)
                    .Text("Löneart").SemiBold().FontSize(9);
                header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(2)
                    .Text("Benämning").SemiBold().FontSize(9);
                header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(2)
                    .AlignRight().Text("Antal").SemiBold().FontSize(9);
                header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(2)
                    .AlignRight().Text("Á-pris").SemiBold().FontSize(9);
                header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(2)
                    .AlignRight().Text("Belopp").SemiBold().FontSize(9);
            });

            foreach (var rad in rader)
            {
                table.Cell().PaddingVertical(2).Text(rad.Kod);
                table.Cell().PaddingVertical(2).Text(rad.Benamning);
                table.Cell().PaddingVertical(2).AlignRight().Text(rad.Antal.ToString("0.##"));
                table.Cell().PaddingVertical(2).AlignRight().Text(Kr(rad.Sats));
                table.Cell().PaddingVertical(2).AlignRight()
                    .Text(Kr(rad.ArAvdrag ? -Math.Abs(rad.Belopp) : rad.Belopp));
            }
        });
    }

    private static string Kr(decimal belopp) => $"{belopp:N0} kr";
}

/// <summary>
/// Fullständigt underlag för en lönespecifikation. Byggs ur ett <see cref="PayrollResult"/>
/// via <see cref="FromPayrollResult"/> och renderas av <see cref="LonespecifikationPdfGenerator"/>.
/// </summary>
public sealed record LonespecifikationDokument
{
    public string Namn { get; init; } = "";
    public string Personnummer { get; init; } = "";
    public string Befattning { get; init; } = "";
    public string Period { get; init; } = "";
    public string Utbetalningsdag { get; init; } = "";
    public decimal Sysselsattningsgrad { get; init; }
    public string Kollektivavtal { get; init; } = "";

    // Inkomster
    public decimal Grundlon { get; init; }
    public decimal Brutto { get; init; }
    public decimal OBTillagg { get; init; }
    public decimal Overtidstillagg { get; init; }
    public decimal JourTillagg { get; init; }
    public decimal BeredskapsTillagg { get; init; }
    public decimal Sjuklon { get; init; }
    public decimal Semesterlon { get; init; }
    public decimal Semestertillagg { get; init; }
    public decimal ForaldraloneUtfyllnad { get; init; }

    // Avdrag
    public decimal Skatt { get; init; }
    public decimal Karensavdrag { get; init; }
    public decimal Loneutmatning { get; init; }
    public decimal Fackavgift { get; init; }
    public decimal OvrigaAvdrag { get; init; }
    public decimal Netto { get; init; }

    // Arbetsgivarens kostnader
    public decimal Arbetsgivaravgifter { get; init; }
    public decimal Pensionsavgift { get; init; }

    // Semester
    public int SemesterdagarIntjanade { get; init; }
    public int SemesterdagarUttagna { get; init; }

    public IReadOnlyList<LonespecRad> Rader { get; init; } = [];

    /// <summary>
    /// Bygger ett lönespec-underlag ur ett löneresultat plus den anställdes identitetsuppgifter.
    /// Identitetsuppgifterna skickas in som strängar för att hålla generatorn frikopplad från
    /// Core.Employee (och därmed lätt att enhetstesta).
    /// </summary>
    public static LonespecifikationDokument FromPayrollResult(
        PayrollResult r, string namn, string personnummer, string befattning = "")
    {
        return new LonespecifikationDokument
        {
            Namn = namn,
            Personnummer = personnummer,
            Befattning = befattning,
            Period = FormatPeriod(r.Year, r.Month),
            Utbetalningsdag = new DateOnly(r.Year, r.Month, Math.Min(25, DateTime.DaysInMonth(r.Year, r.Month)))
                .ToString("yyyy-MM-dd"),
            Sysselsattningsgrad = r.Sysselsattningsgrad,
            Kollektivavtal = r.Kollektivavtal.ToString(),

            Grundlon = r.Manadslon.Amount,
            Brutto = r.Brutto.Amount,
            OBTillagg = r.OBTillagg.Amount,
            Overtidstillagg = r.Overtidstillagg.Amount,
            JourTillagg = r.JourTillagg.Amount,
            BeredskapsTillagg = r.BeredskapsTillagg.Amount,
            Sjuklon = r.Sjuklon.Amount,
            Semesterlon = r.Semesterlon.Amount,
            Semestertillagg = r.Semestertillagg.Amount,
            ForaldraloneUtfyllnad = r.ForaldraloneUtfyllnad.Amount,

            Skatt = r.Skatt.Amount,
            Karensavdrag = r.Karensavdrag.Amount,
            Loneutmatning = r.Loneutmatning.Amount,
            Fackavgift = r.Fackavgift.Amount,
            OvrigaAvdrag = r.OvrigaAvdrag.Amount,
            Netto = r.Netto.Amount,

            Arbetsgivaravgifter = r.Arbetsgivaravgifter.Amount,
            Pensionsavgift = r.Pensionsavgift.Amount,

            SemesterdagarIntjanade = r.SemesterdagarIntjanade,
            SemesterdagarUttagna = r.SemesterdagarUttagna,

            Rader = r.Rader
                .OrderBy(l => l.LoneartKod)
                .Select(l => new LonespecRad(
                    l.LoneartKod, l.Benamning, l.Antal, l.Sats.Amount, l.Belopp.Amount, l.ArAvdrag))
                .ToList()
        };
    }

    private static string FormatPeriod(int year, int month)
    {
        var monthNames = new[] { "", "Januari", "Februari", "Mars", "April", "Maj", "Juni",
            "Juli", "Augusti", "September", "Oktober", "November", "December" };
        var namn = month >= 1 && month <= 12 ? monthNames[month] : month.ToString();
        return $"{namn} {year}";
    }
}

public sealed record LonespecRad(
    string Kod, string Benamning, decimal Antal, decimal Sats, decimal Belopp, bool ArAvdrag);
