using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RegionHR.Core.Contracts;

namespace RegionHR.Infrastructure.Export;

/// <summary>
/// Genererar riktiga PDF-dokument (lönespecifikation, tjänstgöringsintyg, anställningsavtal)
/// via QuestPDF (Community-licens). Anställningsavtalet byggs på den lagstadgade
/// 6 c §-informationen från <see cref="AnstallningsavtalGenerator"/>.
/// </summary>
public class PdfGenerator
{
    public const string ArbetsgivareNamn = "Region Örebro län";
    public const string ArbetsgivareOrgnr = "232100-0164";
    public const string ArbetsgivareAdress = "Box 1613, 701 16 Örebro";

    static PdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GenerateLonespecifikation(LonespecData data)
    {
        return SkapaDokument("LÖNESPECIFIKATION", col =>
        {
            UppgiftsTabell(col,
                ("Namn", data.Namn),
                ("Personnummer", data.Personnummer),
                ("Period", data.Period),
                ("Utbetalningsdag", data.Utbetalningsdag));

            Sektionsrubrik(col, "Inkomster");
            BeloppTabell(col,
                ("Grundlön", data.Grundlon, false),
                ("OB-tillägg", data.OBTillagg, false),
                ("Övertid", data.Overtid, false),
                ("Brutto", data.Brutto, true));

            Sektionsrubrik(col, "Avdrag");
            BeloppTabell(col,
                ("Kommunalskatt", -data.KommunalSkatt, false),
                ("Statlig skatt", -data.StatligSkatt, false),
                ("Netto", data.Netto, true));

            Sektionsrubrik(col, "Arbetsgivarens kostnader");
            BeloppTabell(col,
                ("Arbetsgivaravgift", data.Arbetsgivaravgift, false));
        });
    }

    public byte[] GenerateTjanstgoringsintyg(TjanstgoringsintyData data)
    {
        return SkapaDokument("TJÄNSTGÖRINGSINTYG", col =>
        {
            col.Item().Text(
                $"Härmed intygas att {data.Namn} ({data.Personnummer}) " +
                $"har varit anställd hos {data.Arbetsgivare} " +
                $"under perioden {data.StartDatum} — {data.SlutDatum}.");

            Sektionsrubrik(col, "Anställningsuppgifter");
            UppgiftsTabell(col,
                ("Befattning", data.Befattning),
                ("Anställningsform", data.Anstallningsform),
                ("Sysselsättningsgrad", data.Sysselsattningsgrad));

            col.Item().PaddingTop(14).Text($"Utfärdat: {DateTime.Today:yyyy-MM-dd}");
            col.Item().Text("Av: HR-avdelningen");

            Underskrifter(col, "Arbetsgivare");
        });
    }

    /// <summary>
    /// Genererar anställningsavtal med den kompletta skriftliga informationen enligt
    /// LAS 6 c § (semester, arbetstid, övertid, uppsägningstid, social trygghet m.m.)
    /// via <see cref="AnstallningsavtalGenerator.Skapa6cInformation"/>.
    /// </summary>
    public byte[] GenerateAnstallningsavtal(AnstallningsavtalUppgifter uppgifter)
    {
        var avsnitt = AnstallningsavtalGenerator.Skapa6cInformation(uppgifter);

        return SkapaDokument("ANSTÄLLNINGSAVTAL", col =>
        {
            col.Item().Text(
                    "Skriftlig information om anställningsvillkor enligt 6 c § lagen (1982:80) " +
                    "om anställningsskydd (LAS).")
                .FontSize(9).Italic().FontColor(Colors.Grey.Darken1);

            var nr = 1;
            foreach (var a in avsnitt)
            {
                Sektionsrubrik(col, $"{nr}. {a.Rubrik}");
                col.Item().Text(a.Text);
                nr++;
            }

            Sektionsrubrik(col, $"{nr}. Underskrifter");
            col.Item().Text($"Datum: {DateTime.Today:yyyy-MM-dd}");
            Underskrifter(col, "Arbetsgivare", "Arbetstagare");
        });
    }

    /// <summary>
    /// Generisk PDF för enklare mallar (t.ex. bekräftelse på löneändring):
    /// renderar den färdiga malltexten som ett dokument med standardhuvud/-fot.
    /// </summary>
    public byte[] GenerateEnkeltDokument(string titel, string text)
    {
        return SkapaDokument(titel.ToUpperInvariant(), col =>
        {
            foreach (var stycke in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                col.Item().Text(stycke.Trim());
            }
        });
    }

    // ---------- Interna byggstenar ----------

    private static byte[] SkapaDokument(string titel, Action<ColumnDescriptor> innehall)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2.2f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(header =>
                {
                    header.Item().Text(titel).FontSize(18).Bold().FontColor(Colors.Teal.Darken3);
                    header.Item().Text($"{ArbetsgivareNamn} (org.nr {ArbetsgivareOrgnr}) — OpenHR")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                    header.Item().PaddingTop(6).LineHorizontal(1.2f).LineColor(Colors.Teal.Darken3);
                });

                page.Content().PaddingVertical(14).Column(col =>
                {
                    col.Spacing(6);
                    innehall(col);
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
                c.ConstantColumn(170);
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
                c.ConstantColumn(120);
            });

            foreach (var (text, belopp, arSumma) in rader)
            {
                if (arSumma)
                {
                    table.Cell().BorderTop(0.8f).PaddingVertical(2).Text(text.ToUpperInvariant()).SemiBold();
                    table.Cell().BorderTop(0.8f).PaddingVertical(2).AlignRight().Text($"{belopp:N0} kr").SemiBold();
                }
                else
                {
                    table.Cell().PaddingVertical(2).Text(text);
                    table.Cell().PaddingVertical(2).AlignRight().Text($"{belopp:N0} kr");
                }
            }
        });
    }

    private static void Underskrifter(ColumnDescriptor col, params string[] parter)
    {
        col.Item().PaddingTop(34).Row(row =>
        {
            foreach (var part in parter)
            {
                row.RelativeItem().PaddingRight(30).Column(c =>
                {
                    c.Item().LineHorizontal(0.8f);
                    c.Item().PaddingTop(4).Text(part).FontSize(9);
                });
            }
        });
    }
}

public record LonespecData(string Namn, string Personnummer, string Period, string Utbetalningsdag, decimal Grundlon, decimal OBTillagg, decimal Overtid, decimal Brutto, decimal KommunalSkatt, decimal StatligSkatt, decimal Netto, decimal Arbetsgivaravgift);
public record TjanstgoringsintyData(string Namn, string Personnummer, string Arbetsgivare, string StartDatum, string SlutDatum, string Befattning, string Anstallningsform, string Sysselsattningsgrad);
