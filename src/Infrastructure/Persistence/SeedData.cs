using Microsoft.EntityFrameworkCore;
using RegionHR.Core.Domain;
using RegionHR.Agreements.Domain;
using RegionHR.Analytics.Domain;
using RegionHR.Competence.Domain;
using RegionHR.Configuration.Domain;
using RegionHR.HalsoSAM.Domain;
using RegionHR.Infrastructure.Journeys;
using RegionHR.Knowledge.Domain;
using RegionHR.Positions.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Persistence;

public static class SeedData
{
    public static async Task SeedAsync(RegionHRDbContext db)
    {
        if (await db.Employees.AnyAsync()) return; // Already seeded

        // === Organization units ===
        var region = OrganizationUnit.Skapa(
            "Region Örebro län", OrganizationUnitType.Region,
            "10000", DateOnly.FromDateTime(DateTime.Today.AddYears(-20)));

        var sjukhus = OrganizationUnit.Skapa(
            "Universitetssjukhuset Örebro", OrganizationUnitType.Forvaltning,
            "20000", DateOnly.FromDateTime(DateTime.Today.AddYears(-20)),
            overordnadEnhetId: region.Id);

        var avd32 = OrganizationUnit.Skapa(
            "Avdelning 32", OrganizationUnitType.Avdelning,
            "20032", DateOnly.FromDateTime(DateTime.Today.AddYears(-10)),
            overordnadEnhetId: sjukhus.Id);

        var avd33 = OrganizationUnit.Skapa(
            "Avdelning 33", OrganizationUnitType.Avdelning,
            "20033", DateOnly.FromDateTime(DateTime.Today.AddYears(-10)),
            overordnadEnhetId: sjukhus.Id);

        var akuten = OrganizationUnit.Skapa(
            "Akutmottagningen", OrganizationUnitType.Avdelning,
            "20050", DateOnly.FromDateTime(DateTime.Today.AddYears(-15)),
            overordnadEnhetId: sjukhus.Id);

        var iva = OrganizationUnit.Skapa(
            "IVA", OrganizationUnitType.Avdelning,
            "20060", DateOnly.FromDateTime(DateTime.Today.AddYears(-15)),
            overordnadEnhetId: sjukhus.Id);

        db.OrganizationUnits.AddRange(region, sjukhus, avd32, avd33, akuten, iva);

        // === Collective Agreements (10 avtal) ===
        var giltigFran = new DateOnly(2025, 4, 1);

        // 1. AB — Allmanna bestammelser (kommun/region)
        var ab = CollectiveAgreement.Skapa("AB 2025", "SKR och Kommunal", giltigFran, IndustrySector.KommunRegion);
        // AB §21 O-tillägg, kanoniska satser fr.o.m. 2025-04-01 (SKR Allmänna bestämmelser).
        ab.LaggTillOBSats(OBCategory.VardagKvall, 25.60m, giltigFran);
        ab.LaggTillOBSats(OBCategory.VardagNatt, 56.70m, giltigFran);
        ab.LaggTillOBSats(OBCategory.Helg, 66.10m, giltigFran);
        ab.LaggTillOBSats(OBCategory.Storhelg, 126.90m, giltigFran);
        ab.LaggTillOvertidsRegel(1.0m, 1.8m, 200m);
        ab.LaggTillSemesterRegel(25, 31, 32);
        ab.LaggTillPensionsRegel(PensionType.AKAPKR, 6.0m, 31.5m, 599500m, "{\"IBB\":599500,\"TakMultipel\":7.5}");
        ab.LaggTillViloRegel(11m, 36m, 0.5m);
        ab.LaggTillArbetstidsRegel(38.25m, "{\"MaxFlexSaldo\":40}");
        ab.LaggTillUppságningsRegel(0, 1);
        ab.LaggTillUppságningsRegel(24, 2);
        ab.LaggTillUppságningsRegel(72, 3);
        ab.LaggTillForsakringspaket("{\"Belopp\":285000}", "{\"DagErsattning\":810}", "{\"Omfattning\":\"Full\"}", "{\"Typ\":\"AFA\"}", "{\"Omfattning\":\"Standard\"}");
        ab.LaggTillLonestruktur("{\"Underskoterska\":25500,\"Sjukskoterska\":29000,\"Lakare\":42000}", "[]");

        // 2. HOK — Huvudoverenskommelse
        var hok = CollectiveAgreement.Skapa("HOK 2025", "SKR och OFR/S,P,O", giltigFran, IndustrySector.KommunRegion);
        // HÖK följer AB §21 O-tillägg → samma belopp.
        hok.LaggTillOBSats(OBCategory.VardagKvall, 25.60m, giltigFran);
        hok.LaggTillOBSats(OBCategory.VardagNatt, 56.70m, giltigFran);
        hok.LaggTillOBSats(OBCategory.Helg, 66.10m, giltigFran);
        hok.LaggTillOBSats(OBCategory.Storhelg, 126.90m, giltigFran);
        hok.LaggTillOvertidsRegel(1.0m, 1.8m, 200m);
        hok.LaggTillSemesterRegel(25, 31, 32);
        hok.LaggTillPensionsRegel(PensionType.AKAPKR, 6.0m, 31.5m, 599500m, "{\"IBB\":599500,\"TakMultipel\":7.5}");
        hok.LaggTillViloRegel(11m, 36m, 0.5m);
        hok.LaggTillArbetstidsRegel(38.25m, "{\"MaxFlexSaldo\":40}");

        // 3. Teknikavtalet
        var teknik = CollectiveAgreement.Skapa("Teknikavtalet 2025", "Teknikarbetsgivarna och IF Metall", giltigFran, IndustrySector.IndustriTeknik);
        teknik.LaggTillOBSats(OBCategory.VardagKvall, 58.70m, giltigFran);
        teknik.LaggTillOBSats(OBCategory.VardagNatt, 78.50m, giltigFran);
        teknik.LaggTillOBSats(OBCategory.Helg, 44.00m, giltigFran);
        teknik.LaggTillOBSats(OBCategory.Storhelg, 105.00m, giltigFran);
        teknik.LaggTillOvertidsRegel(1.0m, 1.5m, 200m);
        teknik.LaggTillSemesterRegel(25, 27, 28);
        teknik.LaggTillPensionsRegel(PensionType.SAFLO, 4.5m, 30.0m, 599500m, "{\"IBB\":599500,\"TakMultipel\":7.5}");
        teknik.LaggTillViloRegel(11m, 36m, 0.5m);
        teknik.LaggTillArbetstidsRegel(40.0m, "{}");

        // 4. Handelsavtalet
        var handel = CollectiveAgreement.Skapa("Handelsavtalet 2025", "Svensk Handel och Handels", giltigFran, IndustrySector.Handel);
        handel.LaggTillOBSats(OBCategory.VardagKvall, 42.00m, giltigFran);
        handel.LaggTillOBSats(OBCategory.VardagNatt, 72.00m, giltigFran);
        handel.LaggTillOBSats(OBCategory.Helg, 55.00m, giltigFran);
        handel.LaggTillOBSats(OBCategory.Storhelg, 115.00m, giltigFran);
        handel.LaggTillOvertidsRegel(1.0m, 1.5m, 150m);
        handel.LaggTillSemesterRegel(25, 27, 28);
        handel.LaggTillPensionsRegel(PensionType.SAFLO, 4.5m, 30.0m, 599500m, "{\"IBB\":599500}");
        handel.LaggTillViloRegel(11m, 36m, 0.5m);
        handel.LaggTillArbetstidsRegel(40.0m, "{}");

        // 5. IT/Telekomavtalet
        var itTelekom = CollectiveAgreement.Skapa("IT/Telekomavtalet 2025", "Almega IT och Unionen", giltigFran, IndustrySector.ITTelekom);
        itTelekom.LaggTillOBSats(OBCategory.VardagKvall, 35.00m, giltigFran);
        itTelekom.LaggTillOBSats(OBCategory.VardagNatt, 65.00m, giltigFran);
        itTelekom.LaggTillOBSats(OBCategory.Helg, 45.00m, giltigFran);
        itTelekom.LaggTillOBSats(OBCategory.Storhelg, 95.00m, giltigFran);
        itTelekom.LaggTillOvertidsRegel(1.0m, 1.5m, 200m);
        itTelekom.LaggTillSemesterRegel(25, 28, 30);
        itTelekom.LaggTillPensionsRegel(PensionType.ITP1, 4.5m, 30.0m, 599500m, "{\"IBB\":599500}");
        itTelekom.LaggTillViloRegel(11m, 36m, 0.5m);
        itTelekom.LaggTillArbetstidsRegel(40.0m, "{\"MaxFlexSaldo\":80,\"KarnTidStart\":\"10:00\",\"KarnTidSlut\":\"15:00\"}");
        itTelekom.LaggTillPrivatErsattningsPlan("{\"MaxProcent\":15}", "{}", "{\"ESOP\":true}", "{\"MaxBelopp\":8000}");

        // 6. Vardforetagaravtalet
        var vardforetag = CollectiveAgreement.Skapa("Vardforetagaravtalet 2025", "Vardforetagarna och Kommunal", giltigFran, IndustrySector.SjukvardPrivat);
        vardforetag.LaggTillOBSats(OBCategory.VardagKvall, 110.00m, giltigFran);
        vardforetag.LaggTillOBSats(OBCategory.VardagNatt, 135.00m, giltigFran);
        vardforetag.LaggTillOBSats(OBCategory.Helg, 80.00m, giltigFran);
        vardforetag.LaggTillOBSats(OBCategory.Storhelg, 170.00m, giltigFran);
        vardforetag.LaggTillOvertidsRegel(1.0m, 1.7m, 200m);
        vardforetag.LaggTillSemesterRegel(25, 30, 31);
        vardforetag.LaggTillPensionsRegel(PensionType.SAFLO, 4.5m, 30.0m, 599500m, "{\"IBB\":599500}");
        vardforetag.LaggTillViloRegel(11m, 36m, 0.5m);
        vardforetag.LaggTillArbetstidsRegel(38.25m, "{}");

        // 7. Transportavtalet
        var transport = CollectiveAgreement.Skapa("Transportavtalet 2025", "Biltrafikens Arbetsgivareforb. och Transport", giltigFran, IndustrySector.Transport);
        transport.LaggTillOBSats(OBCategory.VardagKvall, 48.00m, giltigFran);
        transport.LaggTillOBSats(OBCategory.VardagNatt, 85.00m, giltigFran);
        transport.LaggTillOBSats(OBCategory.Helg, 60.00m, giltigFran);
        transport.LaggTillOBSats(OBCategory.Storhelg, 125.00m, giltigFran);
        transport.LaggTillOvertidsRegel(1.0m, 1.5m, 200m);
        transport.LaggTillSemesterRegel(25, 27, 28);
        transport.LaggTillPensionsRegel(PensionType.SAFLO, 4.5m, 30.0m, 599500m, "{\"IBB\":599500}");
        transport.LaggTillViloRegel(11m, 36m, 0.75m);
        transport.LaggTillArbetstidsRegel(40.0m, "{}");

        // 8. HRF-avtalet (Hotell och restaurang)
        var hrf = CollectiveAgreement.Skapa("HRF-avtalet 2025", "Visita och HRF", giltigFran, IndustrySector.HotellRestaurang);
        hrf.LaggTillOBSats(OBCategory.VardagKvall, 38.50m, giltigFran);
        hrf.LaggTillOBSats(OBCategory.VardagNatt, 58.00m, giltigFran);
        hrf.LaggTillOBSats(OBCategory.Helg, 48.00m, giltigFran);
        hrf.LaggTillOBSats(OBCategory.Storhelg, 98.00m, giltigFran);
        hrf.LaggTillOvertidsRegel(1.0m, 1.5m, 150m);
        hrf.LaggTillSemesterRegel(25, 26, 27);
        hrf.LaggTillPensionsRegel(PensionType.SAFLO, 4.5m, 30.0m, 599500m, "{\"IBB\":599500}");
        hrf.LaggTillViloRegel(11m, 36m, 0.5m);
        hrf.LaggTillArbetstidsRegel(40.0m, "{}");

        // 9. Tjanstemannaavtalet
        var tjansteman = CollectiveAgreement.Skapa("Tjanstemannaavtalet 2025", "Almega och Unionen", giltigFran, IndustrySector.Tjanstemannaallman);
        tjansteman.LaggTillOBSats(OBCategory.VardagKvall, 32.00m, giltigFran);
        tjansteman.LaggTillOBSats(OBCategory.VardagNatt, 55.00m, giltigFran);
        tjansteman.LaggTillOBSats(OBCategory.Helg, 42.00m, giltigFran);
        tjansteman.LaggTillOBSats(OBCategory.Storhelg, 88.00m, giltigFran);
        tjansteman.LaggTillOvertidsRegel(1.0m, 1.5m, 200m);
        tjansteman.LaggTillSemesterRegel(25, 28, 30);
        tjansteman.LaggTillPensionsRegel(PensionType.ITP1, 4.5m, 30.0m, 599500m, "{\"IBB\":599500}");
        tjansteman.LaggTillViloRegel(11m, 36m, 0.5m);
        tjansteman.LaggTillArbetstidsRegel(40.0m, "{\"MaxFlexSaldo\":40}");

        // 10. Avtalslost — tomt avtal for privat utan kollektivavtal
        var avtalslost = CollectiveAgreement.Skapa("Avtalslost", "Ingen", giltigFran, IndustrySector.Avtalslost);

        db.CollectiveAgreements.AddRange(ab, hok, teknik, handel, itTelekom, vardforetag, transport, hrf, tjansteman, avtalslost);

        // === Employees ===
        var seedEmployees = new (string Fornamn, string Efternamn, string Pnr, string Befattning, OrganizationId Enhet, Money Lon)[]
        {
            ("Anna", "Svensson", "198503152383", "Sjukskoterska", avd32.Id, Money.SEK(34500m)),
            ("Erik", "Johansson", "197806221211", "Lakare", akuten.Id, Money.SEK(62000m)),
            ("Maria", "Lindgren", "199001015604", "Underskoterska", avd33.Id, Money.SEK(27800m)),
            ("Karl", "Berg", "198207143499", "Sjukskoterska", iva.Id, Money.SEK(35200m)),
            ("Sara", "Karlsson", "199504307843", "Underskoterska", avd32.Id, Money.SEK(27500m)),
            ("Johan", "Nilsson", "198802152382", "Lakare", akuten.Id, Money.SEK(58000m)),
            ("Helena", "Bergstrom", "199209184528", "Sjukskoterska", avd33.Id, Money.SEK(33800m)),
            ("Anders", "Olsson", "197503056789", "Verksamhetschef", sjukhus.Id, Money.SEK(52000m)),
            ("Eva", "Nilsson", "198007121303", "HR-chef", sjukhus.Id, Money.SEK(48000m)),
            ("Per", "Andersson", "198705232466", "Underskoterska", iva.Id, Money.SEK(28200m)),
        };

        var employees = new List<Employee>();
        var employments = new List<RegionHR.Core.Domain.Employment>();
        foreach (var (fornamn, efternamn, pnr, befattning, enhetId, lon) in seedEmployees)
        {
            var employee = Employee.Skapa(Personnummer.CreateValidated(pnr), fornamn, efternamn);
            employee.UppdateraKontaktuppgifter(
                $"{fornamn.ToLower()}.{efternamn.ToLower()}@regionorebrolan.se",
                $"070-{Random.Shared.Next(100, 999)} {Random.Shared.Next(10, 99)} {Random.Shared.Next(10, 99)}",
                null);
            // Skatteuppgifter (våg 1/skatt): Örebro → Skatteverkets tabell 34, kolumn 1.
            employee.UppdateraSkatteuppgifter(34, 1, "Örebro", 33.65m, harKyrkoavgift: false, kyrkoavgiftssats: null);
            var startdatum = DateOnly.FromDateTime(DateTime.Today.AddYears(-Random.Shared.Next(1, 15)));
            var employment = employee.LaggTillAnstallning(
                enhetId, EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
                lon, Percentage.FullTime, startdatum);
            employment.SattBefattning(befattning);
            employment.SattKollektivavtal(ab.Id);
            db.Employees.Add(employee);
            employees.Add(employee);
            employments.Add(employment);
        }

        // === Skills (normaliserad katalog) ===
        var hlr = Skill.Skapa("HLR", SkillCategory.Klinisk, "Hjart-lungr\u00e4ddning");
        var triage = Skill.Skapa("Triage", SkillCategory.Klinisk, "Prioritering av patienter");
        var lakemedel = Skill.Skapa("Lakemedelshantering", SkillCategory.Klinisk, "Administration och kontroll av lakemedel");
        var journal = Skill.Skapa("Journalforing", SkillCategory.Klinisk, "Dokumentation i patientjournal");
        var saravard = Skill.Skapa("Saravard", SkillCategory.Klinisk, "Omlaggning och savard");
        var ventilator = Skill.Skapa("Ventilatorvard", SkillCategory.Klinisk, "Hantering av respirator/ventilator");
        var ledarskap = Skill.Skapa("Ledarskap", SkillCategory.Ledarskap, "Arbetsledning och teamledning");
        var projektledning = Skill.Skapa("Projektledning", SkillCategory.Ledarskap, "Planering och genomforande av projekt");
        var excel = Skill.Skapa("Excel/Statistik", SkillCategory.Teknisk, "Dataanalys och rapportering");
        var it = Skill.Skapa("IT-system", SkillCategory.Teknisk, "Hantering av verksamhetssystem");
        var kommunikation = Skill.Skapa("Kommunikation", SkillCategory.Administration, "Muntlig och skriftlig kommunikation");
        var arbetsratt = Skill.Skapa("Arbetsratt", SkillCategory.Administration, "Kunskap om LAS, MBL, AML");

        db.Skills.AddRange(hlr, triage, lakemedel, journal, saravard, ventilator,
            ledarskap, projektledning, excel, it, kommunikation, arbetsratt);

        // === Positions (kopplade till anstallda via InnehavareAnstallId) ===
        var posSsk32 = Position.Skapa(avd32.Id.Value, "Sjukskoterska Avd 32", 34500, 100);
        posSsk32.Tillsatt(employees[0].Id.Value); // Anna Svensson
        var posLakAkut = Position.Skapa(akuten.Id.Value, "Lakare Akutmottagningen", 62000, 100);
        posLakAkut.Tillsatt(employees[1].Id.Value); // Erik Johansson
        var posUsk33 = Position.Skapa(avd33.Id.Value, "Underskoterska Avd 33", 27800, 100);
        posUsk33.Tillsatt(employees[2].Id.Value); // Maria Lindgren
        var posSskIva = Position.Skapa(iva.Id.Value, "Sjukskoterska IVA", 35200, 100);
        posSskIva.Tillsatt(employees[3].Id.Value); // Karl Berg
        var posVc = Position.Skapa(sjukhus.Id.Value, "Verksamhetschef", 52000, 100);
        posVc.Tillsatt(employees[7].Id.Value); // Anders Olsson

        // Vakant position
        var posUskAkut = Position.Skapa(akuten.Id.Value, "Underskoterska Akutmottagningen", 27500, 75);
        // Frusen position
        var posItSjukhus = Position.Skapa(sjukhus.Id.Value, "IT-tekniker", 38000, 100);
        posItSjukhus.Frys();

        db.Positions_Table.AddRange(posSsk32, posLakAkut, posUsk33, posSskIva, posVc, posUskAkut, posItSjukhus);

        // === PositionSkillRequirements (kravprofiler per position) ===
        // Sjukskoterska Avd 32: HLR 3, Lakemedel 4, Journal 3, Saravard 3
        db.PositionSkillRequirements.AddRange(
            PositionSkillRequirement.Skapa(posSsk32.Id, hlr.Id, 3),
            PositionSkillRequirement.Skapa(posSsk32.Id, lakemedel.Id, 4),
            PositionSkillRequirement.Skapa(posSsk32.Id, journal.Id, 3),
            PositionSkillRequirement.Skapa(posSsk32.Id, saravard.Id, 3));

        // Lakare Akutmottagningen: HLR 5, Triage 5, Lakemedel 5, Journal 4
        db.PositionSkillRequirements.AddRange(
            PositionSkillRequirement.Skapa(posLakAkut.Id, hlr.Id, 5),
            PositionSkillRequirement.Skapa(posLakAkut.Id, triage.Id, 5),
            PositionSkillRequirement.Skapa(posLakAkut.Id, lakemedel.Id, 5),
            PositionSkillRequirement.Skapa(posLakAkut.Id, journal.Id, 4));

        // Underskoterska Avd 33: HLR 2, Saravard 3, Journal 2
        db.PositionSkillRequirements.AddRange(
            PositionSkillRequirement.Skapa(posUsk33.Id, hlr.Id, 2),
            PositionSkillRequirement.Skapa(posUsk33.Id, saravard.Id, 3),
            PositionSkillRequirement.Skapa(posUsk33.Id, journal.Id, 2));

        // Sjukskoterska IVA: HLR 5, Ventilator 4, Lakemedel 5, Journal 4
        db.PositionSkillRequirements.AddRange(
            PositionSkillRequirement.Skapa(posSskIva.Id, hlr.Id, 5),
            PositionSkillRequirement.Skapa(posSskIva.Id, ventilator.Id, 4),
            PositionSkillRequirement.Skapa(posSskIva.Id, lakemedel.Id, 5),
            PositionSkillRequirement.Skapa(posSskIva.Id, journal.Id, 4));

        // Verksamhetschef: Ledarskap 5, Kommunikation 4, Arbetsratt 3, Excel 3
        db.PositionSkillRequirements.AddRange(
            PositionSkillRequirement.Skapa(posVc.Id, ledarskap.Id, 5),
            PositionSkillRequirement.Skapa(posVc.Id, kommunikation.Id, 4),
            PositionSkillRequirement.Skapa(posVc.Id, arbetsratt.Id, 3),
            PositionSkillRequirement.Skapa(posVc.Id, excel.Id, 3));

        // === EmployeeSkills (vad de anstallda faktiskt kan) ===
        // Anna Svensson (SSK Avd32): HLR 4, Lakemedel 3, Journal 4, Saravard 4
        db.EmployeeSkills.AddRange(
            EmployeeSkill.Skapa(employees[0].Id.Value, hlr.Id, 4),
            EmployeeSkill.Skapa(employees[0].Id.Value, lakemedel.Id, 3),
            EmployeeSkill.Skapa(employees[0].Id.Value, journal.Id, 4),
            EmployeeSkill.Skapa(employees[0].Id.Value, saravard.Id, 4));

        // Erik Johansson (Lakare Akut): HLR 5, Triage 4, Lakemedel 5, Journal 3
        db.EmployeeSkills.AddRange(
            EmployeeSkill.Skapa(employees[1].Id.Value, hlr.Id, 5),
            EmployeeSkill.Skapa(employees[1].Id.Value, triage.Id, 4),
            EmployeeSkill.Skapa(employees[1].Id.Value, lakemedel.Id, 5),
            EmployeeSkill.Skapa(employees[1].Id.Value, journal.Id, 3));

        // Maria Lindgren (USK Avd33): HLR 2, Saravard 2, Journal 2
        db.EmployeeSkills.AddRange(
            EmployeeSkill.Skapa(employees[2].Id.Value, hlr.Id, 2),
            EmployeeSkill.Skapa(employees[2].Id.Value, saravard.Id, 2),
            EmployeeSkill.Skapa(employees[2].Id.Value, journal.Id, 2));

        // Karl Berg (SSK IVA): HLR 5, Ventilator 3, Lakemedel 4, Journal 3
        db.EmployeeSkills.AddRange(
            EmployeeSkill.Skapa(employees[3].Id.Value, hlr.Id, 5),
            EmployeeSkill.Skapa(employees[3].Id.Value, ventilator.Id, 3),
            EmployeeSkill.Skapa(employees[3].Id.Value, lakemedel.Id, 4),
            EmployeeSkill.Skapa(employees[3].Id.Value, journal.Id, 3));

        // Anders Olsson (VC): Ledarskap 4, Kommunikation 4, Arbetsratt 2, Excel 3
        db.EmployeeSkills.AddRange(
            EmployeeSkill.Skapa(employees[7].Id.Value, ledarskap.Id, 4),
            EmployeeSkill.Skapa(employees[7].Id.Value, kommunikation.Id, 4),
            EmployeeSkill.Skapa(employees[7].Id.Value, arbetsratt.Id, 2),
            EmployeeSkill.Skapa(employees[7].Id.Value, excel.Id, 3));

        // === Skill Categories (entity-baserade, ersätter enum) ===
        var katKlinisk = SkillCategoryEntity.Skapa("Klinisk", "Kliniska vårdkompetenser");
        var katTeknisk = SkillCategoryEntity.Skapa("Teknisk", "Tekniska och IT-kompetenser");
        var katLedarskap = SkillCategoryEntity.Skapa("Ledarskap", "Ledarskaps- och chefskompetenser");
        var katAdmin = SkillCategoryEntity.Skapa("Administration", "Administrativa kompetenser");
        var katKomm = SkillCategoryEntity.Skapa("Kommunikation", "Kommunikations- och samverkanskompetenser");
        var katReg = SkillCategoryEntity.Skapa("Regulatorisk", "Regelverks- och lagstiftningskompetenser");

        db.SkillCategories.AddRange(katKlinisk, katTeknisk, katLedarskap, katAdmin, katKomm, katReg);

        // Koppla befintliga skills till nya kategori-entiteter
        hlr.SattKategoriEntitet(katKlinisk.Id);
        triage.SattKategoriEntitet(katKlinisk.Id);
        lakemedel.SattKategoriEntitet(katKlinisk.Id);
        journal.SattKategoriEntitet(katKlinisk.Id);
        saravard.SattKategoriEntitet(katKlinisk.Id);
        ventilator.SattKategoriEntitet(katKlinisk.Id);
        ledarskap.SattKategoriEntitet(katLedarskap.Id);
        projektledning.SattKategoriEntitet(katLedarskap.Id);
        excel.SattKategoriEntitet(katTeknisk.Id);
        it.SattKategoriEntitet(katTeknisk.Id);
        kommunikation.SattKategoriEntitet(katAdmin.Id);
        arbetsratt.SattKategoriEntitet(katAdmin.Id);

        // === Skill Relations ===
        db.SkillRelations.AddRange(
            SkillRelation.Skapa(hlr.Id, triage.Id, "Related"),
            SkillRelation.Skapa(lakemedel.Id, journal.Id, "Related"),
            SkillRelation.Skapa(ledarskap.Id, projektledning.Id, "Prerequisite"));

        // === Inferred Skills ===
        db.InferredSkills.AddRange(
            InferredSkill.Skapa(employees[0].Id.Value, triage.Id, "Befattning", 75),
            InferredSkill.Skapa(employees[1].Id.Value, ventilator.Id, "Erfarenhet", 60),
            InferredSkill.Skapa(employees[3].Id.Value, ledarskap.Id, "Kurs", 50));

        // === Career Paths ===
        var cpVard = CareerPath.Skapa("Sjukskoterska till Vardenhetschef", "Vard",
            "Karriarvag fran grundutbildad sjukskoterska till enhetschef");
        cpVard.LaggTillSteg("Sjukskoterska", 1, 24,
            "[{\"Skill\":\"HLR\",\"Niva\":3},{\"Skill\":\"Lakemedel\",\"Niva\":3}]", 0);
        cpVard.LaggTillSteg("Specialistsjukskoterska", 2, 36,
            "[{\"Skill\":\"HLR\",\"Niva\":5},{\"Skill\":\"Ventilatorvard\",\"Niva\":3}]", 24);
        cpVard.LaggTillSteg("Vardenhetschef", 3, 0,
            "[{\"Skill\":\"Ledarskap\",\"Niva\":4},{\"Skill\":\"Kommunikation\",\"Niva\":4}]", 60);

        var cpIt = CareerPath.Skapa("Utvecklare till Tech Lead", "IT",
            "Karriarvag inom IT-utveckling");
        cpIt.LaggTillSteg("Utvecklare", 1, 24,
            "[{\"Skill\":\"IT-system\",\"Niva\":3}]", 0);
        cpIt.LaggTillSteg("Senior Utvecklare", 2, 36,
            "[{\"Skill\":\"IT-system\",\"Niva\":4},{\"Skill\":\"Projektledning\",\"Niva\":2}]", 24);
        cpIt.LaggTillSteg("Tech Lead", 3, 0,
            "[{\"Skill\":\"IT-system\",\"Niva\":5},{\"Skill\":\"Ledarskap\",\"Niva\":3}]", 60);

        db.CareerPaths.AddRange(cpVard, cpIt);

        // === Internal Opportunity (published project) ===
        var opportunity = InternalOpportunity.Skapa("Projekt", "Digitalisering av patientjournaler",
            sjukhus.Id.Value,
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today.AddMonths(6)),
            "[{\"Skill\":\"IT-system\",\"Niva\":3},{\"Skill\":\"Journalforing\",\"Niva\":2}]");
        opportunity.Publicera();
        db.InternalOpportunities.Add(opportunity);

