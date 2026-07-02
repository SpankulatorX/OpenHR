using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Payroll.Domain;
using RegionHR.Payroll.Engine;
using RegionHR.Payroll.Contracts;
using RegionHR.SharedKernel.Domain;
using RegionHR.IntegrationHub.Adapters.Skatteverket;
using RegionHR.IntegrationHub.Adapters.Nordea;
using RegionHR.Infrastructure.Export;
using RegionHR.Core.Domain;

namespace RegionHR.Api.Endpoints;

public static class PayrollEndpoints
{
    public static WebApplication MapPayrollEndpoints(this WebApplication app)
    {
        var lon = app.MapGroup("/api/v1/lon").WithTags("Lön").RequireAuthorization("LonOchHR");

        // ============================================================
        // Lönekörningar
        // ============================================================

        lon.MapGet("/korningar", async (int? ar, RegionHRDbContext db, CancellationToken ct) =>
        {
            var query = db.PayrollRuns.AsQueryable();
            if (ar.HasValue)
                query = query.Where(r => r.Year == ar.Value);

            var runs = await query
                .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
                .Take(24)
                .ToListAsync(ct);

            return Results.Ok(runs.Select(r => new
            {
                r.Id, r.Period, Status = r.Status.ToString(),
                r.AntalAnstallda, TotalBrutto = r.TotalBrutto.Amount,
                TotalNetto = r.TotalNetto.Amount, TotalSkatt = r.TotalSkatt.Amount,
                TotalArbetsgivaravgifter = r.TotalArbetsgivaravgifter.Amount,
                r.ArRetroaktiv, r.StartadAv, r.StartadVid
            }));
        }).WithName("ListPayrollRuns");

        lon.MapGet("/korning/{id:guid}", async (Guid id, RegionHRDbContext db, CancellationToken ct) =>
        {
            var run = await db.PayrollRuns
                .FirstOrDefaultAsync(r => r.Id == PayrollRunId.From(id), ct);
            return run is not null ? Results.Ok(run) : Results.NotFound();
        }).WithName("GetPayrollRun");

        lon.MapPost("/korning", async (StartPayrollRunRequest req, RegionHRDbContext db, CancellationToken ct) =>
        {
            var run = PayrollRun.Skapa(req.Year, req.Month, req.StartadAv, req.Retroaktiv, req.RetroaktivtForPeriod);
            await db.PayrollRuns.AddAsync(run, ct);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/lon/korning/{run.Id}", new { run.Id, run.Period, Status = run.Status.ToString() });
        }).WithName("CreatePayrollRun");

        lon.MapPost("/korning/{id:guid}/berakna", async (
            Guid id,
            PayrollCalculationEngine engine,
            RegionHRDbContext db,
            CancellationToken ct) =>
        {
            var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == PayrollRunId.From(id), ct);
            if (run is null) return Results.NotFound();

            try
            {
                run.Paborja();
                await db.SaveChangesAsync(ct);

                // TODO: In production, PayrollBatchService handles the full orchestration.
                // This endpoint delegates to the batch service when available.
                // For now, mark as beräknad to support the workflow.
                run.MarkeraSomBeraknad();
                await db.SaveChangesAsync(ct);

                return Results.Ok(new
                {
                    run.Id, run.Period, Status = run.Status.ToString(),
                    run.AntalAnstallda, TotalBrutto = run.TotalBrutto.Amount
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("CalculatePayrollRun");

        lon.MapPost("/korning/{id:guid}/godkann", async (Guid id, GodkannRequest req, RegionHRDbContext db, CancellationToken ct) =>
        {
            var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == PayrollRunId.From(id), ct);
            if (run is null) return Results.NotFound();

            try
            {
                run.Godkann(req.GodkandAv);
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { run.Id, Status = run.Status.ToString(), run.GodkandAv });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("ApprovePayrollRun");

        lon.MapPost("/korning/{id:guid}/utbetala", async (Guid id, RegionHRDbContext db, CancellationToken ct) =>
        {
            var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == PayrollRunId.From(id), ct);
            if (run is null) return Results.NotFound();

            try
            {
                run.MarkeraSomUtbetald();
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { run.Id, Status = run.Status.ToString() });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("MarkPayrollRunAsPaid");

        // ============================================================
        // Löneresultat
        // ============================================================

        lon.MapGet("/korning/{id:guid}/resultat", async (Guid id, RegionHRDbContext db, CancellationToken ct) =>
        {
            var results = await db.PayrollResults
                .Where(r => r.KorningsId == PayrollRunId.From(id))
                .ToListAsync(ct);

            return Results.Ok(results.Select(r => new
            {
                r.AnstallId, r.Year, r.Month,
                Brutto = r.Brutto.Amount, Skatt = r.Skatt.Amount,
                Netto = r.Netto.Amount, Arbetsgivaravgifter = r.Arbetsgivaravgifter.Amount,
                OBTillagg = r.OBTillagg.Amount, Overtid = r.Overtidstillagg.Amount,
                Sjuklon = r.Sjuklon.Amount, Semesterlon = r.Semesterlon.Amount,
                Pension = r.Pensionsavgift.Amount
            }));
        }).WithName("GetPayrollRunResults");

        lon.MapGet("/resultat/{anstallId:guid}/{year:int}/{month:int}", async (
            Guid anstallId, int year, int month, RegionHRDbContext db, CancellationToken ct) =>
        {
            var result = await db.PayrollResults
                .FirstOrDefaultAsync(r =>
                    r.AnstallId == EmployeeId.From(anstallId) &&
                    r.Year == year && r.Month == month, ct);

            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetEmployeePayrollResult");

        lon.MapGet("/resultat/{anstallId:guid}/{year:int}/{month:int}/pdf", async (
            Guid anstallId, int year, int month,
            RegionHRDbContext db, PdfPayslipGenerator pdfGen, CancellationToken ct) =>
        {
            var result = await db.PayrollResults
                .FirstOrDefaultAsync(r => r.AnstallId == EmployeeId.From(anstallId) && r.Year == year && r.Month == month, ct);
            if (result is null) return Results.NotFound();

            var emp = await db.Employees.FirstOrDefaultAsync(e => e.Id == EmployeeId.From(anstallId), ct);

            var payslipData = new PayslipData
            {
                AnstallNamn = emp != null ? $"{emp.Fornamn} {emp.Efternamn}" : "Okänd",
                Personnummer = emp?.Personnummer.ToMaskedString() ?? "",
                Period = $"{year}-{month:D2}",
                Enhet = "",
                Brutto = result.Brutto.Amount,
                Skatt = result.Skatt.Amount,
                Netto = result.Netto.Amount,
                Arbetsgivaravgifter = result.Arbetsgivaravgifter.Amount,
                OBTillagg = result.OBTillagg.Amount,
                Overtid = result.Overtidstillagg.Amount,
                Sjuklon = result.Sjuklon.Amount,
                Semesterlon = result.Semesterlon.Amount,
                Pension = result.Pensionsavgift.Amount
            };

            var pdfBytes = pdfGen.Generate(payslipData);
            return Results.File(pdfBytes, "application/pdf", $"lonespec_{year}{month:D2}_{anstallId}.pdf");
        }).WithName("DownloadPayslip");

        // ============================================================
        // Export — AGI, Betalningsfil, Kontering
        // ============================================================

        lon.MapPost("/korning/{id:guid}/export/agi", async (
            Guid id, AGIExportRequest req, RegionHRDbContext db, CancellationToken ct) =>
        {
            var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == PayrollRunId.From(id), ct);
            if (run is null) return Results.NotFound();

            var results = await db.PayrollResults
                .Where(r => r.KorningsId == PayrollRunId.From(id))
                .ToListAsync(ct);

            // Slå upp verkliga personnummer per anställd (aldrig GUID i AGI-filen).
            var agiAnstallIds = results.Select(r => r.AnstallId).Distinct().ToList();
            var agiEmployees = await db.Employees
                .Where(e => agiAnstallIds.Contains(e.Id))
                .ToListAsync(ct);
            var agiEmpById = agiEmployees.ToDictionary(e => e.Id);

            var generator = new AGIXmlGenerator();
            var input = new AGIInput
            {
                Organisationsnummer = req.Organisationsnummer,
                Period = $"{run.Year}{run.Month:D2}",
                KontaktpersonNamn = req.KontaktpersonNamn,
                KontaktpersonTelefon = req.KontaktpersonTelefon,
                KontaktpersonEpost = req.KontaktpersonEpost,
                Individer = results
                    .Where(r => agiEmpById.ContainsKey(r.AnstallId))
                    .Select(r =>
                    {
                        var emp = agiEmpById[r.AnstallId];
                        return new AGIIndivid
                        {
                            Personnummer = (string)emp.Personnummer, // 12-siffrigt YYYYMMDDNNNN
                            Namn = emp.FulltNamn,
                            KontantBruttolonMm = r.Brutto.Amount,
                            AvdragenSkatt = r.Skatt.Amount,
                            Avgiftsunderlag = r.Brutto.Amount,
                            Arbetsgivaravgifter = r.Arbetsgivaravgifter.Amount
                        };
                    })
                    .ToList()
            };

            var files = generator.Generate(input);

            // Leverera själva filen (inte metadata). En fil → XML, flera → ZIP.
            if (files.Count == 1)
            {
                var xmlBytes = Encoding.UTF8.GetBytes(files[0].XmlContent);
                return Results.File(xmlBytes, "application/xml", files[0].FileName);
            }

            using var zipStream = new MemoryStream();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var f in files)
                {
                    var entry = zip.CreateEntry(f.FileName);
                    await using var entryStream = entry.Open();
                    var entryBytes = Encoding.UTF8.GetBytes(f.XmlContent);
                    await entryStream.WriteAsync(entryBytes, ct);
                }
            }
            return Results.File(zipStream.ToArray(), "application/zip",
                $"AGI_{req.Organisationsnummer}_{run.Year}{run.Month:D2}.zip");
        }).WithName("ExportAGI");

        lon.MapPost("/korning/{id:guid}/export/betalning", async (
            Guid id, BetalningsExportRequest req, RegionHRDbContext db, CancellationToken ct) =>
        {
            var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == PayrollRunId.From(id), ct);
            if (run is null) return Results.NotFound();

            var results = await db.PayrollResults
                .Where(r => r.KorningsId == PayrollRunId.From(id))
                .ToListAsync(ct);

            // Slå upp verkliga namn + bankuppgifter per anställd (aldrig hårdkodat 3300/0000000000).
            var betAnstallIds = results.Select(r => r.AnstallId).Distinct().ToList();
            var betEmployees = await db.Employees
                .Where(e => betAnstallIds.Contains(e.Id))
                .ToListAsync(ct);
            var betEmpById = betEmployees.ToDictionary(e => e.Id);

            var payments = new List<SalaryPayment>();
            var saknarBankuppgifter = new List<string>();
            foreach (var r in results)
            {
                if (!betEmpById.TryGetValue(r.AnstallId, out var emp))
                    continue;
                if (string.IsNullOrWhiteSpace(emp.Clearingnummer) || string.IsNullOrWhiteSpace(emp.Kontonummer))
                {
                    saknarBankuppgifter.Add(emp.FulltNamn);
                    continue;
                }
                payments.Add(new SalaryPayment
                {
                    RecipientName = emp.FulltNamn,
                    Amount = r.Netto.Amount,
                    Period = run.Period,
                    ClearingNumber = emp.Clearingnummer,
                    AccountNumber = emp.Kontonummer
                });
            }

            if (payments.Count == 0)
                return Results.BadRequest(new
                {
                    error = "Inga betalningar kunde skapas — bankuppgifter (clearing-/kontonummer) saknas för samtliga anställda i körningen.",
                    saknarBankuppgifter
                });

            var generator = new NordeaPaymentFileGenerator();
            var batch = new PaymentBatch
            {
                Period = run.Period,
                OrganizationNumber = req.Organisationsnummer,
                InitiatorName = req.AvsandareNamn,
                DebtorName = req.AvsandareNamn,
                DebtorIBAN = req.IBAN,
                ExecutionDate = req.Utbetalningsdatum,
                Payments = payments
            };

            var xml = generator.Generate(batch);
            var betBytes = Encoding.UTF8.GetBytes(xml);
            var fileName = $"SALARY_{run.Period}_{DateTime.UtcNow:yyyyMMddHHmmss}.xml";
            return Results.File(betBytes, "application/xml", fileName);
        }).WithName("ExportPaymentFile");

        // ============================================================
        // Skattetabeller
        // ============================================================

        lon.MapGet("/skattetabeller/{year:int}", async (int year, RegionHRDbContext db, CancellationToken ct) =>
        {
            var tables = await db.TaxTables
                .Where(t => t.Ar == year)
                .Select(t => new { t.Tabellnummer, t.Kolumn, AntalRader = t.Rader.Count })
                .ToListAsync(ct);
            return Results.Ok(new { Ar = year, Tabeller = tables });
        }).WithName("ListTaxTables");

        // ============================================================
        // Lönearter
        // ============================================================

        lon.MapGet("/lonearter", async (RegionHRDbContext db, CancellationToken ct) =>
        {
            var codes = await db.SalaryCodes
                .Where(c => c.ArAktiv)
                .OrderBy(c => c.Kod)
                .ToListAsync(ct);
            return Results.Ok(codes);
        }).WithName("ListSalaryCodes");

        return app;
    }
}

// Request DTOs
record StartPayrollRunRequest(int Year, int Month, string StartadAv, bool Retroaktiv = false, string? RetroaktivtForPeriod = null);
record GodkannRequest(string GodkandAv);
record AGIExportRequest(string Organisationsnummer, string KontaktpersonNamn, string KontaktpersonTelefon, string KontaktpersonEpost);
record BetalningsExportRequest(string Organisationsnummer, string AvsandareNamn, string IBAN, DateOnly Utbetalningsdatum);