        // === Mentor Relations ===
        db.MentorRelations.AddRange(
            MentorRelation.Skapa(employees[7].Id.Value, employees[0].Id.Value, "Ledarskap och karriarutveckling",
                DateOnly.FromDateTime(DateTime.Today.AddMonths(-2)), 14),
            MentorRelation.Skapa(employees[1].Id.Value, employees[3].Id.Value, "Klinisk specialisering",
                DateOnly.FromDateTime(DateTime.Today.AddMonths(-1)), 7));

        // === Provisioning rules (default konfiguration) ===
        db.ProvisioningRules.AddRange(
            Provisioning.ProvisioningRule.Skapa(
                Provisioning.ProvisioningTrigger.NyAnstallning,
                "Active Directory",
                Provisioning.ProvisioningAktion.SkapaKonto,
                "Skapa AD-konto vid nyanstallning"),
            Provisioning.ProvisioningRule.Skapa(
                Provisioning.ProvisioningTrigger.NyAnstallning,
                "E-post (Exchange)",
                Provisioning.ProvisioningAktion.SkapaEpost,
                "Skapa e-postlada vid nyanstallning"),
            Provisioning.ProvisioningRule.Skapa(
                Provisioning.ProvisioningTrigger.AvslutadAnstallning,
                "Active Directory",
                Provisioning.ProvisioningAktion.InaktiveraKonto,
                "Inaktivera AD-konto vid avslut"),
            Provisioning.ProvisioningRule.Skapa(
                Provisioning.ProvisioningTrigger.AvslutadAnstallning,
                "Passersystem",
                Provisioning.ProvisioningAktion.SparraPasserkort,
                "Sparra passerkort vid avslut"));

        // === Arbetsmiljo: incidents, safety rounds, risk assessments ===
        db.Incidents.AddRange(
            Arbetsmiljo.Incident.Skapa(DateTime.UtcNow.AddDays(-12), "Anna Svensson", avd32.Id.Value,
                "Korridoren vid rum 4", "Halkolycka pa vatt golv efter stadning", Arbetsmiljo.IncidentAllvarlighetsgrad.Medel,
                Arbetsmiljo.IncidentTyp.Tillbud, "Battre skyltning vid vatgolv"),
            Arbetsmiljo.Incident.Skapa(DateTime.UtcNow.AddDays(-8), "Karl Berg", iva.Id.Value,
                "IVA sal 2", "Nalsticka vid blodprovstagning", Arbetsmiljo.IncidentAllvarlighetsgrad.Hog,
                Arbetsmiljo.IncidentTyp.Arbetsskada, "Genomgang av stickskadeprevention"),
            Arbetsmiljo.Incident.Skapa(DateTime.UtcNow.AddDays(-5), "Maria Lindgren", avd33.Id.Value,
                "Personalrum", "Ergonomisk brist — for laga arbetsstolar", Arbetsmiljo.IncidentAllvarlighetsgrad.Lag,
                Arbetsmiljo.IncidentTyp.Tillbud, "Bestall nya stolar"),
            Arbetsmiljo.Incident.Skapa(DateTime.UtcNow.AddDays(-3), "Erik Johansson", akuten.Id.Value,
                "Akutens vantrum", "Hotfull patient mot personal", Arbetsmiljo.IncidentAllvarlighetsgrad.Kritisk,
                Arbetsmiljo.IncidentTyp.Tillbud, "Genomgang av hot- och valdpolicy"),
            Arbetsmiljo.Incident.Skapa(DateTime.UtcNow.AddDays(-1), "Helena Bergstrom", avd33.Id.Value,
                "Medicinforraadet", "Felmarkerad medicinburk", Arbetsmiljo.IncidentAllvarlighetsgrad.Hog,
                Arbetsmiljo.IncidentTyp.Tillbud, "Kontroll av alla medicinforrrad"));

        db.SafetyRounds.AddRange(
            Arbetsmiljo.SafetyRound.Skapa(DateTime.UtcNow.AddDays(-30), avd32.Id.Value,
                "Anna Svensson, Skyddsombud Lars Ek", 3, Arbetsmiljo.SafetyRoundStatus.Genomford,
                "Brister: belysning korridor, handtag toalett, ventilation rum 6"),
            Arbetsmiljo.SafetyRound.Skapa(DateTime.UtcNow.AddDays(-15), iva.Id.Value,
                "Karl Berg, Skyddsombud Eva Lind", 1, Arbetsmiljo.SafetyRoundStatus.Genomford,
                "Brist: nododusch saknar skylt"),
            Arbetsmiljo.SafetyRound.Skapa(DateTime.UtcNow.AddDays(14), akuten.Id.Value,
                "Erik Johansson, Skyddsombud Maria Ek", 0, Arbetsmiljo.SafetyRoundStatus.Planerad, null),
            Arbetsmiljo.SafetyRound.Skapa(DateTime.UtcNow.AddDays(30), avd33.Id.Value,
                "Helena Bergstrom, Skyddsombud Per Lund", 0, Arbetsmiljo.SafetyRoundStatus.Planerad, null));

        db.RiskAssessments.AddRange(
            Arbetsmiljo.RiskAssessment.Skapa("Halkolyckor", "Risk for halkolyckor pa nystadade golv", 3, 2,
                "Installera anti-halkskyltning", "Facilitetsansvarig", DateOnly.FromDateTime(DateTime.Today.AddDays(30))),
            Arbetsmiljo.RiskAssessment.Skapa("Stickskador", "Risk for nalsticka vid blodprov och injektioner", 2, 4,
                "Obligatorisk utbildning i sakra injektionstekniker", "Vardchef", DateOnly.FromDateTime(DateTime.Today.AddDays(60))),
            Arbetsmiljo.RiskAssessment.Skapa("Hot och vald", "Risk for hotfulla patienter pa akuten", 3, 5,
                "Utbildning i bemotande, overfallslarm, kamerapolicy", "Verksamhetschef", DateOnly.FromDateTime(DateTime.Today.AddDays(14))),
            Arbetsmiljo.RiskAssessment.Skapa("Ergonomi", "Risk for belastningsskador vid tunga lyft", 4, 3,
                "Lyfthjalpmedel, ergonomiutbildning", "Arbetsmiljosamordnare", DateOnly.FromDateTime(DateTime.Today.AddDays(45))),
            Arbetsmiljo.RiskAssessment.Skapa("Medicinhantering", "Risk for felmedicinering vid markning", 2, 5,
                "Dubbelsignering, barkodsystem", "Lakemedelsansvarig", DateOnly.FromDateTime(DateTime.Today.AddDays(90))));

        // === RehabCase — skapade via domänens SkapaForSeed() ===
        // Milstolpar beräknas konsekvent från givet startdatum av SkapaForSeed().
        // Detta representerar planerade uppföljningsdatum, inte verifierad sjukfallsstart.

        // Karl Berg — upprepad korttidsfrånvaro, startade ~45 dagar sedan
        var rehabKarl = RehabCase.SkapaForSeed(
            employees[3].Id, RehabTrigger.SexTillfallenTolvManader, DateTime.UtcNow.AddDays(-45));
        rehabKarl.TilldelaArendeagare(employees[8].Id); // Eva Nilsson (HR)
        rehabKarl.SattRehabPlan("Uppfoljningssamtal varannan vecka, anpassad arbetsatergang diskuteras");
        db.RehabCases.Add(rehabKarl);

        // Helena Bergstrom — långtidssjuk >14 dagar, startade ~22 dagar sedan
        var rehabHelena = RehabCase.SkapaForSeed(
            employees[6].Id, RehabTrigger.FjortonSammanhangandeDagar, DateTime.UtcNow.AddDays(-22));
        rehabHelena.TilldelaArendeagare(employees[8].Id);
        db.RehabCases.Add(rehabHelena);

        // Maria Lindgren — chef initierat, startade ~160 dagar sedan (passerat dag 90)
        var rehabMaria = RehabCase.SkapaForSeed(
            employees[2].Id, RehabTrigger.ChefInitierat, DateTime.UtcNow.AddDays(-160));
        rehabMaria.TilldelaArendeagare(employees[8].Id);
        rehabMaria.SattRehabPlan("Arbetsformagebedomning genomford, deltidsatergang 50%");
        db.RehabCases.Add(rehabMaria);

        // === Journey templates + instances ===
        var onboardingMall = JourneyTemplate.Skapa("Ny medarbetare", JourneyKategori.Onboarding,
            "Onboarding-process for nyanstallda — 12 steg over 90 dagar");
        onboardingMall.LaggTillSteg("Skapa konton", "AD-konto, e-post, systembehorigheter", "IT", -5);
        onboardingMall.LaggTillSteg("Bestall utrustning", "Dator, telefon, passerkort", "IT", -3);
        onboardingMall.LaggTillSteg("Forbered arbetsplats", "Skrivbord, nyckel, introduktionsmaterial", "Chef", -2);
        onboardingMall.LaggTillSteg("Valkomstmote", "Introduktion till teamet och verksamheten", "Chef", 1);
        onboardingMall.LaggTillSteg("HR-introduktion", "Avtal, policyer, friskvard, lon", "HR", 1);
        onboardingMall.LaggTillSteg("IT-genomgang", "Inloggning, system, support", "IT", 2);
        onboardingMall.LaggTillSteg("Sakerhetsutbildning", "Obligatorisk brandskydd och HLR", "System", 3);
        onboardingMall.LaggTillSteg("Forsta uppfoljning", "Hur gar det? Fragor?", "Chef", 5);
        onboardingMall.LaggTillSteg("Mentor check-in", "Fragor och stod fran mentor", "Mentor", 14);
        onboardingMall.LaggTillSteg("30-dagarssamtal", "Formell uppfoljning med chef", "Chef", 30);
        onboardingMall.LaggTillSteg("60-dagarssamtal", "Utvecklingsplan och feedback", "Chef", 60);
        onboardingMall.LaggTillSteg("Provanstallning avslutas", "Utvardering och beslut", "HR", 90);
        db.JourneyTemplates.Add(onboardingMall);

        var avslutMall = JourneyTemplate.Skapa("Avslut", JourneyKategori.Avslut,
            "Offboarding-process vid anstallningens slut");
        avslutMall.LaggTillSteg("Kunskapsoverforing", "Dokumentera och overlat arbetsuppgifter", "Chef", -14);
        avslutMall.LaggTillSteg("Aterlamnning utrustning", "Dator, telefon, nycklar, passerkort", "Chef", -2);
        avslutMall.LaggTillSteg("Stang IT-behorigheter", "AD, e-post, VPN, system", "IT", 0);
        avslutMall.LaggTillSteg("Slutlon beraknad", "Semester, komptid, overtid", "HR", 0);
        avslutMall.LaggTillSteg("Tjanstgoringsintyg", "Utfarda och signera", "HR", 0);
        avslutMall.LaggTillSteg("Arbetsgivarintyg AF", "Utfarda for Arbetsformedlingen", "HR", 0);
        avslutMall.LaggTillSteg("Exit-samtal", "Avslutande samtal med chef", "Chef", -1);
        avslutMall.LaggTillSteg("GDPR-gallringsplan", "Upprata plan for dataradering", "HR", 5);
        db.JourneyTemplates.Add(avslutMall);

        // Journey instances — skapade via SkapaFranMall() for korrekt snapshot
        var onboardingAnna = JourneyInstance.SkapaFranMall(
            onboardingMall, employees[0].Id.Value,
            $"{employees[0].Fornamn} {employees[0].Efternamn}",
            DateTime.UtcNow.AddDays(-10));
        // Markera de forsta 5 stegen som klara (simulerar pagar ande onboarding)
        foreach (var steg in onboardingAnna.Steg.Take(5))
            onboardingAnna.MarkeraStegKlart(steg.Id, "Seed");
        db.JourneyInstances.Add(onboardingAnna);

        var onboardingSara = JourneyInstance.SkapaFranMall(
            onboardingMall, employees[4].Id.Value,
            $"{employees[4].Fornamn} {employees[4].Efternamn}",
            DateTime.UtcNow.AddDays(-3));
        // Markera forsta 2 stegen
        foreach (var steg in onboardingSara.Steg.Take(2))
            onboardingSara.MarkeraStegKlart(steg.Id, "Seed");
        db.JourneyInstances.Add(onboardingSara);

        // === Benefits catalog + employee selections ===
        var friskvard = RegionHR.Benefits.Domain.Benefit.Skapa(
            "Friskvardsbidrag", "Bidrag for fysisk aktivitet (gym, simhall, massage)",
            RegionHR.Benefits.Domain.BenefitCategory.Friskvard, 5000m, 100m, false);
        var sjukforsakring = RegionHR.Benefits.Domain.Benefit.Skapa(
            "Extra sjukvardsforsakring", "Privatvardsforsakring via Skandia",
            RegionHR.Benefits.Domain.BenefitCategory.Sjukvard, 0m, 100m, false);
        var cykel = RegionHR.Benefits.Domain.Benefit.Skapa(
            "Cykelfoman", "Cykel via bruttoloneavdrag, max 3000 kr/man",
            RegionHR.Benefits.Domain.BenefitCategory.Ovrigt, 3000m, 0m, true);
        var pension = RegionHR.Benefits.Domain.Benefit.Skapa(
            "AKAP-KR Pension", "Tjanstepension 6% under 7.5 IBB, 31.5% over",
            RegionHR.Benefits.Domain.BenefitCategory.Pension, 0m, 100m, false);
        var utbildning = RegionHR.Benefits.Domain.Benefit.Skapa(
            "Kompetensutvecklingsbidrag", "Bidrag for arbetsrelaterad vidareutbildning",
            RegionHR.Benefits.Domain.BenefitCategory.Utbildning, 15000m, 100m, false);
        db.Benefits.AddRange(friskvard, sjukforsakring, cykel, pension, utbildning);

        // Employee benefit selections via domänens Anmala()
        var valAnna = RegionHR.Benefits.Domain.EmployeeBenefit.Anmala(
            employees[0].Id.Value, friskvard.Id, DateOnly.FromDateTime(DateTime.Today), 5000m);
        valAnna.Godkann();
        var valErik = RegionHR.Benefits.Domain.EmployeeBenefit.Anmala(
            employees[1].Id.Value, friskvard.Id, DateOnly.FromDateTime(DateTime.Today), 3500m);
        valErik.Godkann();
        var valKarl = RegionHR.Benefits.Domain.EmployeeBenefit.Anmala(
            employees[3].Id.Value, cykel.Id, DateOnly.FromDateTime(DateTime.Today), 2000m, "Nytt barn — andrat pendlingsvanor");
        db.EmployeeBenefits.AddRange(valAnna, valErik, valKarl);

        // === Talent pool candidates via domänens Skapa() ===
        db.TalentPoolEntries.AddRange(
            RegionHR.Recruitment.Domain.TalentPoolEntry.Skapa(
                "Lisa Ekstrom", "lisa.ekstrom@mail.se", "IVA, akutsjukvard",
                "8 ars erfarenhet, intervjuad for SSK IVA men inte antagen. Mycket stark kandidat."),
            RegionHR.Recruitment.Domain.TalentPoolEntry.Skapa(
                "Per Strand", "per.strand@mail.se", "Vardcentral, slutenvard",
                "3 ars erfarenhet. Intresserad av framtida tjanster."),
            RegionHR.Recruitment.Domain.TalentPoolEntry.Skapa(
                "Sofia Magnusson", "sofia.magnusson@mail.se", "Intensivvard, anestesi",
                "Specialistsjukskoterska anestesi. Spontanansokande."),
            RegionHR.Recruitment.Domain.TalentPoolEntry.Skapa(
                "Oscar Blom", "oscar.blom@mail.se", "Underskoterska, natt",
                "2 ars erfarenhet nattjour. Soker heltid."),
            RegionHR.Recruitment.Domain.TalentPoolEntry.Skapa(
                "Karin Ek", "karin.ek@mail.se", "Sjukskoterska, barnklinik",
                "Nyexaminerad 2025. Praktik pa USÖ."));

        // === HeadcountPlan per enhet (budget for 2026) ===
        var hcAvd32 = RegionHR.Positions.Domain.HeadcountPlan.Skapa(avd32.Id.Value, 2026, 8, 8.0m, 2_760_000m);
        hcAvd32.UppdateraFaktiskt(8, 8.0m, 2_832_000m);
        var hcAvd33 = RegionHR.Positions.Domain.HeadcountPlan.Skapa(avd33.Id.Value, 2026, 6, 6.0m, 1_944_000m);
        hcAvd33.UppdateraFaktiskt(5, 5.0m, 1_620_000m);
        var hcAkuten = RegionHR.Positions.Domain.HeadcountPlan.Skapa(akuten.Id.Value, 2026, 12, 12.0m, 6_048_000m);
        hcAkuten.UppdateraFaktiskt(10, 10.0m, 5_040_000m);
        var hcIva = RegionHR.Positions.Domain.HeadcountPlan.Skapa(iva.Id.Value, 2026, 10, 10.0m, 3_816_000m);
        hcIva.UppdateraFaktiskt(7, 7.0m, 2_671_200m);
        db.HeadcountPlans.AddRange(hcAvd32, hcAvd33, hcAkuten, hcIva);

        // === Notifications via domänens Create() ===
        // UserId sätts till anställds Id.Value. Auth saknar EmployeeId-mapping
        // så i v1.5 matchar detta inte säkert inloggad användare.
        db.Notifications.AddRange(
            RegionHR.Notifications.Domain.Notification.Create(
                employees[0].Id.Value, "Ledighetsansokan godkand",
                "Din semester 14-18 juli har godkants av chef.",
                RegionHR.Notifications.Domain.NotificationType.Info, actionUrl: "/ledighet"),
            RegionHR.Notifications.Domain.Notification.Create(
                employees[0].Id.Value, "Nytt lonebesked",
                "Lonebeskedet for mars 2026 finns tillgangligt.",
                RegionHR.Notifications.Domain.NotificationType.Info, actionUrl: "/minsida/lon"),
            RegionHR.Notifications.Domain.Notification.Create(
                employees[3].Id.Value, "Certifiering gar ut snart",
                "Din sjukskoterska-legitimation gar ut om 45 dagar.",
                RegionHR.Notifications.Domain.NotificationType.Warning, actionUrl: "/kompetens"),
            RegionHR.Notifications.Domain.Notification.Create(
                employees[1].Id.Value, "Schemaandring",
                "Ditt schema for vecka 13 har uppdaterats.",
                RegionHR.Notifications.Domain.NotificationType.Info, actionUrl: "/minsida/schema"),
            RegionHR.Notifications.Domain.Notification.Create(
                employees[7].Id.Value, "Medarbetarsamtal bokat",
                "Arligt medarbetarsamtal inbokat 25 mars kl. 10:00.",
                RegionHR.Notifications.Domain.NotificationType.Action, actionUrl: "/medarbetarsamtal"));

        // === AuditEntries via domänens Create() ===
        db.AuditEntries.AddRange(
            RegionHR.Audit.Domain.AuditEntry.Create(
                "Employee", employees[0].Id.Value.ToString(), RegionHR.Audit.Domain.AuditAction.Create,
                null, "{\"Fornamn\":\"Anna\",\"Efternamn\":\"Svensson\"}", "System", "System", null),
            RegionHR.Audit.Domain.AuditEntry.Create(
                "Employee", employees[0].Id.Value.ToString(), RegionHR.Audit.Domain.AuditAction.Update,
                "{\"Befattning\":\"Underskoterska\"}", "{\"Befattning\":\"Sjukskoterska\"}", employees[8].Id.Value.ToString(), "Eva Nilsson (HR)", null),
            RegionHR.Audit.Domain.AuditEntry.Create(
                "PayrollRun", Guid.NewGuid().ToString(), RegionHR.Audit.Domain.AuditAction.Create,
                null, "{\"Period\":\"2026-02\",\"Status\":\"Beraknad\"}", "System", "System", null),
            RegionHR.Audit.Domain.AuditEntry.Create(
                "LeaveRequest", Guid.NewGuid().ToString(), RegionHR.Audit.Domain.AuditAction.Create,
                null, "{\"Typ\":\"Semester\",\"FranDatum\":\"2026-07\"}", employees[0].Id.Value.ToString(), "Anna Svensson", null),
            RegionHR.Audit.Domain.AuditEntry.Create(
                "Position", posSsk32.Id.ToString(), RegionHR.Audit.Domain.AuditAction.Update,
                "{\"Status\":\"Vakant\"}", "{\"Status\":\"Aktiv\"}", employees[8].Id.Value.ToString(), "Eva Nilsson (HR)", null));

        // === DataSubjectRequests via domänens Skapa() ===
        var dsrRegisterutdrag = RegionHR.GDPR.Domain.DataSubjectRequest.Skapa(employees[4].Id.Value, RegionHR.GDPR.Domain.RequestType.Registerutdrag);
        dsrRegisterutdrag.Tilldela(employees[8].Id.Value.ToString()); // Eva Nilsson (HR)
        var dsrRadering = RegionHR.GDPR.Domain.DataSubjectRequest.Skapa(employees[5].Id.Value, RegionHR.GDPR.Domain.RequestType.Radering);
        db.DataSubjectRequests.AddRange(dsrRegisterutdrag, dsrRadering);

        // === Cases with pending approvals via domänlogik ===
        var semesterCase = RegionHR.CaseManagement.Domain.Case.SkapaFranvaroarende(
            employees[0].Id, RegionHR.SharedKernel.Domain.AbsenceType.Semester,
            DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(34)),
            "Semester vecka 20");
        semesterCase.SkickaForGodkannande("Chefsgodkannande", employees[7].Id); // Anders Olsson (VC)
        db.Cases.Add(semesterCase);

        var vabCase = RegionHR.CaseManagement.Domain.Case.SkapaFranvaroarende(
            employees[3].Id, RegionHR.SharedKernel.Domain.AbsenceType.VAB,
            DateOnly.FromDateTime(DateTime.Today.AddDays(2)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(3)),
            "VAB - barn sjukt");
        vabCase.SkickaForGodkannande("Chefsgodkannande", employees[7].Id);
        db.Cases.Add(vabCase);

        var tjledCase = RegionHR.CaseManagement.Domain.Case.SkapaFranvaroarende(
            employees[6].Id, RegionHR.SharedKernel.Domain.AbsenceType.Tjanstledighet,
            DateOnly.FromDateTime(DateTime.Today.AddDays(60)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(120)),
            "Studieledighet HT 2026");
        tjledCase.SkickaForGodkannande("HR-godkannande", employees[8].Id); // Eva Nilsson (HR)
        db.Cases.Add(tjledCase);

        // === LeaveRequests + VacationBalances via domänlogik ===
        // VacationBalance per anställd för 2026
        db.VacationBalances.AddRange(
            RegionHR.Leave.Domain.VacationBalance.SkapaForAr(employees[0].Id.Value, 2026, 41), // Anna, 41 → 31 dagar
            RegionHR.Leave.Domain.VacationBalance.SkapaForAr(employees[1].Id.Value, 2026, 48), // Erik, 48 → 31
            RegionHR.Leave.Domain.VacationBalance.SkapaForAr(employees[2].Id.Value, 2026, 36), // Maria, 36 → 25
            RegionHR.Leave.Domain.VacationBalance.SkapaForAr(employees[3].Id.Value, 2026, 44)); // Karl, 44 → 31

        // LeaveRequests i olika statusar
        var semAnna = RegionHR.Leave.Domain.LeaveRequest.Skapa(
            employees[0].Id.Value, RegionHR.Leave.Domain.LeaveType.Semester,
            DateOnly.FromDateTime(DateTime.Today.AddDays(40)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(54)),
            "Sommarsemester v28-v30");
        semAnna.SkickaIn();
        db.LeaveRequests.Add(semAnna);

        var semErik = RegionHR.Leave.Domain.LeaveRequest.Skapa(
            employees[1].Id.Value, RegionHR.Leave.Domain.LeaveType.Semester,
            DateOnly.FromDateTime(DateTime.Today.AddDays(20)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(24)),
            "Sportlov med familjen");
        semErik.SkickaIn();
        semErik.Godkann(employees[7].Id.Value, "Godkant");
        db.LeaveRequests.Add(semErik);

        var kompKarl = RegionHR.Leave.Domain.LeaveRequest.Skapa(
            employees[3].Id.Value, RegionHR.Leave.Domain.LeaveType.Komptid,
            DateOnly.FromDateTime(DateTime.Today.AddDays(5)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(5)),
            "Kompdag for jourtjanst");
        kompKarl.SkickaIn();
        db.LeaveRequests.Add(kompKarl);

        // Föräldraledighet — Maria Lindgren, godkänd
        var flMaria = RegionHR.Leave.Domain.LeaveRequest.Skapa(
            employees[2].Id.Value, RegionHR.Leave.Domain.LeaveType.Foraldraledighet,
            DateOnly.FromDateTime(DateTime.Today.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(150)),
            "Foraldraledighet 6 manader");
        flMaria.SkickaIn();
        flMaria.Godkann(employees[7].Id.Value, "Godkant");
        db.LeaveRequests.Add(flMaria);

        // Föräldraledighet — Erik Johansson, inskickad
        var flErik = RegionHR.Leave.Domain.LeaveRequest.Skapa(
            employees[1].Id.Value, RegionHR.Leave.Domain.LeaveType.Foraldraledighet,
            DateOnly.FromDateTime(DateTime.Today.AddDays(60)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(120)),
            "Foraldraledighet hosten");
        flErik.SkickaIn();
        db.LeaveRequests.Add(flErik);

        // VAB — Karl Berg, godkänd
        var vabKarl = RegionHR.Leave.Domain.LeaveRequest.Skapa(
            employees[3].Id.Value, RegionHR.Leave.Domain.LeaveType.VAB,
            DateOnly.FromDateTime(DateTime.Today.AddDays(-7)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(-6)),
            "Barn sjukt i feber");
        vabKarl.SkickaIn();
        vabKarl.Godkann(employees[7].Id.Value, "Godkant");
        db.LeaveRequests.Add(vabKarl);

        // VAB — Anna Svensson, inskickad (pågående)
        var vabAnna = RegionHR.Leave.Domain.LeaveRequest.Skapa(
            employees[0].Id.Value, RegionHR.Leave.Domain.LeaveType.VAB,
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today),
            "VAB magsjuka");
        vabAnna.SkickaIn();
        db.LeaveRequests.Add(vabAnna);

        // === PayrollRuns + PayrollResults via domänlogik ===
        // Februari 2026 — utbetald körning
        var runFeb = RegionHR.Payroll.Domain.PayrollRun.Skapa(2026, 2, "System");
        runFeb.Paborja();
        for (var i = 0; i < 4; i++)
        {
            var emp = employees[i];
            var empl = employments[i];
            var lon = seedEmployees[i].Lon;
            var result = RegionHR.Payroll.Domain.PayrollResult.Skapa(
                runFeb.Id, emp.Id, empl.Id, 2026, 2,
                lon, 100m, RegionHR.SharedKernel.Domain.CollectiveAgreementType.AB);
            result.Brutto = lon;
            result.Skatt = Money.SEK(Math.Round(lon.Amount * 0.30m, 0));
            result.Netto = Money.SEK(lon.Amount - Math.Round(lon.Amount * 0.30m, 0));
            result.Arbetsgivaravgifter = Money.SEK(Math.Round(lon.Amount * 0.3142m, 0));
            result.ArbetsgivaravgiftSats = 31.42m;
            runFeb.LaggTillResultat(result);
        }
        runFeb.MarkeraSomBeraknad();
        runFeb.Godkann("Eva Nilsson");
        runFeb.MarkeraSomUtbetald();
        db.PayrollRuns.Add(runFeb);

        // Mars 2026 — beräknad körning (med OB-tillägg på Anna)
        var runMars = RegionHR.Payroll.Domain.PayrollRun.Skapa(2026, 3, "System");
        runMars.Paborja();
        for (var i = 0; i < 4; i++)
        {
            var emp = employees[i];
            var empl = employments[i];
            var lon = seedEmployees[i].Lon;
            var result = RegionHR.Payroll.Domain.PayrollResult.Skapa(
                runMars.Id, emp.Id, empl.Id, 2026, 3,
                lon, 100m, RegionHR.SharedKernel.Domain.CollectiveAgreementType.AB);
            var brutto = lon.Amount;
            // Anna Svensson (i=0): 3 nattpass OB
            if (i == 0)
            {
                result.OBTillagg = Money.SEK(3 * 113m); // natt-OB 113 kr/h * 3
                brutto += result.OBTillagg.Amount;
            }
            // Erik Johansson (i=1): övertid
            if (i == 1)
            {
                result.Overtidstillagg = Money.SEK(8 * 62000m / 165m * 1.8m); // 8h övertid
                brutto += result.Overtidstillagg.Amount;
            }
            result.Brutto = Money.SEK(Math.Round(brutto, 0));
            result.Skatt = Money.SEK(Math.Round(brutto * 0.30m, 0));
            result.Netto = Money.SEK(Math.Round(brutto * 0.70m, 0));
            result.Arbetsgivaravgifter = Money.SEK(Math.Round(brutto * 0.3142m, 0));
            result.ArbetsgivaravgiftSats = 31.42m;
            runMars.LaggTillResultat(result);
        }
        runMars.MarkeraSomBeraknad();
        db.PayrollRuns.Add(runMars);

        // === ScheduledShifts (idag, baserat på DateTime.Today) ===
        var idagDatum = DateOnly.FromDateTime(DateTime.Today);
        // FK-krav (PostgreSQL): passen måste peka på ett Schedule som faktiskt finns.
        var grundschema = RegionHR.Scheduling.Domain.Schedule.SkapaGrundschema(
            avd32.Id, "Grundschema Avdelning 32", idagDatum.AddDays(-28), 4);
        db.Schedules.Add(grundschema);
        var schemaId = grundschema.Id;
        db.ScheduledShifts.AddRange(
            new RegionHR.Scheduling.Domain.ScheduledShift { Id = Guid.NewGuid(), SchemaId = schemaId, AnstallId = employees[0].Id, Datum = idagDatum, PassTyp = RegionHR.Scheduling.Domain.ShiftType.Dag, PlaneradStart = new TimeOnly(7, 0), PlaneradSlut = new TimeOnly(16, 0), Rast = TimeSpan.FromMinutes(60), Status = RegionHR.Scheduling.Domain.ShiftStatus.Planerad, OBKategori = OBCategory.Ingen },
            new RegionHR.Scheduling.Domain.ScheduledShift { Id = Guid.NewGuid(), SchemaId = schemaId, AnstallId = employees[2].Id, Datum = idagDatum, PassTyp = RegionHR.Scheduling.Domain.ShiftType.Dag, PlaneradStart = new TimeOnly(7, 0), PlaneradSlut = new TimeOnly(16, 0), Rast = TimeSpan.FromMinutes(60), Status = RegionHR.Scheduling.Domain.ShiftStatus.Planerad, OBKategori = OBCategory.Ingen },
            new RegionHR.Scheduling.Domain.ScheduledShift { Id = Guid.NewGuid(), SchemaId = schemaId, AnstallId = employees[4].Id, Datum = idagDatum, PassTyp = RegionHR.Scheduling.Domain.ShiftType.Dag, PlaneradStart = new TimeOnly(7, 0), PlaneradSlut = new TimeOnly(16, 0), Rast = TimeSpan.FromMinutes(60), Status = RegionHR.Scheduling.Domain.ShiftStatus.Planerad, OBKategori = OBCategory.Ingen },
            new RegionHR.Scheduling.Domain.ScheduledShift { Id = Guid.NewGuid(), SchemaId = schemaId, AnstallId = employees[6].Id, Datum = idagDatum, PassTyp = RegionHR.Scheduling.Domain.ShiftType.Kvall, PlaneradStart = new TimeOnly(15, 0), PlaneradSlut = new TimeOnly(22, 0), Rast = TimeSpan.FromMinutes(30), Status = RegionHR.Scheduling.Domain.ShiftStatus.Planerad, OBKategori = OBCategory.VardagKvall },
            new RegionHR.Scheduling.Domain.ScheduledShift { Id = Guid.NewGuid(), SchemaId = schemaId, AnstallId = employees[9].Id, Datum = idagDatum, PassTyp = RegionHR.Scheduling.Domain.ShiftType.Kvall, PlaneradStart = new TimeOnly(15, 0), PlaneradSlut = new TimeOnly(22, 0), Rast = TimeSpan.FromMinutes(30), Status = RegionHR.Scheduling.Domain.ShiftStatus.Planerad, OBKategori = OBCategory.VardagKvall },
            new RegionHR.Scheduling.Domain.ScheduledShift { Id = Guid.NewGuid(), SchemaId = schemaId, AnstallId = employees[3].Id, Datum = idagDatum, PassTyp = RegionHR.Scheduling.Domain.ShiftType.Natt, PlaneradStart = new TimeOnly(21, 0), PlaneradSlut = new TimeOnly(7, 0), Rast = TimeSpan.FromMinutes(45), Status = RegionHR.Scheduling.Domain.ShiftStatus.Planerad, OBKategori = OBCategory.VardagNatt });

        // Historiska avslutade pass med faktisk tid → ger direkt ett flexsaldo att titta på (Anna, employees[0]) — våg 2/tid-flex
        db.ScheduledShifts.AddRange(
            new RegionHR.Scheduling.Domain.ScheduledShift { Id = Guid.NewGuid(), SchemaId = schemaId, AnstallId = employees[0].Id,
                Datum = DateOnly.FromDateTime(DateTime.Today.AddDays(-3)), PassTyp = RegionHR.Scheduling.Domain.ShiftType.Dag,
                PlaneradStart = new TimeOnly(8, 0), PlaneradSlut = new TimeOnly(16, 0), Rast = TimeSpan.FromMinutes(30),
                FaktiskStart = new TimeOnly(8, 0), FaktiskSlut = new TimeOnly(17, 0), OvertidTimmar = 1m,
                Status = RegionHR.Scheduling.Domain.ShiftStatus.Avslutad, OBKategori = OBCategory.Ingen },
            new RegionHR.Scheduling.Domain.ScheduledShift { Id = Guid.NewGuid(), SchemaId = schemaId, AnstallId = employees[0].Id,
                Datum = DateOnly.FromDateTime(DateTime.Today.AddDays(-2)), PassTyp = RegionHR.Scheduling.Domain.ShiftType.Dag,
                PlaneradStart = new TimeOnly(8, 0), PlaneradSlut = new TimeOnly(16, 0), Rast = TimeSpan.FromMinutes(30),
                FaktiskStart = new TimeOnly(8, 15), FaktiskSlut = new TimeOnly(15, 30),
                Status = RegionHR.Scheduling.Domain.ShiftStatus.Avslutad, OBKategori = OBCategory.Ingen });

        // === Certifications via domänens Skapa() ===
        db.Certifications.AddRange(
            RegionHR.Competence.Domain.Certification.Skapa(
                employees[0].Id.Value, "HLR", RegionHR.Competence.Domain.CertificationType.ObligatoriskUtbildning,
                "Svensk HLR-rad", DateOnly.FromDateTime(DateTime.Today.AddMonths(-6)),
                DateOnly.FromDateTime(DateTime.Today.AddMonths(6)), obligatorisk: true),
            RegionHR.Competence.Domain.Certification.Skapa(
                employees[0].Id.Value, "Brandskydd", RegionHR.Competence.Domain.CertificationType.ObligatoriskUtbildning,
                "MSB", DateOnly.FromDateTime(DateTime.Today.AddMonths(-4)),
                DateOnly.FromDateTime(DateTime.Today.AddMonths(8)), obligatorisk: true),
            RegionHR.Competence.Domain.Certification.Skapa(
                employees[0].Id.Value, "Lakemedelshantering", RegionHR.Competence.Domain.CertificationType.Legitimation,
                "Socialstyrelsen", DateOnly.FromDateTime(DateTime.Today.AddMonths(-18)),
                DateOnly.FromDateTime(DateTime.Today.AddMonths(-3)), obligatorisk: true));

        // === PerformanceReviews via domänens Skapa() ===
        // Anna Svensson — genomfört samtal (alla steg)
        var reviewAnna = RegionHR.Performance.Domain.PerformanceReview.Skapa(
            employees[0].Id.Value, employees[7].Id.Value, 2026); // Chef: Anders Olsson
        reviewAnna.SattSjalvbedomning("Jag har utvecklats inom saravard och tagit mer ansvar i teamet.");
        reviewAnna.SattChefsbedomning("Anna visar gott engagemang och tar initiativ. Utveckling inom ledarskap rekommenderas.", 4);
        reviewAnna.SattMalsattning("Genomga ledarskapsutbildning HT 2026, ta ansvar for handledning av ny personal.");
        reviewAnna.Genomfor();
        db.PerformanceReviews.Add(reviewAnna);

        // Erik Johansson — sjalvbedomning klar, vantar pa chefens bedomning
        var reviewErik = RegionHR.Performance.Domain.PerformanceReview.Skapa(
            employees[1].Id.Value, employees[7].Id.Value, 2026);
        reviewErik.SattSjalvbedomning("Har hanterat hog arbetsbelastning pa akuten. Onskar mer tid for forskning.");
        db.PerformanceReviews.Add(reviewErik);

        // Karl Berg — planerat, ej paborjat
        var reviewKarl = RegionHR.Performance.Domain.PerformanceReview.Skapa(
            employees[3].Id.Value, employees[7].Id.Value, 2026);
        db.PerformanceReviews.Add(reviewKarl);

        // Maria Lindgren — chefsbedomning klar, ej genomfort annu
        var reviewMaria = RegionHR.Performance.Domain.PerformanceReview.Skapa(
            employees[2].Id.Value, employees[7].Id.Value, 2026);
        reviewMaria.SattSjalvbedomning("Trivs pa avdelningen. Vill lara mer om dokumentation.");
        reviewMaria.SattChefsbedomning("Maria ar palitlig och omtyckt. Behover utveckla journalforing.", 3);
        db.PerformanceReviews.Add(reviewMaria);

        // === Courses + CourseEnrollments via domänlogik ===
        var hlrKurs = RegionHR.LMS.Domain.Course.Skapa(
            "HLR — Hjart-lungr\u00e4ddning", "Obligatorisk utbildning i hjart-lungr\u00e4ddning for all vardpersonal.",
            RegionHR.LMS.Domain.CourseFormat.Blandat, 240, true, "Klinisk", 24, 20);
        var brandKurs = RegionHR.LMS.Domain.Course.Skapa(
            "Brandskyddsutbildning", "Grundlaggande brandskydd och utrymning.",
            RegionHR.LMS.Domain.CourseFormat.Klassrum, 120, true, "Sakerhet", 12);
        var gdprKurs = RegionHR.LMS.Domain.Course.Skapa(
            "GDPR for vardpersonal", "Hantering av patientdata enligt GDPR och patientdatalagen.",
            RegionHR.LMS.Domain.CourseFormat.Elearning, 60, true, "Juridik", 12);
        var ledarKurs = RegionHR.LMS.Domain.Course.Skapa(
            "Grundlaggande ledarskap", "Ledarskapsutbildning for blivande chefer.",
            RegionHR.LMS.Domain.CourseFormat.Workshop, 480, false, "Ledarskap");
        var excelKurs = RegionHR.LMS.Domain.Course.Skapa(
            "Excel for HR", "Dataanalys och rapportering med Excel.",
            RegionHR.LMS.Domain.CourseFormat.Elearning, 90, false, "IT");

        // Publicera kurserna (domänmetod om den finns, annars direkt)
        // Course.Skapa() sätter Status=Utkast — vi behöver Publicerad
        // Kolla om Publicera() finns:
        db.Courses.AddRange(hlrKurs, brandKurs, gdprKurs, ledarKurs, excelKurs);

        // Enrollments
        var enrollAnnaHlr = RegionHR.LMS.Domain.CourseEnrollment.Anmala(employees[0].Id.Value, hlrKurs.Id);
        enrollAnnaHlr.Paborja();
        var enrollAnnaGdpr = RegionHR.LMS.Domain.CourseEnrollment.Anmala(employees[0].Id.Value, gdprKurs.Id);
        var enrollErikBrand = RegionHR.LMS.Domain.CourseEnrollment.Anmala(employees[1].Id.Value, brandKurs.Id);
        enrollErikBrand.Paborja();
        var enrollKarlHlr = RegionHR.LMS.Domain.CourseEnrollment.Anmala(employees[3].Id.Value, hlrKurs.Id);
        db.CourseEnrollments.AddRange(enrollAnnaHlr, enrollAnnaGdpr, enrollErikBrand, enrollKarlHlr);

        // === Pulsundersökningar via domänens Skapa() ===
        // 1. Stängd enkät med frågor + responses (för resultatvy)
        var pulsMars = RegionHR.Pulse.Domain.PulseSurvey.Skapa(
            "Pulsundersokning mars 2026", "Manadsvis pulsmating av arbetsmiljo och trivsel.", "Admin");
        pulsMars.LaggTillFraga("Hur trivs du pa jobbet just nu?", 1);
        pulsMars.LaggTillFraga("Kanner du att din arbetsbelastning ar rimlig?", 2);
        pulsMars.LaggTillFraga("Har du tillgang till det stod du behover fran din chef?", 3);
        pulsMars.LaggTillFraga("Kanner du dig delaktig i beslut som paverkar ditt arbete?", 4);
        pulsMars.LaggTillFraga("Skulle du rekommendera din arbetsplats till en van?", 5);
        pulsMars.Oppna();
        pulsMars.Stang();

        // 3 anonyma svar
        var svar1 = RegionHR.Pulse.Domain.PulseSurveyResponse.Skapa(pulsMars.Id);
        foreach (var f in pulsMars.Fragor)
            svar1.LaggTillSvar(f.Id, f.Ordning <= 2 ? 4 : 3);
        var svar2 = RegionHR.Pulse.Domain.PulseSurveyResponse.Skapa(pulsMars.Id);
        foreach (var f in pulsMars.Fragor)
            svar2.LaggTillSvar(f.Id, f.Ordning <= 3 ? 5 : 4);
        var svar3 = RegionHR.Pulse.Domain.PulseSurveyResponse.Skapa(pulsMars.Id);
        foreach (var f in pulsMars.Fragor)
            svar3.LaggTillSvar(f.Id, f.Ordning == 1 ? 3 : 2);

        db.PulseSurveys.Add(pulsMars);
        db.PulseSurveyResponses.AddRange(svar1, svar2, svar3);

        // 2. Öppen enkät utan responses (för svarsflöde)
        var pulsApril = RegionHR.Pulse.Domain.PulseSurvey.Skapa(
            "Pulsundersokning april 2026", "Uppfoljning efter organisationsforandring.", "Admin");
        pulsApril.LaggTillFraga("Hur upplever du forandringarna pa din arbetsplats?", 1);
        pulsApril.LaggTillFraga("Har du fatt tillracklig information om forandringarna?", 2);
        pulsApril.LaggTillFraga("Kanner du dig trygg i din anstallning?", 3);
        pulsApril.Oppna();
        db.PulseSurveys.Add(pulsApril);

        // === Policyer via domänens Skapa() + Publicera() ===
        var policyGdpr = RegionHR.PolicyManagement.Domain.Policy.Skapa(
            "GDPR-policy", "Hantering av personuppgifter enligt GDPR och patientdatalagen.",
            RegionHR.PolicyManagement.Domain.PolicyCategory.GDPR, kraverBekraftelse: true, "Admin",
            sammanfattning: "Alla anstallda ska folja regionens regler for hantering av personuppgifter.");
        policyGdpr.Publicera();

        var policyIt = RegionHR.PolicyManagement.Domain.Policy.Skapa(
            "IT-sakerhetspolicy", "Regler for anvandning av IT-system, losenord och e-post.",
            RegionHR.PolicyManagement.Domain.PolicyCategory.ITSakerhet, kraverBekraftelse: true, "Admin",
            sammanfattning: "Alla anvandare av regionens IT-system ska folja dessa regler.");
        policyIt.Publicera();

        var policyArbetsmiljo = RegionHR.PolicyManagement.Domain.Policy.Skapa(
            "Arbetsmiljopolicy", "Regionens overgripande policy for fysisk och psykosocial arbetsmiljo.",
            RegionHR.PolicyManagement.Domain.PolicyCategory.Arbetsmiljo, kraverBekraftelse: false, "Admin",
            sammanfattning: "Arbetsmiljon ska vara saker och halsoframjande.");

        db.Policies.AddRange(policyGdpr, policyIt, policyArbetsmiljo);

        // Bekräftelser — kopplade till verkliga anställda
        db.PolicyConfirmations.AddRange(
            RegionHR.PolicyManagement.Domain.PolicyConfirmation.Skapa(policyGdpr.Id, employees[0].Id.Value, policyGdpr.Version), // Anna
            RegionHR.PolicyManagement.Domain.PolicyConfirmation.Skapa(policyGdpr.Id, employees[1].Id.Value, policyGdpr.Version), // Erik
            RegionHR.PolicyManagement.Domain.PolicyConfirmation.Skapa(policyGdpr.Id, employees[3].Id.Value, policyGdpr.Version), // Karl
            RegionHR.PolicyManagement.Domain.PolicyConfirmation.Skapa(policyIt.Id, employees[0].Id.Value, policyIt.Version),     // Anna
            RegionHR.PolicyManagement.Domain.PolicyConfirmation.Skapa(policyIt.Id, employees[2].Id.Value, policyIt.Version));    // Maria

        // === Timesheets via domänens Skapa() + RegistreraTimmar() + SkickaIn() etc. ===
        // Anna — Inskickad (mars)
        var tsAnnaMars = RegionHR.Scheduling.Domain.Timesheet.Skapa(employees[0].Id.Value, 2026, 3, 160m);
        tsAnnaMars.RegistreraTimmar(168.5m, 8.5m);
        tsAnnaMars.SkickaIn();

        // Erik — Inskickad (mars)
        var tsErikMars = RegionHR.Scheduling.Domain.Timesheet.Skapa(employees[1].Id.Value, 2026, 3, 160m);
        tsErikMars.RegistreraTimmar(162m, 2m);
        tsErikMars.SkickaIn();

        // Karl — Godkänd (februari)
        var tsKarlFeb = RegionHR.Scheduling.Domain.Timesheet.Skapa(employees[3].Id.Value, 2026, 2, 160m);
        tsKarlFeb.RegistreraTimmar(158m, 0m);
        tsKarlFeb.SkickaIn();
        tsKarlFeb.Godkann(employees[7].Id.Value, "Ser bra ut"); // Anders godkänner

        // Maria — Avslagen (mars)
        var tsMariaMars = RegionHR.Scheduling.Domain.Timesheet.Skapa(employees[2].Id.Value, 2026, 3, 120m);
        tsMariaMars.RegistreraTimmar(135m, 15m);
        tsMariaMars.SkickaIn();
        tsMariaMars.Avvisa(employees[7].Id.Value, "Overtid ej forhandsanmald"); // Anders avvisar

        // Sara — Öppen (mars, ej inskickad)
        var tsSaraMars = RegionHR.Scheduling.Domain.Timesheet.Skapa(employees[4].Id.Value, 2026, 3, 120m);
        tsSaraMars.RegistreraTimmar(100m, 0m);

        db.Timesheets.AddRange(tsAnnaMars, tsErikMars, tsKarlFeb, tsMariaMars, tsSaraMars);

        // === TravelClaims via domänens Skapa() ===
        var reseErik = RegionHR.Travel.Domain.TravelClaim.Skapa(
            employees[1].Id, "Konferens Stockholm", DateOnly.FromDateTime(DateTime.Today.AddDays(-10)));
        reseErik.SattTraktamente(2, 0);
        reseErik.SkickaIn();
        var reseAnna = RegionHR.Travel.Domain.TravelClaim.Skapa(
            employees[0].Id, "Utbildning Goteborg", DateOnly.FromDateTime(DateTime.Today.AddDays(-25)));
        reseAnna.SattTraktamente(1, 0);
        reseAnna.SkickaIn();
        reseAnna.Attestera("Eva Nilsson");
        db.TravelClaims.AddRange(reseErik, reseAnna);

        // === DelegatedAccess via domänens Skapa() ===
        var delegEva = RegionHR.Infrastructure.Authorization.DelegatedAccess.Skapa(
            employees[8].Id.Value, employees[2].Id.Value, "Ledighet – godkanna",
            DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today.AddDays(14)), "Semester");
        var delegAnders = RegionHR.Infrastructure.Authorization.DelegatedAccess.Skapa(
            employees[7].Id.Value, employees[3].Id.Value, "Tidrapporter – attestera",
            DateOnly.FromDateTime(DateTime.Today.AddDays(10)), DateOnly.FromDateTime(DateTime.Today.AddDays(24)), "Konferens");
        db.DelegatedAccesses.AddRange(delegEva, delegAnders);

        // === ShiftSwapRequests via domänens Skapa() ===
        var swap1 = RegionHR.Scheduling.Domain.ShiftSwapRequest.Skapa(
            employees[0].Id, Guid.NewGuid(), "Behover byta pga lakartid");
        swap1.Erbjud(employees[4].Id);
        swap1.Acceptera(employees[4].Id, null);
        var swap2 = RegionHR.Scheduling.Domain.ShiftSwapRequest.Skapa(
            employees[3].Id, Guid.NewGuid(), "Vill byta till dagpass");
        swap2.Erbjud(employees[6].Id);
        db.ShiftSwapRequests.AddRange(swap1, swap2);

        // === WellnessClaims via domänens Skapa() ===
        var friskvardAnna = RegionHR.Wellness.Domain.WellnessClaim.Skapa(
            employees[0].Id.Value, "Personlig traning", 2000m, DateOnly.FromDateTime(DateTime.Today.AddDays(-15)));
        friskvardAnna.Godkann(employees[8].Id.Value, "Godkant");
        var friskvardKarl = RegionHR.Wellness.Domain.WellnessClaim.Skapa(
            employees[3].Id.Value, "Yoga", 1500m, DateOnly.FromDateTime(DateTime.Today.AddDays(-5)));
        var friskvardErik = RegionHR.Wellness.Domain.WellnessClaim.Skapa(
            employees[1].Id.Value, "Simkort arskort", 3500m, DateOnly.FromDateTime(DateTime.Today.AddDays(-30)));
        friskvardErik.Godkann(employees[8].Id.Value);
        db.WellnessClaims.AddRange(friskvardAnna, friskvardKarl, friskvardErik);

        // === Announcements via domänens Skapa() ===
        var annons1 = RegionHR.Communication.Domain.Announcement.Skapa(
            "Parkering under ombyggnation", "Parkeringshuset stängs 1-15 april för renovering. Använd alternativ parkering vid norra infarten.",
            RegionHR.Communication.Domain.AnnouncementPriority.Viktig, "Admin");
        annons1.Publicera();
        var annons2 = RegionHR.Communication.Domain.Announcement.Skapa(
            "Vaccinering influensa", "Årets influensavaccinering erbjuds 1-30 november. Boka tid via MinSida.",
            RegionHR.Communication.Domain.AnnouncementPriority.Normal, "Admin");
        annons2.Publicera();
        var annons3 = RegionHR.Communication.Domain.Announcement.Skapa(
            "IT-driftstopp planerat 5 april", "IT-system nedstängda 02:00-06:00 för uppgradering.",
            RegionHR.Communication.Domain.AnnouncementPriority.Kritisk, "Admin");
        db.Announcements.AddRange(annons1, annons2, annons3);

        // === InsuranceCoverages ===
        db.InsuranceCoverages.AddRange(
            RegionHR.Insurance.Domain.InsuranceCoverage.Skapa(RegionHR.Insurance.Domain.InsuranceType.TGL, "TGL — Tjänstegrupplivförsäkring", "KPA Pension", "Livförsäkring för alla anställda"),
            RegionHR.Insurance.Domain.InsuranceCoverage.Skapa(RegionHR.Insurance.Domain.InsuranceType.AGS, "AGS — Avtalsgruppsjukförsäkring", "AFA Försäkring", "Kompletterande sjukförsäkring"),
            RegionHR.Insurance.Domain.InsuranceCoverage.Skapa(RegionHR.Insurance.Domain.InsuranceType.TFA, "TFA — Trygghetsförsäkring vid arbetsskada", "AFA Försäkring", "Arbetsskadeförsäkring"),
            RegionHR.Insurance.Domain.InsuranceCoverage.Skapa(RegionHR.Insurance.Domain.InsuranceType.AFA, "AFA — Avtalsförsäkring", "AFA Försäkring", "Samlingsbegrepp"),
            RegionHR.Insurance.Domain.InsuranceCoverage.Skapa(RegionHR.Insurance.Domain.InsuranceType.PSA, "PSA — Avtal om ersättning vid personskada", "AFA Försäkring", "Personskadeförsäkring"));

        // === Recognitions ===
        db.Recognitions.AddRange(
            RegionHR.Communication.Domain.Recognition.Skapa(employees[0].Id.Value, employees[3].Id.Value, "Samarbete", "Karl hoppade in pa kort varsel och tacklade kvallspasset. Fantastiskt!"),
            RegionHR.Communication.Domain.Recognition.Skapa(employees[1].Id.Value, employees[6].Id.Value, "Innovation", "Helena foreslog nytt triageflode som kortat vantetiderna."),
            RegionHR.Communication.Domain.Recognition.Skapa(employees[4].Id.Value, employees[0].Id.Value, "Hjalpsamhet", "Anna tog sig tid att handleda en ny kollega trots hog belastning."));

        // === SuccessionPlans ===
        db.SuccessionPlans.AddRange(
            RegionHR.Positions.Domain.SuccessionPlan.Skapa(posVc.Id, employees[7].Id.Value, 2029, employees[0].Id.Value, RegionHR.Positions.Domain.SuccessionReadiness.RedoInom1Ar, 75),
            RegionHR.Positions.Domain.SuccessionPlan.Skapa(posLakAkut.Id, employees[1].Id.Value, 2032, null, RegionHR.Positions.Domain.SuccessionReadiness.EjIdentifierad, 0),
            RegionHR.Positions.Domain.SuccessionPlan.Skapa(posSsk32.Id, employees[0].Id.Value, 2035, employees[4].Id.Value, RegionHR.Positions.Domain.SuccessionReadiness.RedoInom2Ar, 40));

        // === FeedbackRound + Responses ===
        var fb360Anna = RegionHR.Performance.Domain.FeedbackRound.Skapa(employees[0].Id.Value, "360-feedback Anna Svensson 2026");
        fb360Anna.Oppna();
        fb360Anna.Stang();
        var fbResp1 = RegionHR.Performance.Domain.FeedbackResponse.Skapa(fb360Anna.Id, employees[7].Id.Value, "Chef", 4, "Bra samarbetsformaga");
        var fbResp2 = RegionHR.Performance.Domain.FeedbackResponse.Skapa(fb360Anna.Id, employees[3].Id.Value, "Kollega", 5, "Alltid hjalpsam");
        var fbResp3 = RegionHR.Performance.Domain.FeedbackResponse.Skapa(fb360Anna.Id, employees[4].Id.Value, "Kollega", 4);

        var fb360Erik = RegionHR.Performance.Domain.FeedbackRound.Skapa(employees[1].Id.Value, "360-feedback Erik Johansson 2026");
        fb360Erik.Oppna();
        db.FeedbackRounds.AddRange(fb360Anna, fb360Erik);
        db.FeedbackResponses.AddRange(fbResp1, fbResp2, fbResp3);

        // === LASAccumulations ===
        var lasMaria = RegionHR.LAS.Domain.LASAccumulation.Skapa(employees[2].Id, EmploymentType.SAVA);
        lasMaria.LaggTillPeriod(DateOnly.FromDateTime(DateTime.Today.AddDays(-200)), DateOnly.FromDateTime(DateTime.Today.AddDays(-50)), null);
        lasMaria.LaggTillPeriod(DateOnly.FromDateTime(DateTime.Today.AddDays(-40)), DateOnly.FromDateTime(DateTime.Today), null);
        var lasJohan = RegionHR.LAS.Domain.LASAccumulation.Skapa(employees[5].Id, EmploymentType.Vikariat);
        lasJohan.LaggTillPeriod(DateOnly.FromDateTime(DateTime.Today.AddDays(-320)), DateOnly.FromDateTime(DateTime.Today), null);
        db.LASAccumulations.AddRange(lasMaria, lasJohan);

        // === Vacancies ===
        var vakSsk = RegionHR.Recruitment.Domain.Vacancy.Skapa(
            avd32.Id, "Sjukskoterska", "Vi soker erfaren sjukskoterska till Avdelning 32.",
            EmploymentType.Tillsvidare, DateOnly.FromDateTime(DateTime.Today.AddDays(30)));
        vakSsk.Publicera(false, false);
        vakSsk.TaEmotAnsokan("Anna Bergman", "anna.bergman@mail.se", null);
        vakSsk.TaEmotAnsokan("Karl Lindqvist", "karl.l@mail.se", null);
        var vakUsk = RegionHR.Recruitment.Domain.Vacancy.Skapa(
            iva.Id, "Underskoterska IVA", "IVA soker underskoterska med intensivvardserfarenhet.",
            EmploymentType.Tillsvidare, DateOnly.FromDateTime(DateTime.Today.AddDays(14)));
        vakUsk.Publicera(false, false);
        var vakLak = RegionHR.Recruitment.Domain.Vacancy.Skapa(
            akuten.Id, "Lakare akutmottagningen", "Specialist i akutsjukvard.",
            EmploymentType.Tillsvidare, DateOnly.FromDateTime(DateTime.Today.AddDays(60)));
        db.Vacancies.AddRange(vakSsk, vakUsk, vakLak);

        // === MandatoryTrainings ===
        db.MandatoryTrainings.AddRange(
            RegionHR.Competence.Domain.MandatoryTraining.Skapa("Sjukskoterska", "HLR", 12, "Hjartstoppsutbildning"),
            RegionHR.Competence.Domain.MandatoryTraining.Skapa("Sjukskoterska", "Brandskydd", 24, "Grundutbildning brandsakerhet"),
            RegionHR.Competence.Domain.MandatoryTraining.Skapa("Alla", "Hygien", 12, "Basala hygienrutiner"),
            RegionHR.Competence.Domain.MandatoryTraining.Skapa("Underskoterska", "Forsta hjalpen", 24));

        // === ReferenceChecks ===
        var refCheck1 = RegionHR.Recruitment.Domain.ReferenceCheck.Skapa(vakSsk.Id, "Anna Bergman", "Lars Svensson", "Tidigare chef");
        refCheck1.Genomfor("Mycket positiva omdomen. Rekommenderas starkt.", true);
        var refCheck2 = RegionHR.Recruitment.Domain.ReferenceCheck.Skapa(vakSsk.Id, "Karl Lindqvist", "Eva Holm", "Handledare");
        db.ReferenceChecks.AddRange(refCheck1, refCheck2);

        // === MBL-förhandlingar ===
        var mbl1 = RegionHR.CaseManagement.Domain.MBLNegotiation.Skapa("Omorganisation Avdelning 32", RegionHR.CaseManagement.Domain.MBLType.Forhandling, DateOnly.FromDateTime(DateTime.Today.AddDays(-5)), "Kommunal — Anna Ek", "HR-chef Eva Nilsson");
        mbl1.Paborja();
        mbl1.Avsluta();
        mbl1.RegistreraProtokoll("Parterna enade. Omorganisation genomfors fran 1 maj 2026.");
        var mbl2 = RegionHR.CaseManagement.Domain.MBLNegotiation.Skapa("Nyanstallning verksamhetschef", RegionHR.CaseManagement.Domain.MBLType.Information, DateOnly.FromDateTime(DateTime.Today.AddDays(7)), "Vardforbundet — Karl Berg", "HR-chef Eva Nilsson");
        db.MBLNegotiations.AddRange(mbl1, mbl2);

        // === SalaryCodes via SalaryCodeSeed ===
        db.SalaryCodes.AddRange(RegionHR.Payroll.Domain.SalaryCodeSeed.GetAll());

        // === Automation Framework ===
        var catCompliance = RegionHR.Automation.Domain.AutomationCategory.Skapa("Compliance", "Lagefterlevnad och regelkrav (LAS, ATL, diskriminering)", "Gavel");
        var catFranvaro = RegionHR.Automation.Domain.AutomationCategory.Skapa("Franvaro", "Sjukfranvaro, VAB och frånvarohantering", "EventBusy");
        var catLon = RegionHR.Automation.Domain.AutomationCategory.Skapa("Lon", "Lonebearbetning och utbetalning", "Payments");
        var catKompetens = RegionHR.Automation.Domain.AutomationCategory.Skapa("Kompetens", "Certifieringar, utbildning och kompetensgap", "School");
        var catRekrytering = RegionHR.Automation.Domain.AutomationCategory.Skapa("Rekrytering", "Rekrytering, onboarding och offboarding", "PersonAdd");
        var catGDPR = RegionHR.Automation.Domain.AutomationCategory.Skapa("GDPR", "Dataskydd, gallring och registerutdrag", "Security");
        db.AutomationCategories.AddRange(catCompliance, catFranvaro, catLon, catKompetens, catRekrytering, catGDPR);

        // --- Automation Rules (22 regler) ---
        // Compliance (5)
        var rules = new List<RegionHR.Automation.Domain.AutomationRule>
        {
            RegionHR.Automation.Domain.AutomationRule.Skapa("LAS-varning 300 dagar", catCompliance.Id, "LASAccumulationUpdated", "{\"dagar_min\":300}", "{\"typ\":\"notify\",\"mall\":\"las_varning\"}", RegionHR.Automation.Domain.AutomationLevel.Notify, true),
            RegionHR.Automation.Domain.AutomationRule.Skapa("LAS konvertering 360 dagar", catCompliance.Id, "LASAccumulationUpdated", "{\"dagar_min\":360}", "{\"typ\":\"autopilot\",\"mall\":\"las_konvertering\"}", RegionHR.Automation.Domain.AutomationLevel.Notify, true),
            RegionHR.Automation.Domain.AutomationRule.Skapa("ATL max veckoarbetstid", catCompliance.Id, "ShiftCreated", "{\"max_timmar_vecka\":48}", "{\"typ\":\"block\"}", RegionHR.Automation.Domain.AutomationLevel.Block, true),
            RegionHR.Automation.Domain.AutomationRule.Skapa("ATL dygnsvila 11 timmar", catCompliance.Id, "ShiftCreated", "{\"min_vila_timmar\":11}", "{\"typ\":\"block\"}", RegionHR.Automation.Domain.AutomationLevel.Block, true),
            RegionHR.Automation.Domain.AutomationRule.Skapa("Diskrimineringskontroll loneoversyn", catCompliance.Id, "SalaryReviewCreated", "{\"avvikelse_procent\":5}", "{\"typ\":\"suggest\",\"mall\":\"diskriminering_varning\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),

            // Franvaro (4)
            RegionHR.Automation.Domain.AutomationRule.Skapa("FK-anmalan dag 15", catFranvaro.Id, "SickLeaveUpdated", "{\"dagar_min\":14}", "{\"typ\":\"notify\",\"mall\":\"fk_anmalan\"}", RegionHR.Automation.Domain.AutomationLevel.Notify, true),
            RegionHR.Automation.Domain.AutomationRule.Skapa("Rehab-trigger dag 30", catFranvaro.Id, "SickLeaveUpdated", "{\"dagar_min\":30}", "{\"typ\":\"suggest\",\"mall\":\"rehab_start\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),
            RegionHR.Automation.Domain.AutomationRule.Skapa("Sjukfranvaro-eskalering dag 90", catFranvaro.Id, "SickLeaveUpdated", "{\"dagar_min\":90}", "{\"typ\":\"notify\",\"mall\":\"eskalering_90\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),
            RegionHR.Automation.Domain.AutomationRule.Skapa("Korttidsfranvaro-monster", catFranvaro.Id, "CronDaily", "{\"max_tillfallen_6_man\":6}", "{\"typ\":\"suggest\",\"mall\":\"korttid_monster\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),

            // Lon (4)
            RegionHR.Automation.Domain.AutomationRule.Skapa("AGI-generering manadsslut", catLon.Id, "PayrollRunCompleted", "{}", "{\"typ\":\"autopilot\",\"mall\":\"agi_xml\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),
            RegionHR.Automation.Domain.AutomationRule.Skapa("Lonefil pain.001", catLon.Id, "PayrollRunApproved", "{}", "{\"typ\":\"autopilot\",\"mall\":\"pain001\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),
            RegionHR.Automation.Domain.AutomationRule.Skapa("Loneavvikelse-varning", catLon.Id, "PayrollCalculated", "{\"avvikelse_procent\":20}", "{\"typ\":\"suggest\",\"mall\":\"lon_avvikelse\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),
            RegionHR.Automation.Domain.AutomationRule.Skapa("OB-tillagg automatisk berakning", catLon.Id, "TimesheetApproved", "{}", "{\"typ\":\"autopilot\",\"mall\":\"ob_berakning\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),

            // Kompetens (3)
            RegionHR.Automation.Domain.AutomationRule.Skapa("Certifiering utgar 30 dagar", catKompetens.Id, "CronDaily", "{\"dagar_kvar\":30}", "{\"typ\":\"notify\",\"mall\":\"cert_utgar_30\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),
            RegionHR.Automation.Domain.AutomationRule.Skapa("Certifiering utgar 90 dagar", catKompetens.Id, "CronDaily", "{\"dagar_kvar\":90}", "{\"typ\":\"notify\",\"mall\":\"cert_utgar_90\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),
            RegionHR.Automation.Domain.AutomationRule.Skapa("Obligatorisk utbildning forsenad", catKompetens.Id, "CronWeekly", "{\"forsenad\":true}", "{\"typ\":\"suggest\",\"mall\":\"utbildning_paminnelse\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),

            // Rekrytering (3)
            RegionHR.Automation.Domain.AutomationRule.Skapa("Onboarding auto-start", catRekrytering.Id, "EmploymentCreated", "{}", "{\"typ\":\"autopilot\",\"mall\":\"onboarding_start\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),
            RegionHR.Automation.Domain.AutomationRule.Skapa("Offboarding auto-start", catRekrytering.Id, "EmploymentTerminated", "{}", "{\"typ\":\"autopilot\",\"mall\":\"offboarding_start\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),
            RegionHR.Automation.Domain.AutomationRule.Skapa("Provanstallning utgar", catRekrytering.Id, "CronDaily", "{\"dagar_kvar\":30}", "{\"typ\":\"notify\",\"mall\":\"provanstallning_utgar\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),

            // GDPR (3)
            RegionHR.Automation.Domain.AutomationRule.Skapa("Retention gallring 7 ar", catGDPR.Id, "CronMonthly", "{\"ar\":7}", "{\"typ\":\"autopilot\",\"mall\":\"gallring\"}", RegionHR.Automation.Domain.AutomationLevel.Autopilot, true),
            RegionHR.Automation.Domain.AutomationRule.Skapa("Registerutdrag auto-generering", catGDPR.Id, "DataSubjectRequestCreated", "{}", "{\"typ\":\"autopilot\",\"mall\":\"registerutdrag\"}", RegionHR.Automation.Domain.AutomationLevel.Autopilot, true),
            RegionHR.Automation.Domain.AutomationRule.Skapa("Samtycke utgangen", catGDPR.Id, "CronWeekly", "{\"dagar_kvar\":0}", "{\"typ\":\"notify\",\"mall\":\"samtycke_utgangen\"}", RegionHR.Automation.Domain.AutomationLevel.Notify),
        };
        db.AutomationRules.AddRange(rules);

        // --- Default LevelConfigs (6 st) ---
        db.AutomationLevelConfigs.AddRange(
            RegionHR.Automation.Domain.AutomationLevelConfig.Skapa(catCompliance.Id, RegionHR.Automation.Domain.AutomationLevel.Notify),
            RegionHR.Automation.Domain.AutomationLevelConfig.Skapa(catFranvaro.Id, RegionHR.Automation.Domain.AutomationLevel.Notify),
            RegionHR.Automation.Domain.AutomationLevelConfig.Skapa(catLon.Id, RegionHR.Automation.Domain.AutomationLevel.Notify),
            RegionHR.Automation.Domain.AutomationLevelConfig.Skapa(catKompetens.Id, RegionHR.Automation.Domain.AutomationLevel.Notify),
            RegionHR.Automation.Domain.AutomationLevelConfig.Skapa(catRekrytering.Id, RegionHR.Automation.Domain.AutomationLevel.Notify),
            RegionHR.Automation.Domain.AutomationLevelConfig.Skapa(catGDPR.Id, RegionHR.Automation.Domain.AutomationLevel.Autopilot));

        // === KPI Definitions (10 st) ===
        var kpiHeadcount = KPIDefinition.Skapa("Headcount", "Workforce", "COUNT(employees WHERE active=true)", "count", "HigherIsBetter", 100, 80, 50);
        var kpiFTE = KPIDefinition.Skapa("FTE (heltidsekvivalenter)", "Workforce", "SUM(employment_percent)/100", "count", "HigherIsBetter", 95, 85, 70);
        var kpiVacancy = KPIDefinition.Skapa("Vakansgrad", "Recruitment", "vacant_positions/total_positions*100", "percent", "LowerIsBetter", 5, 10, 15);
        var kpiTurnover = KPIDefinition.Skapa("Personalomsattning", "Turnover", "terminated_12m/avg_headcount*100", "percent", "LowerIsBetter", 8, 15, 20);
        var kpiSickLeave = KPIDefinition.Skapa("Sjukfranvaro %", "Absence", "sick_days/total_workdays*100", "percent", "LowerIsBetter", 4, 6, 8);
        var kpiSalaryCost = KPIDefinition.Skapa("Lonekostnad per FTE", "Compensation", "total_salary_cost/fte_count", "currency", "LowerIsBetter", 45000, 50000, 55000);
        var kpiCertCoverage = KPIDefinition.Skapa("Certifieringstackning", "Competence", "valid_certs/required_certs*100", "percent", "HigherIsBetter", 95, 85, 70);
        var kpiTimeToFill = KPIDefinition.Skapa("Time to fill (dagar)", "Recruitment", "AVG(fill_date - publish_date)", "days", "LowerIsBetter", 30, 45, 60);
        var kpiENPS = KPIDefinition.Skapa("eNPS", "Engagement", "promoters_pct - detractors_pct", "count", "HigherIsBetter", 30, 10, -10);
        var kpiLASRisk = KPIDefinition.Skapa("LAS-riskantal", "Compliance", "COUNT(las_days >= 300)", "count", "LowerIsBetter", 0, 3, 5);
        db.KPIDefinitions.AddRange(kpiHeadcount, kpiFTE, kpiVacancy, kpiTurnover, kpiSickLeave, kpiSalaryCost, kpiCertCoverage, kpiTimeToFill, kpiENPS, kpiLASRisk);

        // === KPI Snapshots (current period: 2026-Q1) ===
        db.KPISnapshots.AddRange(
            KPISnapshot.Skapa(kpiHeadcount.Id, "2026-Q1", 10, 10, "Stable"),
            KPISnapshot.Skapa(kpiFTE.Id, "2026-Q1", 9.2m, 9.0m, "Up"),
            KPISnapshot.Skapa(kpiVacancy.Id, "2026-Q1", 7.5m, 8.0m, "Down"),
            KPISnapshot.Skapa(kpiTurnover.Id, "2026-Q1", 0, null, "Stable"),
            KPISnapshot.Skapa(kpiSickLeave.Id, "2026-Q1", 4.2m, 4.5m, "Down"),
            KPISnapshot.Skapa(kpiSalaryCost.Id, "2026-Q1", 42500, 41000, "Up"),
            KPISnapshot.Skapa(kpiCertCoverage.Id, "2026-Q1", 88, 85, "Up"),
            KPISnapshot.Skapa(kpiTimeToFill.Id, "2026-Q1", 0, null, "Stable"),
            KPISnapshot.Skapa(kpiENPS.Id, "2026-Q1", 0, null, "Stable"),
            KPISnapshot.Skapa(kpiLASRisk.Id, "2026-Q1", 2, 1, "Up"));

        // === Prediction Models (4 st) ===
        var pmAttrition = PredictionModel.Skapa("Uppsagningsriskmodell", "Attrition", """{"features":["tenure","age","salary_delta","sick_days","overtime_hours"]}""");
        pmAttrition.UppdateraTranning(0.82m);
        var pmHeadcount = PredictionModel.Skapa("Bemanningsprognos", "HeadcountForecast", """{"features":["headcount_history","turnover_rate","planned_recruitment"]}""");
        pmHeadcount.UppdateraTranning(0.91m);
        var pmSickLeave = PredictionModel.Skapa("Sjukfranvaroprognos", "SickLeaveForecast", """{"features":["season","sick_history","workload","age"]}""");
        pmSickLeave.UppdateraTranning(0.78m);
        var pmLaborCost = PredictionModel.Skapa("Lonekostnadsprognos", "LaborCostForecast", """{"features":["salary_base","ob_tillagg","overtime","employer_fees","pension"]}""");
        pmLaborCost.UppdateraTranning(0.95m);
        db.PredictionModels.AddRange(pmAttrition, pmHeadcount, pmSickLeave, pmLaborCost);

        // === VMS / Contingent Workforce ===
        var vendor1 = RegionHR.VMS.Domain.Vendor.Skapa("MedStaff AB", "556789-1234", "Anna Lind", "anna@medstaff.se", "031-111222", "Sjukvard");
        var vendor2 = RegionHR.VMS.Domain.Vendor.Skapa("VardPool Sverige", "556790-5678", "Erik Johansson", "erik@vardpool.se", "08-333444", "Sjukvard");
        var vendor3 = RegionHR.VMS.Domain.Vendor.Skapa("BemanningsExpert", "556791-9012", "Lisa Berg", "lisa@bemanningsexpert.se", "040-555666", "IT");
        db.Vendors.AddRange(vendor1, vendor2, vendor3);

        var ramavtal1 = RegionHR.VMS.Domain.FrameworkAgreement.Skapa(
            vendor1.Id, new DateOnly(2025, 1, 1), new DateOnly(2027, 12, 31),
            "Standardvillkor for bemanningssjukskoterskor", 3, "Automatisk forlangning 12 manader", 5_000_000m);
        ramavtal1.LaggTillRateCard("Sjukskoterska", 520m, 85m, 195m, 25m);
        ramavtal1.LaggTillRateCard("Underskoterska", 380m, 65m, 145m, 25m);

        var ramavtal2 = RegionHR.VMS.Domain.FrameworkAgreement.Skapa(
            vendor3.Id, new DateOnly(2025, 6, 1), new DateOnly(2026, 12, 31),
            "IT-konsulter systemutveckling", 2, "Ingen automatisk forlangning", 2_000_000m);
        ramavtal2.LaggTillRateCard("Systemutvecklare", 950m, 0m, 0m, 25m);
        ramavtal2.LaggTillRateCard("Projektledare", 1100m, 0m, 0m, 25m);
        db.FrameworkAgreements.AddRange(ramavtal1, ramavtal2);

        var vmsRequest = RegionHR.VMS.Domain.StaffingRequest.Skapa(
            avd32.Id, "Sjukskoterska", new DateOnly(2026, 1, 15), new DateOnly(2026, 6, 30), 2,
            "Minst 3 ars erfarenhet inom akutsjukvard");
        vmsRequest.SkickaIn();
        vmsRequest.Godkann();
        vmsRequest.Tillsatt();
        db.StaffingRequests.Add(vmsRequest);

        var cw1 = RegionHR.VMS.Domain.ContingentWorker.Skapa(
            "Maria Lindqvist", vendor1.Id, vmsRequest.Id, new DateOnly(2026, 1, 20), new DateOnly(2026, 6, 30), 520m, avd32.Id);
        var cw2 = RegionHR.VMS.Domain.ContingentWorker.Skapa(
            "Johan Pettersson", vendor1.Id, vmsRequest.Id, new DateOnly(2026, 2, 1), null, 520m, avd32.Id);
        db.ContingentWorkers.AddRange(cw1, cw2);

        var tr1 = RegionHR.VMS.Domain.ContingentTimeReport.Skapa(cw1.Id, "2026-02", 152m, 8m, 4m);
        tr1.SkickaIn();
        tr1.Attestera(Guid.NewGuid());
        var tr2 = RegionHR.VMS.Domain.ContingentTimeReport.Skapa(cw2.Id, "2026-02", 160m, 0m, 0m);
        tr2.SkickaIn();
        var tr3 = RegionHR.VMS.Domain.ContingentTimeReport.Skapa(cw1.Id, "2026-03", 168m, 12m, 8m);
        db.ContingentTimeReports.AddRange(tr1, tr2, tr3);

        var inv1 = RegionHR.VMS.Domain.VendorInvoice.Skapa(vendor1.Id, "2026-02", 168_480m);
        inv1.Matcha(165_100m);
        inv1.Godkann();
        var inv2 = RegionHR.VMS.Domain.VendorInvoice.Skapa(vendor1.Id, "2026-03", 183_040m);
        db.VendorInvoices.AddRange(inv1, inv2);

        db.VendorPerformances.AddRange(
            RegionHR.VMS.Domain.VendorPerformance.Skapa(vendor1.Id, "2026-Q1", 4, "Bra leverans, punktliga rapporter"),
            RegionHR.VMS.Domain.VendorPerformance.Skapa(vendor2.Id, "2026-Q1", 3, "Acceptabelt men forlangda ledtider"),
            RegionHR.VMS.Domain.VendorPerformance.Skapa(vendor3.Id, "2026-Q1", 5, "Utmarkt kvalitet, proaktiv kommunikation"));

        db.SpendCategories.AddRange(
            RegionHR.VMS.Domain.SpendCategory.Skapa("Bemanningssjukskoterska", "Inhyrda sjukskoterskor via bemanningsforetag"),
            RegionHR.VMS.Domain.SpendCategory.Skapa("IT-konsult", "Externt inhyrda IT-resurser"),
            RegionHR.VMS.Domain.SpendCategory.Skapa("Administrativ", "Administrativ stodpersonal via bemanning"));

        // === F-skatt registreringar ===
        var fskatt1 = RegionHR.VMS.Domain.FSkattRegistration.Skapa("556789-1234", RegionHR.VMS.Domain.FSkattStatus.Active, new DateOnly(2027, 6, 30), vendorId: vendor1.Id);
        var fskatt2 = RegionHR.VMS.Domain.FSkattRegistration.Skapa("556790-5678", RegionHR.VMS.Domain.FSkattStatus.Active, new DateOnly(2026, 4, 15), vendorId: vendor2.Id); // Gar ut snart
        var fskatt3 = RegionHR.VMS.Domain.FSkattRegistration.Skapa("556791-9012", RegionHR.VMS.Domain.FSkattStatus.Inactive, null, vendorId: vendor3.Id); // Inaktiv — kraver skatteavdrag
        db.FSkattRegistrations.AddRange(fskatt1, fskatt2, fskatt3);

        // === Contractor Classifications ===
        var cc1 = RegionHR.VMS.Domain.ContractorClassification.Bedöm(cw1.Id, "Contractor", "Low",
            """{"control":0,"tools":0,"economic_dependency":1,"integration":0,"duration":1}""", "HR-specialisten");
        var cc2 = RegionHR.VMS.Domain.ContractorClassification.Bedöm(cw2.Id, "Unclear", "Medium",
            """{"control":1,"tools":1,"economic_dependency":2,"integration":1,"duration":1}""", "HR-specialisten");
        db.ContractorClassifications.AddRange(cc1, cc2);

        // === Planning Scenarios ===
        var scenario1 = RegionHR.Analytics.Domain.PlanningScenario.Skapa("Basscenario 2026", "Nuvarande trender utan forandringar", DateTime.Today.Year, "System");
        scenario1.Antaganden.Add(RegionHR.Analytics.Domain.ScenarioAssumption.Skapa(scenario1.Id, "AttritionRate", 8m, "Historisk personalomsattning 8%"));
        scenario1.Antaganden.Add(RegionHR.Analytics.Domain.ScenarioAssumption.Skapa(scenario1.Id, "SalaryIncrease", 3.2m, "Avtalsenlig loneokning 3.2%"));
        db.PlanningScenarios.Add(scenario1);

        var scenario2 = RegionHR.Analytics.Domain.PlanningScenario.Skapa("Expansionsscenario", "Okad bemanning for ny avdelning", DateTime.Today.Year, "System");
        scenario2.Antaganden.Add(RegionHR.Analytics.Domain.ScenarioAssumption.Skapa(scenario2.Id, "NewHires", 2m, "2 nyanstallningar per manad"));
        scenario2.Antaganden.Add(RegionHR.Analytics.Domain.ScenarioAssumption.Skapa(scenario2.Id, "AttritionRate", 6m, "Lagre omsattning vid expansion"));
        scenario2.Antaganden.Add(RegionHR.Analytics.Domain.ScenarioAssumption.Skapa(scenario2.Id, "SalaryIncrease", 3.2m, "Avtalsenlig loneokning"));
        db.PlanningScenarios.Add(scenario2);

        var scenario3 = RegionHR.Analytics.Domain.PlanningScenario.Skapa("Besparingsscenario", "Anstallningsstopp och kostnadsminskning", DateTime.Today.Year, "System");
        scenario3.Antaganden.Add(RegionHR.Analytics.Domain.ScenarioAssumption.Skapa(scenario3.Id, "FreezeHiring", 1m, "Total anstallningsstopp"));
        scenario3.Antaganden.Add(RegionHR.Analytics.Domain.ScenarioAssumption.Skapa(scenario3.Id, "AttritionRate", 12m, "Hogre omsattning vid besparingar"));
        db.PlanningScenarios.Add(scenario3);

        // === WFM: DemandPatterns for Akuten (5 st — vardagar, helger) ===
        db.DemandPatterns.AddRange(
            new RegionHR.Scheduling.Domain.DemandPattern { EnhetId = akuten.Id, Veckodag = 1, GenomsnittligBelastning = 12, SasongsVariation = 1.0m }, // Mån
            new RegionHR.Scheduling.Domain.DemandPattern { EnhetId = akuten.Id, Veckodag = 2, GenomsnittligBelastning = 11, SasongsVariation = 1.0m }, // Tis
            new RegionHR.Scheduling.Domain.DemandPattern { EnhetId = akuten.Id, Veckodag = 5, GenomsnittligBelastning = 14, SasongsVariation = 1.1m }, // Fre
            new RegionHR.Scheduling.Domain.DemandPattern { EnhetId = akuten.Id, Veckodag = 6, GenomsnittligBelastning = 16, SasongsVariation = 1.2m }, // Lör
            new RegionHR.Scheduling.Domain.DemandPattern { EnhetId = akuten.Id, Veckodag = 0, GenomsnittligBelastning = 15, SasongsVariation = 1.15m }); // Sön

        // === WFM: DemandEvents (3 st) ===
        db.DemandEvents.AddRange(
            new RegionHR.Scheduling.Domain.DemandEvent { Namn = "Jul 2026", Typ = "Helgdag", PaverkanGrad = 1.6m, DatumFran = new DateOnly(2026, 12, 23), DatumTill = new DateOnly(2026, 12, 26) },
            new RegionHR.Scheduling.Domain.DemandEvent { Namn = "Midsommar 2026", Typ = "Helgdag", PaverkanGrad = 1.4m, DatumFran = new DateOnly(2026, 6, 19), DatumTill = new DateOnly(2026, 6, 21) },
            new RegionHR.Scheduling.Domain.DemandEvent { Namn = "Influensasasong 2026/2027", Typ = "Influensasasong", PaverkanGrad = 1.35m, DatumFran = new DateOnly(2026, 11, 1), DatumTill = new DateOnly(2027, 2, 28) });

        // === WFM: SchedulingConstraints (5 st) ===
        db.SchedulingConstraints.AddRange(
            new RegionHR.Scheduling.Domain.SchedulingConstraint { Typ = "ATL", Beskrivning = "ATL §13: Minst 11h sammanhangande dygnsvila", Vikt = 100, ArHard = true },
            new RegionHR.Scheduling.Domain.SchedulingConstraint { Typ = "ATL", Beskrivning = "ATL §14: Minst 36h sammanhangande veckovila", Vikt = 100, ArHard = true },
            new RegionHR.Scheduling.Domain.SchedulingConstraint { Typ = "Kompetens", Beskrivning = "Ratt kompetens pa varje pass (legitimation, HLR etc.)", Vikt = 95, ArHard = true },
            new RegionHR.Scheduling.Domain.SchedulingConstraint { Typ = "Preferens", Beskrivning = "Medarbetarpreferenser for pass och tider", Vikt = 30, ArHard = false },
            new RegionHR.Scheduling.Domain.SchedulingConstraint { Typ = "Kostnad", Beskrivning = "Minimera OB- och overtidskostnader", Vikt = 40, ArHard = false });

        // === WFM: EmployeeAvailability (4 st) ===
        db.EmployeeAvailabilities.AddRange(
            new RegionHR.Scheduling.Domain.EmployeeAvailability { AnstallId = employees[0].Id, Veckodag = 1, TidFran = new TimeOnly(7, 0), TidTill = new TimeOnly(16, 0), Preferens = "VillJobba", ArRepeterande = true },
            new RegionHR.Scheduling.Domain.EmployeeAvailability { AnstallId = employees[0].Id, Veckodag = 6, Preferens = "KanInte", ArRepeterande = true },
            new RegionHR.Scheduling.Domain.EmployeeAvailability { AnstallId = employees[2].Id, Veckodag = 5, TidFran = new TimeOnly(15, 0), TidTill = new TimeOnly(22, 0), Preferens = "KanJobba", ArRepeterande = true },
            new RegionHR.Scheduling.Domain.EmployeeAvailability { AnstallId = employees[3].Id, Datum = new DateOnly(2026, 4, 10), Preferens = "KanInte", ArRepeterande = false });

        // === WFM: FatigueScores (2 st for seeded employees) ===
        db.FatigueScores.AddRange(
            RegionHR.Scheduling.Domain.FatigueScore.Berakna(employees[1].Id, konsekutivaDagar: 5, nattpassSenaste7Dagar: 2, totalTimmarSenaste7Dagar: 42.5m, kortVila: 1, helgarbeteSenaste4Veckor: 3),
            RegionHR.Scheduling.Domain.FatigueScore.Berakna(employees[3].Id, konsekutivaDagar: 3, nattpassSenaste7Dagar: 1, totalTimmarSenaste7Dagar: 35.0m, kortVila: 0, helgarbeteSenaste4Veckor: 1));

        // === Custom Objects (platform schema) ===
        var utrustning = CustomObject.Skapa(
            "Utrustning", "Utrustningar", "Register over utrustning tilldelad personal",
            """[{"name":"Typ","type":"Dropdown","required":true,"options":["Dator","Telefon","Passerkort"]},{"name":"Serienummer","type":"Text","required":true,"options":null},{"name":"Tilldelad","type":"Text","required":false,"options":null},{"name":"Utlamnad","type":"Date","required":false,"options":null}]""",
            """[{"entityType":"Employee","relationType":"OneToMany"}]""",
            "Devices");
        db.CustomObjects.Add(utrustning);

        // 2 CustomObjectRecords for Utrustning
        var record1 = CustomObjectRecord.Skapa(utrustning.Id,
            """{"Typ":"Dator","Serienummer":"SN-2026-00142","Tilldelad":"Anna Svensson","Utlamnad":"2026-01-15"}""",
            "System");
        var record2 = CustomObjectRecord.Skapa(utrustning.Id,
            """{"Typ":"Passerkort","Serienummer":"PK-2026-00087","Tilldelad":"Erik Johansson","Utlamnad":"2026-02-01"}""",
            "System");
        db.CustomObjectRecords.AddRange(record1, record2);

        // === WorkflowNodes for existing Franvaro workflow ===
        // Find the Franvaro workflow definition (seeded via Configuration)
        var franvaroWf = await db.WorkflowDefinitions
            .FirstOrDefaultAsync(w => w.TargetEntityType == "Franvaro");

        if (franvaroWf != null)
        {
            db.WorkflowNodes.AddRange(
                WorkflowNode.Skapa(franvaroWf.Id, 1, WorkflowNodeTyp.Approval, "Inskickat",
                    """{"godkannareRoll":"Anstalld","beskrivning":"Den anstallde skickar in franvaroarende"}"""),
                WorkflowNode.Skapa(franvaroWf.Id, 2, WorkflowNodeTyp.Approval, "Chefsgodkannande",
                    """{"godkannareRoll":"Chef","beskrivning":"Narmaste chef godkanner eller avvisar"}"""),
                WorkflowNode.Skapa(franvaroWf.Id, 3, WorkflowNodeTyp.FieldUpdate, "Verkstallande",
                    """{"godkannareRoll":"System","beskrivning":"Systemet verkstaller beslutet automatiskt"}"""));
        }

        // === Platform — Sample webhook subscriptions (inactive/paused) ===
        if (!await db.EventSubscriptions.AnyAsync())
        {
            var sub1 = RegionHR.Platform.Domain.EventSubscription.Skapa(
                "HR Analytics Webhook",
                "https://analytics.example.com/webhook/openhr",
                "demo-secret-key-1",
                """["employee.created","employee.updated"]""");
            sub1.Pausa();

            var sub2 = RegionHR.Platform.Domain.EventSubscription.Skapa(
                "Extern Loneystem",
                "https://payroll.example.com/api/events",
                "demo-secret-key-2",
                """["payroll.run.completed","payroll.run.created"]""");
            sub2.Pausa();

            db.EventSubscriptions.AddRange(sub1, sub2);
        }

        // ================================================================
        // Knowledge Base — Categories, Articles, AssistantActions
        // ================================================================
        if (!await db.KnowledgeCategories.AnyAsync())
        {
            var catSemester = KnowledgeCategory.Skapa("Semester/Ledighet", "Information om semester, ledighet och frånvaro", 1, "BeachAccess");
            var kbCatLon = KnowledgeCategory.Skapa("Lön/Förmåner", "Allt om lön, lönespecifikationer och förmåner", 2, "AccountBalance");
            var catSchema = KnowledgeCategory.Skapa("Schema/Tid", "Schema, arbetstider, övertid och OB-tillägg", 3, "Schedule");
            var catAnstallning = KnowledgeCategory.Skapa("Anställning", "Anställningsvillkor, uppsägning och LAS", 4, "Work");
            var catRehab = KnowledgeCategory.Skapa("Rehab/Hälsa", "Rehabilitering, sjukskrivning och arbetsmiljö", 5, "HealthAndSafety");
            var kbCatGDPR = KnowledgeCategory.Skapa("GDPR", "Dataskydd och personuppgiftshantering", 6, "Security");
            var catArbetsmiljo = KnowledgeCategory.Skapa("Arbetsmiljö", "Systematiskt arbetsmiljöarbete och skyddsronder", 7, "Shield");
            var catPolicies = KnowledgeCategory.Skapa("Policies", "Regionens policyer och riktlinjer", 8, "Policy");
            var kbCatRekrytering = KnowledgeCategory.Skapa("Rekrytering", "Rekrytering, introduktion och anställning", 9, "PersonAdd");
            var catIT = KnowledgeCategory.Skapa("IT/System", "IT-stöd, system och digitala verktyg", 10, "Computer");

            db.KnowledgeCategories.AddRange(catSemester, kbCatLon, catSchema, catAnstallning, catRehab,
                kbCatGDPR, catArbetsmiljo, catPolicies, kbCatRekrytering, catIT);

            // --- Articles ---

            var art1 = KnowledgeArticle.Skapa(
                "Hur många semesterdagar har jag rätt till?",
                @"## Semesterrätt enligt lag och kollektivavtal

Enligt **Semesterlagen (SFS 1977:480)** har alla anställda rätt till **25 semesterdagar** per år. Kollektivavtalet AB ger dig fler dagar beroende på ålder:

- **Under 40 år**: 25 semesterdagar
- **40–49 år**: 31 semesterdagar
- **Från 50 år**: 32 semesterdagar

### Intjänande och uttag
Semesteråret löper 1 april – 31 mars. Semester intjänas under intjänandeåret (föregående period) och tas ut under semesteråret.

### Obetald semester
Om du inte hunnit tjäna in semester kan du ansöka om obetald semester. Du har alltid rätt till **minst 25 dagars ledighet** per år, men inte nödvändigtvis med lön.

### Sparade semesterdagar
Du kan **spara upp till 5 semesterdagar per år** i max 5 år. Sparade dagar över 25 kan alltid sparas.",
                catSemester.Id, ["semester", "semesterdagar", "ledighet", "semesterlagen", "AB"]);
            art1.Publicera();

            var art2 = KnowledgeArticle.Skapa(
                "Hur sjukanmäler jag mig?",
                @"## Sjukanmälan — steg för steg

### Dag 1 — Sjukanmälan
1. Meddela din chef **före arbetspassets start**
2. Sjukanmäl dig i OpenHR under *Min sida → Sjukanmälan*
3. **Karensavdrag** görs: 20% av din genomsnittliga veckolön

### Dag 2–14 — Sjuklön
- Du får **80% sjuklön** från arbetsgivaren
- Sjuklönen beräknas på din ordinarie lön

### Dag 8 — Läkarintyg
- Från dag 8 **krävs läkarintyg**
- Lämna in till din chef eller via OpenHR

### Dag 15 — Försäkringskassan
- Arbetsgivaren gör en **anmälan till Försäkringskassan**
- Du ansöker om sjukpenning hos FK
- FK betalar ca **80% av din SGI** (max 8 prisbasbelopp)

### Högriskskydd
Om du har en sjukdom som leder till många korta sjukperioder kan du ansöka om *högriskskydd* hos Försäkringskassan, vilket innebär att karensavdraget ersätts.",
                catRehab.Id, ["sjukanmälan", "sjuk", "karens", "läkarintyg", "sjuklön", "försäkringskassan"]);
            art2.Publicera();

            var art3 = KnowledgeArticle.Skapa(
                "Vad är OB-tillägg?",
                @"## OB-tillägg (Obekväm arbetstid)

OB-tillägg är extra ersättning för arbete på obekväma tider. Enligt **kollektivavtal AB 2025** gäller:

### Belopp per timme
| Tid | Belopp |
|-----|--------|
| Vardag kväll (19–22) | 126,50 kr/h |
| Vardag natt (22–06) | 152,00 kr/h |
| Helg (fre 19 – sön 22) | 89,00 kr/h |
| Storhelg (julafton, nyår etc) | 195,00 kr/h |

### Beräkning
OB beräknas automatiskt utifrån ditt schema i OpenHR. Du ser OB-tillägget specificerat på din lönespecifikation.

### Storhelger
Med storhelger avses bl.a. julafton, juldagen, annandag jul, nyårsafton, nyårsdagen, påskafton, påskdagen, annandag påsk, Kristi himmelsfärdsdag och midsommarafton.",
                catSchema.Id, ["OB", "obekväm", "tillägg", "kväll", "natt", "helg", "storhelg"]);
            art3.Publicera();

            var art4 = KnowledgeArticle.Skapa(
                "Hur fungerar LAS?",
                @"## LAS — Lagen om anställningsskydd

**LAS (SFS 1982:80)** reglerar anställningstrygghet i Sverige.

### Anställningsformer
- **Tillsvidareanställning** (fast anställning) — huvudregel
- **Särskild visstidsanställning (SAVA)** — tidsbegränsad
- **Vikariat** — tidsbegränsad

### Konverteringsregler (från 1 oktober 2022)
En anställd med **särskild visstidsanställning** konverteras automatiskt till tillsvidare efter:
- **12 månader** (365 dagar) under en **femårsperiod**

Vikariat konverteras efter:
- **24 månader** under en femårsperiod

### Turordning vid uppsägning
Vid uppsägning pga arbetsbrist gäller turordning: **sist in, först ut** inom turordningskretsen. Arbetsgivaren får undanta **3 personer** oavsett turordning.

### Företrädesrätt
Uppsagda anställda med minst 12 månaders anställning de senaste 3 åren har **företrädesrätt till återanställning** i 9 månader.

### LAS i OpenHR
Under *LAS-uppföljning* kan HR se ackumulerade dagar och konverteringsdatum för alla tidsbegränsat anställda.",
                catAnstallning.Id, ["LAS", "anställningsskydd", "konvertering", "visstid", "vikariat", "turordning", "företrädesrätt"]);
            art4.Publicera();

            var art5 = KnowledgeArticle.Skapa(
                "Vad är friskvårdsbidrag?",
                @"## Friskvårdsbidrag

Som anställd i regionen har du rätt till ett **skattefritt friskvårdsbidrag** på upp till **5 000 kr per år**.

### Vad kan jag använda det till?
- Gymkort och träningspass
- Simhall och bad
- Yoga, pilates, qigong
- Massage (ej medicinsk)
- Danslektioner
- Golfavgifter (ej utrustning)
- Skidkort (ej utrustning)

### Vad gäller **inte**?
- Utrustning (skor, kläder)
- Tävlingsavgifter
- Medicinsk behandling

### Hur ansöker jag?
1. Betala aktiviteten själv
2. Fotografera kvittot
3. Gå till *Förmåner → Friskvårdsbidrag* i OpenHR
4. Ladda upp kvitto och fyll i detaljer
5. Beloppet betalas ut med nästa lön

### Skattefritt
Friskvårdsbidraget är **skattefritt** upp till 5 000 kr/år. Belopp därutöver förmånsbeskattas.",
                kbCatLon.Id, ["friskvård", "friskvårdsbidrag", "gym", "träning", "bidrag"]);
            art5.Publicera();

            var art6 = KnowledgeArticle.Skapa(
                "Hur ansöker jag om föräldraledighet?",
                @"## Föräldraledighet

Enligt **Föräldraledighetslagen** har du rätt till ledighet för att ta hand om ditt barn.

### Antal dagar
- **480 dagar** per barn (240 per förälder)
- **390 dagar** betalas på sjukpenningnivå (ca 80% av SGI)
- **90 dagar** betalas på lägstanivå (180 kr/dag)
- **90 dagar** är reserverade för varje förälder (kan ej överlåtas)

### Föräldralön (regionens tillägg)
Utöver Försäkringskassans ersättning betalar regionen **föräldralön**:
- **10% av lönen** i upp till 180 dagar (enligt AB)

### Ansökan
1. Ansök hos Försäkringskassan om föräldrapenning
2. Anmäl ledigheten i OpenHR under *Ledighet → Föräldraledighet*
3. Meddela din chef **minst 2 månader** i förväg

### Arbetsgivarens skyldigheter
- Få inte missgynna dig pga föräldraledighet
- Din tjänst ska finnas kvar vid återkomst
- Löneutveckling ska vara jämförbar med kollegor",
                catSemester.Id, ["föräldraledighet", "föräldralön", "föräldrapenning", "barn", "mammaled", "pappaled"]);
            art6.Publicera();

            var art7 = KnowledgeArticle.Skapa(
                "Vad gäller vid tjänsteresor?",
                @"## Tjänsteresor och traktamente

### Traktamente
Vid tjänsteresa med övernattning minst 50 km från den vanliga verksamhetsorten:
- **Heldagstraktamente**: 280 kr/dag (inrikes, 2026)
- **Halvdagstraktamente**: 140 kr/dag (avresedag/hemresedag)
- Utökat traktamente vid utebliven frukost/lunch/middag

### Resekostnader
- **Tåg och flyg**: bokad via regionens resebyrå
- **Bilersättning**: 25 kr/mil (egen bil)
- **Kollektivtrafik**: full ersättning

### Utlägg
1. Betala själv och spara kvitto
2. Registrera i OpenHR under *Resor & utlägg*
3. Bifoga kvitton (foto eller PDF)
4. Utlägget betalas med nästa lön efter chefens godkännande

### Policy
- Boka **lägsta rimliga resklass**
- Tåg prioriteras över flyg för resor under 4h
- Hotell bokas till regionens avtalade priser",
                catPolicies.Id, ["tjänsteresa", "resa", "traktamente", "utlägg", "bilersättning"]);
            art7.Publicera();

            var art8 = KnowledgeArticle.Skapa(
                "Hur fungerar löneöversynen?",
                @"## Löneöversyn

Löneöversynen sker **årligen** och styrs av det centrala löneavtalet.

### Process
1. **Centralt avtal** bestämmer utrymmet (vanligtvis ~3% av lönesumman)
2. **Lönesamtal** mellan chef och medarbetare
3. **Ny lön** fastställs utifrån prestation, ansvar och marknadsvärde
4. **Retroaktiv utbetalning** från avtalets startdatum

### Bedömningskriterier
- **Resultat och måluppfyllelse**
- **Kompetens och utveckling**
- **Samarbetsförmåga**
- **Ledarskap** (för chefer)

### Lönekartläggning
Enligt diskrimineringslagen genomför regionen årlig lönekartläggning för att säkerställa att det inte finns osakliga löneskillnader.

### I OpenHR
Under *Löneöversyn* kan chefer hantera löneförslag och HR kan följa processen och göra lönekartläggning.",
                kbCatLon.Id, ["löneöversyn", "lön", "lönesamtal", "lönekartläggning", "löneavtal"]);
            art8.Publicera();

            var art9 = KnowledgeArticle.Skapa(
                "Vad är VAB (vård av barn)?",
                @"## VAB — Vård av barn

### Rätt till VAB
- Gäller till barnet fyller **12 år** (16 år vid allvarlig sjukdom)
- Max **120 dagar per barn och år**
- Gäller vid barns sjukdom, smitta eller vård

### Anmälan
1. Anmäl VAB i OpenHR under *Ledighet → VAB*
2. Anmäl till Försäkringskassan **samma dag** (via app eller webb)

### Ersättning
- FK betalar ca **80% av SGI** (max 8 prisbasbelopp)
- Arbetsgivaren betalar **ingen lön** under VAB
- Ingen karensdag vid VAB

### Tillfällig föräldrapenning
VAB-ersättningen kallas formellt *tillfällig föräldrapenning*. Den kan överlåtas till annan person som vårdar barnet.",
                catSemester.Id, ["VAB", "vård av barn", "barn sjuk", "tillfällig föräldrapenning"]);
            art9.Publicera();

            var art10 = KnowledgeArticle.Skapa(
                "Hur fungerar pensionen (AKAP-KR)?",
                @"## Tjänstepension — AKAP-KR

Som anställd i regionen omfattas du av pensionsavtalet **AKAP-KR** (Avtalad Kollektiv Avgiftsbestämd Pension för Kommun och Region).

### Premie
| Lönedel | Premie |
|---------|--------|
| Under 7,5 inkomstbasbelopp (IBB) | **6%** |
| Över 7,5 IBB | **31,5%** |

IBB 2026: 80 600 kr → 7,5 IBB = 604 500 kr/år (50 375 kr/mån)

### Valbar del
Du kan själv välja hur en del av premien ska placeras:
- **Traditionell försäkring** (garanterad ränta)
- **Fondförsäkring** (du väljer fonder)

### Löneväxling
Du kan **löneväxla** bruttolön till extra pensionsavsättning. Kontakta HR för mer information.

### Delpension
Anställda över **61 år** kan ansöka om delpension (arbetstidsminskning med viss kompensation).",
                kbCatLon.Id, ["pension", "AKAP-KR", "tjänstepension", "premie", "löneväxling", "delpension"]);
            art10.Publicera();

            var art11 = KnowledgeArticle.Skapa(
                "Vilka rättigheter har jag enligt GDPR?",
                @"## GDPR — Dina rättigheter

Enligt **Dataskyddsförordningen (GDPR)** har du som anställd flera rättigheter gällande dina personuppgifter.

### Registerutdrag
Du har rätt att få veta vilka personuppgifter regionen har om dig. Begär registerutdrag via *GDPR → Registerutdrag* i OpenHR.

### Rättelse
Om dina uppgifter är felaktiga har du rätt att få dem rättade.

### Radering
Du har i vissa fall rätt att få dina uppgifter raderade (""rätten att bli glömd""). Dock gäller undantag för uppgifter som krävs enligt lag (t.ex. bokföringslagen).

### Dataportabilitet
Du kan begära att få dina uppgifter i ett maskinläsbart format.

### Personuppgiftsansvarig
Region Västra Götaland är personuppgiftsansvarig. Kontakta dataskyddsombudet vid frågor: dso@regionvg.se

### Laglig grund
Regionen behandlar dina uppgifter med stöd av:
- **Anställningsavtal** (nödvändigt för avtalets fullgörande)
- **Rättslig förpliktelse** (skattelagstiftning, LAS etc.)",
                kbCatGDPR.Id, ["GDPR", "dataskydd", "personuppgifter", "registerutdrag", "radering", "rättelse"]);
            art11.Publicera();

            var art12 = KnowledgeArticle.Skapa(
                "Hur fungerar övertid?",
                @"## Övertidsregler

### Arbetstidslagen (ATL)
- Max **200 timmar övertid per år** (med fackligt avtal kan gränsen höjas till 300)
- Max **48 timmar per vecka** i genomsnitt över 4 veckor
- **11 timmars dygnsvila** ska garanteras

### Ersättning enligt AB
| Typ | Ersättning |
|-----|-----------|
| Enkel övertid (vardag) | **180%** av timlön |
| Kvalificerad övertid (natt/helg) | **240%** av timlön |

### Komptid
Alternativt kan du ta ut övertiden som **komptid** (ledighet). Enkel övertid ger 1,5 timmar komptid per timme, kvalificerad ger 2 timmar.

### Registrering
Övertid registreras automatiskt via schemasystemet i OpenHR baserat på din instämpling.",
                catSchema.Id, ["övertid", "ATL", "arbetstid", "komptid", "övertidsersättning"]);
            art12.Publicera();

            var art13 = KnowledgeArticle.Skapa(
                "Vad innebär rehabiliteringsansvaret?",
                @"## Rehabilitering — Arbetsgivarens ansvar

Arbetsgivaren har ett rehabiliteringsansvar enligt **Socialförsäkringsbalken** och **Arbetsmiljölagen**.

### HälsoSAM-processen
Regionen följer **HälsoSAM** (Hälsa, Sjukfrånvaro, Arbetsanpassning, Möjligheter):

1. **Signal** — Upprepad eller lång sjukfrånvaro upptäcks
2. **Samtal** — Chef håller omtankesamtal
3. **Utredning** — Arbetsförmåga utreds
4. **Plan** — Rehabiliteringsplan upprättas
5. **Uppföljning** — Regelbundna uppföljningsmöten

### Dina skyldigheter
- Delta i rehabiliteringen
- Lämna in läkarintyg
- Informera om din arbetsförmåga

### Stöd
- **Företagshälsovård** — medicinsk och ergonomisk hjälp
- **HR** — koordinerar rehabiliteringsprocessen
- **Facklig representant** — stöd vid möten",
                catRehab.Id, ["rehabilitering", "HälsoSAM", "sjukfrånvaro", "arbetsanpassning", "företagshälsovård"]);
            art13.Publicera();

            var art14 = KnowledgeArticle.Skapa(
                "Hur fungerar systematiskt arbetsmiljöarbete (SAM)?",
                @"## Systematiskt arbetsmiljöarbete (SAM)

Enligt **Arbetsmiljölagen** och **AFS 2001:1** ska arbetsgivaren bedriva systematiskt arbetsmiljöarbete.

### Fyra steg
1. **Undersök** — Kartlägg risker (skyddsronder, enkäter, incidentrapporter)
2. **Bedöm** — Riskbedömning av identifierade risker
3. **Åtgärda** — Vidta åtgärder för att eliminera/minska risker
4. **Kontrollera** — Följ upp att åtgärderna fungerar

### Skyddsombud
Varje arbetsplats med minst 5 anställda ska ha ett **skyddsombud**. Skyddsombudet har rätt att:
- Delta i arbetsmiljöarbetet
- Stoppa farligt arbete (AML 6 kap 7§)
- Begära åtgärder av arbetsgivaren

### Rapportera incident
Använd OpenHR under *Arbetsmiljö → Rapportera incident* för att anmäla tillbud eller olyckor.",
                catArbetsmiljo.Id, ["arbetsmiljö", "SAM", "skyddsombud", "skyddsrond", "riskbedömning", "incident"]);
            art14.Publicera();

            var art15 = KnowledgeArticle.Skapa(
                "Vad gäller vid uppsägning?",
                @"## Uppsägning

### Uppsägningstid
Uppsägningstiden beror på din anställningstid:

| Anställningstid | Uppsägningstid |
|----------------|----------------|
| Under 2 år | 1 månad |
| 2–4 år | 2 månader |
| 4–6 år | 3 månader |
| 6–8 år | 4 månader |
| 8–10 år | 5 månader |
| Över 10 år | 6 månader |

### Egen uppsägning
Vid egen uppsägning gäller **1 månads** uppsägningstid (om inte annat avtalats).

### Arbetsbristuppsägning
Arbetsgivaren måste:
1. Förhandla med facket (MBL 11§)
2. Erbjuda omplacering (LAS 7§)
3. Följa turordningsregler (sist in, först ut)

### Slutlön
Vid anställningens slut utbetalas:
- Innestående lön
- Semesterersättning för ej uttagna dagar
- Eventuell komptid",
                catAnstallning.Id, ["uppsägning", "uppsägningstid", "arbetsbrist", "slutlön", "omplacering"]);
            art15.Publicera();

            var art16 = KnowledgeArticle.Skapa(
                "Hur fungerar introduktion av nyanställda?",
                @"## Introduktion (onboarding)

### Före första dagen
- Chef skapar onboarding-checklista i OpenHR
- IT beställer konto och utrustning
- Passerkort beställs

### Första veckan
- Välkomstmöte med chef
- Rundvandring på arbetsplatsen
- IT-introduktion (OpenHR, e-post, intranät)
- Genomgång av policyer och rutiner
- Brandskydd och nödprocedurer

### Första månaden
- Introduktionsutbildning (regionens värderingar)
- Möte med HR
- Fadder/mentor tilldelas
- Första uppföljningssamtal

### Provanställning
De första 6 månaderna kan vara **provanställning**. Under provanställning kan anställningen avbrytas med 2 veckors varsel från båda sidor.",
                kbCatRekrytering.Id, ["introduktion", "onboarding", "nyanställd", "provanställning", "fadder"]);
            art16.Publicera();

            var art17 = KnowledgeArticle.Skapa(
                "Vilka försäkringar har jag som anställd?",
                @"## Försäkringar via anställningen

### Kollektivavtalsförsäkringar (AFA)
| Försäkring | Skydd |
|-----------|-------|
| **AGS-KL** | Sjukförsäkring — tillägg utöver sjuklön |
| **TFA-KL** | Trygghetsförsäkring vid arbetsskada |
| **FHB** | Förebyggande hälsa — friskvård |

### Tjänstegrupplivförsäkring (TGL-KL)
- Dödsfallsersättning: **285 000 kr** till efterlevande
- Grundbelopp + barnbelopp

### Tjänstepension (AKAP-KR)
Se separat artikel om pensionsvillkor.

### Privat komplettering
Du kan teckna egna försäkringar som komplement, t.ex. sjukvårdsförsäkring eller inkomstförsäkring via facket.",
                kbCatLon.Id, ["försäkring", "AFA", "AGS", "TFA", "TGL", "arbetsskada"]);
            art17.Publicera();

            var art18 = KnowledgeArticle.Skapa(
                "Hur hanterar jag IT-problem?",
                @"## IT-stöd och felsökning

### Vanliga problem
- **Glömt lösenord**: Återställ via Self-Service-portalen eller ring servicedesk
- **VPN-problem**: Kontrollera att klienten är uppdaterad
- **E-post fungerar ej**: Rensa cache eller starta om Outlook

### Kontakta servicedesk
- **Telefon**: 010-441 00 00
- **E-post**: servicedesk@regionvg.se
- **Öppettider**: mån–fre 07:00–17:00

### OpenHR-stöd
Problem specifika för OpenHR:
1. Prova att ladda om sidan (Ctrl+F5)
2. Rensa webbläsarens cache
3. Kontakta HR-systemförvaltning via servicedesk

### Informationssäkerhet
- Lås alltid datorn (Win+L) när du lämnar den
- Dela aldrig ditt lösenord
- Använd stark autentisering (SITHS/BankID)",
                catIT.Id, ["IT", "problem", "lösenord", "servicedesk", "VPN", "e-post"]);
            art18.Publicera();

            var art19 = KnowledgeArticle.Skapa(
                "Vad gäller för distansarbete?",
                @"## Distansarbete / Hybridarbete

### Regionens policy
- Distansarbete kan medges efter **överenskommelse med chef**
- Huvudregeln är att arbete sker på ordinarie arbetsplats
- Max **2 dagar per vecka** distansarbete (beroende på verksamhet)

### Krav
- Ergonomisk arbetsplats hemma
- Stabil internetuppkoppling
- VPN-anslutning till regionens nätverk
- Tillgänglighet under ordinarie arbetstid

### Arbetsmiljöansvar
Arbetsgivaren har arbetsmiljöansvar även vid distansarbete. Du ansvarar för att:
- Rapportera brister i hemmiljön
- Ta pauser och vara ergonomisk
- Hålla arbete och fritid åtskilda

### Utrustning
Regionen kan erbjuda:
- Bärbar dator
- Headset
- Skärm (vid regelbundet distansarbete)

Ansök om distansarbetsavtal via HR.",
                catPolicies.Id, ["distansarbete", "hemarbete", "hybrid", "VPN", "distans"]);
            art19.Publicera();

            var art20 = KnowledgeArticle.Skapa(
                "Hur använder jag lönespecifikationen?",
                @"## Förstå din lönespecifikation

### Komponenter
| Rad | Förklaring |
|-----|-----------|
| **Grundlön** | Din månadslön |
| **OB-tillägg** | Ersättning för obekväm arbetstid |
| **Jour/Beredskap** | Ersättning för jour eller beredskapspass |
| **Övertid** | Övertidsersättning |
| **Skatteavdrag** | Preliminärskatt (kommunalskatt + ev. statlig skatt) |
| **Nettolön** | Det du får utbetalt |

### Avdrag
- **Kommunalskatt**: ca 32% (varierar per kommun)
- **Statlig skatt**: 20% på inkomster över **51 158 kr/mån** (2026)
- **Pensionsavdrag**: om du löneväxlar

### Var hittar jag den?
Lönespecifikationen finns i OpenHR under *Min sida → Lönespecifikationer*. Du kan ladda ner den som PDF.

### Löneutbetalning
Lön betalas ut den **25:e varje månad**. Om den 25:e infaller på helg sker utbetalning fredagen före.",
                kbCatLon.Id, ["lönespecifikation", "lönespec", "nettolön", "bruttolön", "skatt", "avdrag", "utbetalning"]);
            art20.Publicera();

            db.KnowledgeArticles.AddRange(art1, art2, art3, art4, art5, art6, art7, art8, art9, art10,
                art11, art12, art13, art14, art15, art16, art17, art18, art19, art20);

            // --- Assistant Actions ---
            var actions = new[]
            {
                AssistantAction.Skapa("Visa semestersaldo", "Visar ditt aktuella semestersaldo", "CheckBalance", "/minsida/ledighet"),
                AssistantAction.Skapa("Sjukanmäl mig", "Gör en sjukanmälan för idag", "ReportSick", "/minsida/sjukanmalan"),
                AssistantAction.Skapa("Visa mitt schema", "Visar ditt kommande schema", "ViewSchedule", "/minsida/schema"),
                AssistantAction.Skapa("Ansök om ledighet", "Skapa ny ledighetsansökan", "CreateLeave", "/ledighet/ny"),
                AssistantAction.Skapa("Visa lönespecifikation", "Visar din senaste lönespec", "Navigate", "/minsida/lonespecifikationer"),
                AssistantAction.Skapa("Öppna min profil", "Visa och redigera dina uppgifter", "Navigate", "/minsida/profil"),
                AssistantAction.Skapa("Sök i kunskapsbas", "Sök efter HR-information", "SearchKB", "/helpdesk/kunskapsbas"),
                AssistantAction.Skapa("Skapa ärende", "Skapa ett nytt HR-ärende", "Navigate", "/arenden/nytt"),
                AssistantAction.Skapa("Visa förmåner", "Visa dina förmåner och friskvård", "Navigate", "/formaner"),
                AssistantAction.Skapa("Visa notiser", "Visa dina notifikationer", "Navigate", "/notiser"),
            };
            db.AssistantActions.AddRange(actions);
        }

        // === Helpdesk: Categories, SLAs, Queues, Templates ===
        if (!await db.ServiceCategories.AnyAsync())
        {
            // SLA Definitions
            var slaStandard = RegionHR.Helpdesk.Domain.SLADefinition.Skapa("Standard", 240, 1440, 480);
            var slaHog = RegionHR.Helpdesk.Domain.SLADefinition.Skapa("Hög prioritet", 60, 480, 120);
            var slaKritisk = RegionHR.Helpdesk.Domain.SLADefinition.Skapa("Kritisk", 30, 240, 60);
            db.SLADefinitions.AddRange(slaStandard, slaHog, slaKritisk);

            // HR Queues
            var hrSupport = RegionHR.Helpdesk.Domain.HRQueue.Skapa("HR-support", "Allmänna HR-frågor och support", new List<Guid>());
            var loneAdmin = RegionHR.Helpdesk.Domain.HRQueue.Skapa("Löneadmin", "Lönefrågor och förmåner", new List<Guid>());
            db.HRQueues.AddRange(hrSupport, loneAdmin);

            // Service Categories (5 st)
            var hdCatLon = RegionHR.Helpdesk.Domain.ServiceCategory.Skapa(
                "Lön & Förmåner", "Frågor om lön, lönespecifikationer, förmåner och friskvårdsbidrag",
                defaultKoId: loneAdmin.Id, defaultPrioritet: RegionHR.Helpdesk.Domain.ServiceRequestPriority.Medium, defaultSLAId: slaStandard.Id);
            var hdCatSemester = RegionHR.Helpdesk.Domain.ServiceCategory.Skapa(
                "Semester & Ledighet", "Frågor om semester, tjänstledighet, VAB och föräldraledighet",
                defaultKoId: hrSupport.Id, defaultPrioritet: RegionHR.Helpdesk.Domain.ServiceRequestPriority.Medium, defaultSLAId: slaStandard.Id);
            var hdCatAnstallning = RegionHR.Helpdesk.Domain.ServiceCategory.Skapa(
                "Anställning", "Frågor om anställningsvillkor, intyg och arbetsgivarintyg",
                defaultKoId: hrSupport.Id, defaultPrioritet: RegionHR.Helpdesk.Domain.ServiceRequestPriority.Medium, defaultSLAId: slaStandard.Id);
            var hdCatIT = RegionHR.Helpdesk.Domain.ServiceCategory.Skapa(
                "IT & System", "Frågor om HR-systemet, inloggning och tekniska problem",
                defaultKoId: hrSupport.Id, defaultPrioritet: RegionHR.Helpdesk.Domain.ServiceRequestPriority.High, defaultSLAId: slaHog.Id);
            var hdCatOvrigt = RegionHR.Helpdesk.Domain.ServiceCategory.Skapa(
                "Övrigt", "Övriga HR-frågor som inte passar i annan kategori",
                defaultKoId: hrSupport.Id, defaultPrioritet: RegionHR.Helpdesk.Domain.ServiceRequestPriority.Low, defaultSLAId: slaStandard.Id);
            db.ServiceCategories.AddRange(hdCatLon, hdCatSemester, hdCatAnstallning, hdCatIT, hdCatOvrigt);

            // Case Templates (3 st)
            var mallLon = RegionHR.Helpdesk.Domain.CaseTemplate.Skapa("Lönefråga - standardsvar", hdCatLon.Id,
                "Tack för din fråga om lön. Vi har tagit emot ditt ärende och återkommer inom SLA-tiden.\n\nDu kan se din senaste lönespecifikation under Min sida > Lönespecifikationer.",
                new List<string> { "Kontrollera lönespec i systemet", "Jämför med kollektivavtal", "Svara medarbetaren" });
            var mallSemester = RegionHR.Helpdesk.Domain.CaseTemplate.Skapa("Semesterfråga - standardsvar", hdCatSemester.Id,
                "Tack för din fråga om semester. Ditt aktuella semestersaldo hittar du under Ledighet > Saldon.\n\nOm du vill ansöka om semester kan du göra det under Ledighet > Ny ansökan.",
                new List<string> { "Kontrollera semestersaldo", "Verifiera godkännanderegler", "Svara medarbetaren" });
            var mallIT = RegionHR.Helpdesk.Domain.CaseTemplate.Skapa("IT-problem - felsökning", hdCatIT.Id,
                "Vi har tagit emot din felanmälan. Vänligen prova följande:\n1. Rensa webbläsarens cache\n2. Logga ut och in igen\n3. Prova en annan webbläsare\n\nOm problemet kvarstår eskalerar vi till IT-support.",
                new List<string> { "Reproducera felet", "Kontrollera systemloggar", "Eskalera till IT om nödvändigt", "Svara medarbetaren" });
            db.CaseTemplates_Helpdesk.AddRange(mallLon, mallSemester, mallIT);
        }

        // === Pay Transparency Report (EU-direktivet) ===
        if (!await db.PayTransparencyReports.AnyAsync())
        {
            var ptRapport = RegionHR.Analytics.Domain.PayTransparencyReport.Skapa(2025, "2025-01-01 till 2025-12-31");

            // 5 befattningskategorier med exempeldata
            var ptAnalyser = new List<RegionHR.Analytics.Domain.PayGapAnalysis>();

            var a1 = RegionHR.Analytics.Domain.PayGapAnalysis.Skapa(
                ptRapport.Id, "Sjukskoterska", 85, 25, 33800m, 35200m, 33500m, 34800m,
                3.98m, 2.1m,
                "{\"genomsnittligAnstallningstidKvinnor\":6.2,\"genomsnittligAnstallningstidMan\":4.8,\"genomsnittligSysselsattningsgradKvinnor\":92.5,\"genomsnittligSysselsattningsgradMan\":98.0,\"genomsnittligAlderKvinnor\":38.5,\"genomsnittligAlderMan\":35.2}");
            a1.LaggTillKohort(RegionHR.Analytics.Domain.PayGapCohort.Skapa(a1.Id, "Alder: Under 30", 22, 2.1m, -0.5m));
            a1.LaggTillKohort(RegionHR.Analytics.Domain.PayGapCohort.Skapa(a1.Id, "Alder: 30-50", 58, 4.2m, -0.3m));
            a1.LaggTillKohort(RegionHR.Analytics.Domain.PayGapCohort.Skapa(a1.Id, "Alder: 50+", 30, 5.1m, 0.2m));
            a1.LaggTillKohort(RegionHR.Analytics.Domain.PayGapCohort.Skapa(a1.Id, "Anstallningsform: Tillsvidare", 95, 3.5m, -0.4m));
            a1.LaggTillKohort(RegionHR.Analytics.Domain.PayGapCohort.Skapa(a1.Id, "Sysselsattning: Heltid", 78, 3.2m, -0.2m));
            ptAnalyser.Add(a1);

            var a2 = RegionHR.Analytics.Domain.PayGapAnalysis.Skapa(
                ptRapport.Id, "Lakare", 40, 55, 58000m, 62000m, 57500m, 61000m,
                6.45m, 4.8m,
                "{\"genomsnittligAnstallningstidKvinnor\":8.5,\"genomsnittligAnstallningstidMan\":12.3,\"genomsnittligSysselsattningsgradKvinnor\":95.0,\"genomsnittligSysselsattningsgradMan\":100.0,\"genomsnittligAlderKvinnor\":42.0,\"genomsnittligAlderMan\":48.5}");
            a2.LaggTillKohort(RegionHR.Analytics.Domain.PayGapCohort.Skapa(a2.Id, "Alder: Under 30", 12, 1.5m, null));
            a2.LaggTillKohort(RegionHR.Analytics.Domain.PayGapCohort.Skapa(a2.Id, "Alder: 30-50", 48, 5.8m, -1.2m));
            a2.LaggTillKohort(RegionHR.Analytics.Domain.PayGapCohort.Skapa(a2.Id, "Alder: 50+", 35, 8.2m, 0.5m));
            ptAnalyser.Add(a2);

            var a3 = RegionHR.Analytics.Domain.PayGapAnalysis.Skapa(
                ptRapport.Id, "Underskoterska", 120, 30, 27800m, 28200m, 27500m, 28000m,
                1.42m, 0.8m,
                "{\"genomsnittligAnstallningstidKvinnor\":7.1,\"genomsnittligAnstallningstidMan\":5.5,\"genomsnittligSysselsattningsgradKvinnor\":85.0,\"genomsnittligSysselsattningsgradMan\":92.0,\"genomsnittligAlderKvinnor\":41.0,\"genomsnittligAlderMan\":37.0}");
            a3.LaggTillKohort(RegionHR.Analytics.Domain.PayGapCohort.Skapa(a3.Id, "Alder: Under 30", 35, 0.5m, -0.3m));
            a3.LaggTillKohort(RegionHR.Analytics.Domain.PayGapCohort.Skapa(a3.Id, "Alder: 30-50", 75, 1.8m, -0.1m));
            a3.LaggTillKohort(RegionHR.Analytics.Domain.PayGapCohort.Skapa(a3.Id, "Alder: 50+", 40, 2.0m, 0.4m));
            ptAnalyser.Add(a3);

            var a4 = RegionHR.Analytics.Domain.PayGapAnalysis.Skapa(
                ptRapport.Id, "Verksamhetschef", 8, 12, 50000m, 52000m, 49000m, 51500m,
                3.85m, 2.5m,
                "{\"genomsnittligAnstallningstidKvinnor\":10.0,\"genomsnittligAnstallningstidMan\":14.5,\"genomsnittligSysselsattningsgradKvinnor\":100.0,\"genomsnittligSysselsattningsgradMan\":100.0,\"genomsnittligAlderKvinnor\":48.0,\"genomsnittligAlderMan\":52.0}");
            ptAnalyser.Add(a4);

            var a5 = RegionHR.Analytics.Domain.PayGapAnalysis.Skapa(
                ptRapport.Id, "HR-chef", 5, 3, 47000m, 48000m, 46500m, 47500m,
                2.08m, 1.2m,
                "{\"genomsnittligAnstallningstidKvinnor\":6.0,\"genomsnittligAnstallningstidMan\":8.0,\"genomsnittligSysselsattningsgradKvinnor\":100.0,\"genomsnittligSysselsattningsgradMan\":100.0,\"genomsnittligAlderKvinnor\":44.0,\"genomsnittligAlderMan\":46.0}");
            ptAnalyser.Add(a5);

            var rapportDataJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                EUDirectiveVersion = "2023/970",
                Rapportdatum = "2025-12-31",
                Sammanfattning = new
                {
                    TotalAnstallda = 383,
                    AntalKvinnor = 258,
                    AntalMan = 125,
                    AndelKvinnor = 67.4m,
                    AndelMan = 32.6m,
                    AntalBefattningskategorier = 5
                },
                Lonekvartiler = new[]
                {
                    new { Kvartil = 1, Namn = "Q1 (lagst)", AntalKvinnor = 68, AntalMan = 28, AndelKvinnor = 70.8m, AndelMan = 29.2m },
                    new { Kvartil = 2, Namn = "Q2", AntalKvinnor = 65, AntalMan = 31, AndelKvinnor = 67.7m, AndelMan = 32.3m },
                    new { Kvartil = 3, Namn = "Q3", AntalKvinnor = 64, AntalMan = 32, AndelKvinnor = 66.7m, AndelMan = 33.3m },
                    new { Kvartil = 4, Namn = "Q4 (hogst)", AntalKvinnor = 61, AntalMan = 34, AndelKvinnor = 64.2m, AndelMan = 35.8m }
                },
                KategorierMedUtredningskrav = new[]
                {
                    new { Befattningskategori = "Lakare", OjusteratGapProcent = 6.45m, JusteratGapProcent = 4.8m }
                },
                GemensamLonebedomningKravs = true
            }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

            ptRapport.Berakna(383, 4.12m, 3.85m, rapportDataJson, ptAnalyser);
            ptRapport.Publicera();

            db.PayTransparencyReports.Add(ptRapport);
        }

        // === Shift Bidding (3 OpenShifts + 2 ShiftBids) ===
        var openShift1 = RegionHR.Scheduling.Domain.OpenShift.Skapa(
            avd32.Id, DateOnly.FromDateTime(DateTime.Today.AddDays(3)), "Dag",
            new TimeOnly(7, 0), new TimeOnly(15, 30),
            "{\"skills\":[\"HLR\",\"Lakemedelshantering\"]}", "Ordinarie");
        var openShift2 = RegionHR.Scheduling.Domain.OpenShift.Skapa(
            akuten.Id, DateOnly.FromDateTime(DateTime.Today.AddDays(5)), "Kvall",
            new TimeOnly(15, 0), new TimeOnly(22, 0),
            null, "OB");
        var openShift3 = RegionHR.Scheduling.Domain.OpenShift.Skapa(
            avd33.Id, DateOnly.FromDateTime(DateTime.Today.AddDays(7)), "Natt",
            new TimeOnly(21, 0), new TimeOnly(7, 0),
            "{\"skills\":[\"IVA\"]}", "Overtid");
        db.OpenShifts.AddRange(openShift1, openShift2, openShift3);

        // 2 bud på det första öppna passet
        var bid1 = RegionHR.Scheduling.Domain.ShiftBid.Skapa(openShift1.Id, employees[0].Id, 1, "Vill gärna ta extrapasset");
        var bid2 = RegionHR.Scheduling.Domain.ShiftBid.Skapa(openShift1.Id, employees[2].Id, 2, "Kan jobba denna dag");
        // Note: LaggTillBud is internal to Scheduling assembly; bids are linked via FK
        db.ShiftBids.AddRange(bid1, bid2);

        // === Grievance (1 st, status: UnderInvestigation) ===
        var grievance1 = RegionHR.CaseManagement.Domain.Grievance.Skapa(
            employees[4].Id,
            RegionHR.CaseManagement.Domain.GrievanceType.Arbetsmiljo,
            "Upprepade brister i ergonomisk utrustning på arbetsplatsen. Stolen är trasig och höj-sänkbart skrivbord saknas trots upprepade förfrågningar.",
            "Kommunal - Lisa Fackman");
        grievance1.Bekrafta();
        grievance1.StartaUtredning("Karl Berg");
        db.Grievances.Add(grievance1);

        // === Skatteverkets skattetabell 34, kolumn 1, 2026 (Örebro 33,65 %) — våg 1/skatt ===
        // Utan denna seed blir preliminärskatten 0 i drift.
        var skattetabell34 = new RegionHR.Payroll.Domain.TaxTable { Ar = 2026, Tabellnummer = 34, Kolumn = 1 };
        var t34rader = new (decimal Fran, decimal Till, decimal Skatt)[]
        {
            (0, 2000, 0), (2001, 4999, 150), (5001, 9999, 422), (10001, 11999, 1251),
            (12001, 13999, 1676), (14001, 14999, 2099), (15001, 17999, 2304), (18001, 19999, 3015),
            (20001, 21999, 3541), (22001, 23999, 4042), (24001, 25999, 4553), (26001, 27999, 5070),
            (28001, 29999, 5588), (30001, 31999, 6105), (32001, 33999, 6623), (34001, 35999, 7140),
            (36001, 37999, 7658), (38001, 39999, 8175), (40001, 41999, 8720), (42001, 43999, 9400),
            (44001, 45999, 10080), (46001, 47999, 10760), (48001, 49999, 11440), (50001, 51999, 12120),
            (52001, 54999, 12800), (55001, 57999, 13854), (58001, 59999, 15474), (60001, 64999, 16554),
            (65001, 69999, 19254), (70001, 77999, 21954), (78001, 999999, 26166),
        };
        foreach (var (fran, till, skatt) in t34rader)
            skattetabell34.LaggTillRad(new RegionHR.Payroll.Domain.TaxTableRow { InkomstFran = fran, InkomstTill = till, Skattebelopp = skatt });
        db.TaxTables.Add(skattetabell34);

        // === Rapportdefinitioner (self-service builder + schemalagd) — våg 1/rapporter ===
        if (!await db.ReportDefinitions.AnyAsync())
        {
            var rapAnstallda = RegionHR.Reporting.Domain.ReportDefinition.Skapa(
                "Aktiva anställda per enhet", "Antal aktiva anställda grupperat på enhet",
                RegionHR.Reporting.Domain.ReportType.AdHoc);
            rapAnstallda.SattRapportmall(
                "Anstallda",
                "[\"Fornamn\",\"Efternamn\",\"Enhet\",\"Befattning\",\"Sysselsattningsgrad\"]",
                "{\"Enhet\":\"Alla\",\"Status\":\"Aktiv\"}",
                "Enhet", "Bar");
            db.ReportDefinitions.Add(rapAnstallda);

            var rapLon = RegionHR.Reporting.Domain.ReportDefinition.Skapa(
                "Lönekörningar per period", "Brutto/skatt/netto ur persisterad lönedata",
                RegionHR.Reporting.Domain.ReportType.AdHoc);
            rapLon.SattRapportmall(
                "Lonekorngar",
                "[\"Period\",\"Brutto\",\"Skatt\",\"Netto\",\"Arbetsgivaravgift\"]",
                "{}", null, "Table");
            db.ReportDefinitions.Add(rapLon);

            var rapSchema = RegionHR.Reporting.Domain.ReportDefinition.Skapa(
                "Månatligt löneregister", "Automatiskt löneregister till HR",
                RegionHR.Reporting.Domain.ReportType.Loneregister);
            rapSchema.SattRapportmall(
                "Lonekorngar",
                "[\"Period\",\"Brutto\",\"Skatt\",\"Netto\",\"Arbetsgivaravgift\"]",
                "{}", null, "Table");
            rapSchema.SattSchemalagd("0 6 1 * *", "hr@regionorebrolan.se");
            db.ReportDefinitions.Add(rapSchema);

            db.ScheduledReports.Add(RegionHR.Reporting.Domain.ScheduledReport.Skapa(
                rapSchema.Id, "Monthly", "hr@regionorebrolan.se", "Excel"));
        }

        // === Demo: pågående långtidssjukfrånvaro som rehab-auto-triggern fångar upp — våg 1/rehab ===
        var demoSjukfall = RegionHR.Leave.Domain.SickLeaveNotification.Skapa(
            employees[0].Id.Value, DateOnly.FromDateTime(DateTime.Today.AddDays(-20)));
        demoSjukfall.UppdateraDag(21);
        db.SickLeaveNotifications.Add(demoSjukfall);

        // === Utvecklingsplan ur Annas medarbetarsamtal (samtal → kompetensgap → plan) — våg 1/kompetens ===
        var utvPlanAnna = RegionHR.Competence.Domain.DevelopmentPlan.Skapa(
            employees[0].Id.Value, "Sjukskoterska Avd 32",
            DateOnly.FromDateTime(DateTime.Today));
        utvPlanAnna.KopplaTillSamtal(reviewAnna.Id);
        utvPlanAnna.LaggTillMilstolpe(
            "Hoj \"Lakemedelshantering\" fran niva 3 till 4", "Skill",
            DateOnly.FromDateTime(DateTime.Today.AddMonths(3)), lakemedel.Id, 3, 4);
        utvPlanAnna.Aktivera();
        db.DevelopmentPlans.Add(utvPlanAnna);

        await db.SaveChangesAsync();
    }
}
