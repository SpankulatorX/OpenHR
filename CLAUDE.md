# OpenHR — Komplett HR-system för svensk region

## Projektbeskrivning
Modular monolith HR-system (AGPL-3.0) som ersätter HEROMA. 25 moduler, 494 tester, 96 Blazor-sidor, 94 rutter, 75 infrastrukturfiler. Hanterar hela HR-livscykeln: personalregister, lön/payroll (svensk skattelagstiftning + kollektivavtal AB/HOK), schemaplanering (24/7 sjukvård), självservice, ärendehantering, LAS-uppföljning med konverteringsflöde, rehabilitering (HälsoSAM), löneöversyn, resor/utlägg, rekrytering med pipeline, ledighetshantering med interaktiv kalender, dokumenthantering med mallgenerering och PDF-förhandsvisning, medarbetarsamtal med 360-feedback, kompetensregister med gap-analys, rapportering med diagram, GDPR-efterlevnad, granskningslogg med ändringshistorik per anställd, notifieringar (InApp + Email + SMS + SignalR), pulsundersökningar, peer recognition, offboarding, onboarding, förmåner, utbildning/e-learning, MBL-förhandlingar, godkännandeflöden med batch-operationer, samt integrationer mot ~15 externa system.

## Bygga och köra
```bash
# Utan Docker
dotnet build RegionHR.sln          # 0 errors, 0 warnings
dotnet test RegionHR.sln           # 494 tester, 0 failures
dotnet run --project src/Web/RegionHR.Web.csproj  # http://localhost:5076

# Med Docker
docker compose up -d               # PostgreSQL + app
```

## Demo-användare
| Användare | Roll | Ser |
|-----------|------|-----|
| Admin | Admin | Allt |
| Karl Berg | HR | Personal, Lön, Admin |
| Eva Nilsson | Chef | Team, Godkännanden |
| Sara Andersson | Anställd | Min sida, Ledighet |

Login: http://localhost:5076/login (SITHS/BankID-simulering)

## Arkitektur
- **Modular Monolith** med schema-per-modul i PostgreSQL 17
- **Frontend**: Blazor Server med MudBlazor 9.1 (global InteractiveServer via Routes.razor)
- **Tema**: Nordic Refined — Plus Jakarta Sans, teal primary, dark mode (persistent via ProtectedSessionStorage)
- **Auth**: Rollbaserad (Admin/HR/Chef/Anställd) med SITHS/BankID-simulering, persistent session
- **Databas**: PostgreSQL med EF Core migrationer + seed data (10 anställda, 6 enheter)
- **Export**: CSV med kopiera/ladda ner, PDF-förhandsvisning (text-baserad, QuestPDF-redo)
- **Notifieringar**: InApp + Email (MailKit) + SMS (HTTP gateway) + SignalR real-time
- **Bakgrundsjobb**: NotificationReminder, RetentionCleanup, ScheduledReports, CertificationReminder, LASAlert
- **Säkerhet**: CSP headers, X-Frame-Options, rate limiting (100 req/min), CSRF-skydd
- **CI/CD**: GitHub Actions (build + test + publish)
- **Container**: Docker Compose (PostgreSQL + app)
- **PWA**: Service worker, offline-sida, manifest.json
- **Prestanda**: CSS containment, will-change, content-visibility, smooth scroll
- All teknologi är 100% FOSS/open source, inga kommersiella beroenden

## Modulstruktur (25 moduler)

### Kärnmoduler
- `src/SharedKernel/` — Domänprimitiver (Personnummer, Money, DateRange, EmployeeId) med EF Core ValueConverters
- `src/Modules/Core/` — Personalregister, organisation, anställningar
- `src/Modules/Payroll/` — Löneberäkning, skatt, kollektivavtal, OB-tillägg
- `src/Modules/Scheduling/` — Schema, instämpling, ATL-efterlevnad, passbyte
- `src/Modules/CaseManagement/` — Ärenden, workflows, frånvaro, MBL-förhandlingar
- `src/Modules/LAS/` — LAS-ackumulering, konvertering till tillsvidare, företrädesrätt
- `src/Modules/HalsoSAM/` — Rehabilitering, sjukfrånvarobevakning, FK-anmälan
- `src/Modules/SalaryReview/` — Löneöversynsrundor, lönekartering (diskrimineringslagen)
- `src/Modules/Travel/` — Resor, utlägg, traktamente, resekrav
- `src/Modules/Recruitment/` — Vakanser, ansökningar, pipeline, onboarding, referenskontroll
- `src/Modules/IntegrationHub/` — AGI-XML, Nordea pain.001, outbox pattern
- `src/Modules/SelfService/` — Självserviceportal (Min sida)

### Expansionsmoduler
- `src/Modules/Audit/` — Granskningslogg (alla ändringar loggas)
- `src/Modules/Notifications/` — Notiseringar (InApp, Email, SMS)
- `src/Modules/Leave/` — Ledighetshantering (semester, sjukfrånvaro, VAB, föräldraledighet)
- `src/Modules/Documents/` — Dokumenthantering med retention, GDPR-klassificering, mallgenerering
- `src/Modules/Performance/` — Medarbetarsamtal, 360-feedback
- `src/Modules/Reporting/` — Rapportering (standard + schemalagda + lönekartering + analytics)
- `src/Modules/GDPR/` — GDPR-efterlevnad (registerutdrag, anonymisering, retention)
- `src/Modules/Competence/` — Kompetens/certifieringar, gap-analys per roll
- `src/Modules/Benefits/` — Förmåner (friskvårdsbidrag, försäkringar)
- `src/Modules/Offboarding/` — Offboarding-workflow (AD, passerkort, utrustning, slutlön)
- `src/Modules/LMS/` — Utbildning, e-learning
- `src/Modules/Analytics/` — Workforce analytics, kostnadssimulering, SCB-rapporter
- `src/Modules/Positions/` — Positionsregister, successionsplanering

### Presentation & Infrastruktur
- `src/Api/` — ASP.NET Core Web API
- `src/Web/` — Blazor Server frontend (96 sidor, MudBlazor 9.1)
- `src/DesignSystem/` — Blazor komponentbibliotek
- `src/Infrastructure/` — EF Core, repositories, export, integrationer, bakgrundsjobb

## Infrastrukturtjänster

### Löneberäkning & Kollektivavtal
- `SwedishTaxCalculator` — Kommunalskatt per kommun via `KommunSkattesatser` (default Örebro 2026: 33,65%), statlig skatt 20% över skiktgränsen 643 000 kr/år (≈53 583 kr/mån), arbetsgivaravgift 31,42% (äldre endast ålderspensionsavgift 10,21%, född 1937 eller tidigare 0%) via `Arbetsgivaravgift`-domänen
- `KollektivavtalEngine` — legacy-hjälpare som delegerar till de kanoniska, årsversionerade AB-tabellerna (`ABOTillaggSatser`, `ABOvertidSatser`, `ABSemesterRegler`): OB enligt AB § 21 (fr.o.m. 2026-04-01: kväll 26,40 kr/h, natt 58,40, helg 68,10, storhelg 130,70; förhöjd nattsats kl 22–06 för helg 78,30 och storhelg 156,90; fr.o.m. 2025-04-01: 25,60/56,70/66,10/126,90), viloRegler (11h), övertid (180%/240%), semester per ålder

### Schemaoptimering
- `SchemaOptimizer` — Round-robin tilldelning med balansindex, per-person timmespårning

### Export & Dokument
- `PdfGenerator` — Lönespecifikation, tjänstgöringsintyg, anställningsavtal (text-baserad, QuestPDF-redo)
- `ExportService` — CSV/Excel export med ClosedXML
- `FileStorageService` — Filuppladdning/nedladdning till wwwroot/uploads

### Kommunikation
- `EmailSender` — SMTP via MailKit (demo-läge när okonfigurerad)
- `SmsNotificationSender` — HTTP-gateway (FOSS-kompatibel)
- `SignalR NotificationHub` — Realtidsnotiser

### Integrationer
- `AGIXmlGenerator` — Arbetsgivardeklaration till Skatteverket
- `NordeaPainGenerator` — ISO 20022 pain.001 lönefiler

### Bakgrundsjobb
- `NotificationReminderService` — Sjukfrånvaropåminnelser
- `RetentionCleanupService` — GDPR-gallring
- `ScheduledReportService` — Cron-baserade rapporter
- `CertificationReminderService` — Certifieringar som går ut (30/60/90 dagar)
- `LASAlertService` — LAS-trösklar (300/330/350/360 dagar)

## Frontend (96 sidor)

### Layout & Tema
- `AdminLayout.razor` — Huvudlayout med sidebar, topbar, dark mode, auth-guard, laddningsindikator
- `NavMenu.razor` — Rollbaserad sidmeny med i18n (IStringLocalizer), expanderbara grupper
- `TopBar.razor` — AppBar med omnisökning (35+ mål + senaste sökningar), dark mode, språkväxling, notiser, användarmeny med logout
- `openhr-theme.css` — Nordic Refined tema med light/dark mode
- `mobile.css` — Responsiv design (600px/960px breakpoints)
- `performance.css` — CSS containment, will-change, content-visibility

### Sidor per område
| Område | Sidor | Rutter |
|--------|-------|--------|
| Auth | 1 | `/login` (SITHS/BankID-simulering) |
| Dashboard | 1 | `/` (konfigurerbar, beräknade KPI:er, klickbara kort) |
| Personal | 6 | `/anstallda`, `/anstallda/ny`, `/anstallda/{id}` (med audit trail), redigera, lönehistorik, anställning |
| Organisation | 2 | `/organisation`, `/positioner` |
| Lön | 5 | `/lon/korningar`, `/lon/korning/ny`, `/lon/lonearter`, `/lon/statistik` (med diagram), `/loneoversyn` |
| Schema & Tid | 6 | `/schema`, `/schema/ny`, `/schema/bemanning`, `/schema/atl`, `/stampling`, `/tidrapporter` |
| Ärenden | 3 | `/arenden`, `/arenden/nytt`, `/arenden/mbl` |
| Godkännanden | 1 | `/godkannanden` (med batch godkänn/avslå) |
| Ledighet | 6 | `/ledighet`, `/ledighet/ny`, `/ledighet/kalender` (interaktiv månadsvy), `/ledighet/saldon`, `/ledighet/vab`, `/ledighet/foraldraledighet` |
| HälsoSAM | 2 | `/halsosam`, `/halsosam/ny` |
| LAS | 1 | `/las` (konvertera/avsluta/pausa med undo) |
| Dokument | 5 | `/dokument`, `/dokument/ny` (filuppladdning), `/dokument/organisation`, `/dokument/policyer`, `/dokument/mall/generera` (PDF-preview) |
| Medarbetarsamtal | 3 | `/medarbetarsamtal`, `/medarbetarsamtal/ny`, `/medarbetarsamtal/360` |
| Rekrytering | 6 | `/rekrytering/vakanser` (22 ansökande, pipeline), `/rekrytering/pipeline`, `/rekrytering/intern`, `/rekrytering/onboarding`, `/rekrytering/referenskontroll`, `/rekrytering/statistik` |
| Rapporter | 5 | `/rapporter` (CSV-export), `/rapporter/lonekartering`, `/rapporter/analytics` (MudChart diagram), `/rapporter/kostnadssimulering`, `/rapporter/scb` |
| Min sida | 8 | `/minsida`, `/minsida/sjukanmalan`, `/minsida/lon`, `/minsida/lonespecifikationer`, `/minsida/schema`, `/minsida/ledighet`, `/minsida/profil` (med validering), `/minsida/arenden` |
| Chef | 4 | `/chef`, `/chef/team`, `/chef/bemanning`, `/chef/franvarokalender` |
| Admin | 7 | `/admin/konfiguration`, `/admin/import` (CSV-mallar), `/admin/berom`, `/admin/pulsundersokning`, `/admin/anslagstavla`, `/admin/delegering`, `/admin/succession` |
| Förmåner | 3 | `/formaner`, `/formaner/friskvard`, `/formaner/forsakringar` |
| Övrigt | 8 | `/gdpr`, `/audit`, `/notiser` (med batch-radera + simulera), `/notiser/installningar`, `/offboarding`, `/offboarding/ny` (PDF-preview), `/utbildning`, `/utbildning/elearning` |
| Integrationer | 2 | `/integrationer`, `/integrationer/platsbanken` |
| Resor | 1 | `/resor` |
| Kompetens | 2 | `/kompetens`, `/kompetens/gapanalys` |

### Interaktiva komponenter
- **OhrAssistant** — AI-chatbot med personliga svar ("Du har 18 semesterdagar kvar, Anna"), åtgärdsknappar (sjukanmäl direkt), navigering
- **OhrConversationFlow** — Steg-för-steg-wizard med tillbaka-knapp och detaljerade resultatpaneler
- **OhrSuggestionCard** — Åtgärdsförslag med förklaringar och knappar (används i LAS, HälsoSAM)
- **OhrEmptyState** — Tom-tillståndsvy med illustration och CTA
- **OhrPageLoader** — Laddningsindikator med spinner och meddelande
- **Undo Snackbar** — 10-sekunders ångra på destruktiva åtgärder (avslå ansökan, pausa LAS, markera lästa)

### Tema & Dark Mode
- Light: Varm beige bakgrund (#f6f5f2), teal primary (#0e7490), Plus Jakarta Sans
- Dark: Midnattssvart (#0f1419), himmelsblå accent (#38bdf8), navy kort (#1a2332)
- Styrs via `.ohr-dark`/`.ohr-light` klass på wrapper i AdminLayout
- Persistent via ProtectedSessionStorage (överlever F5)

### Auth & Roller
- Login med SITHS-kort (simulerad kortläsare) eller BankID (simulerad QR-kod)
- 4 roller: Admin, HR, Chef, Anställd
- NavMenu filtreras per roll (Admin ser allt, Anställd ser bara Min sida + Ledighet)
- Persistent session via ProtectedSessionStorage

### i18n
- Svenska som standard, engelska förberett
- 50+ nycklar i SharedResources.resx (sv) och SharedResources.en.resx
- NavMenu och TopBar använder IStringLocalizer
- Språkväxling via cookie + page reload

## Konventioner
- Alla monetära värden använder `Money` (decimal-baserad, SEK) med EF Core ValueConverter
- Personnummer hanteras via `Personnummer` value object med Luhn-validering
- Alla strongly-typed IDs (EmployeeId, CaseId, etc.) har EF Core ValueConverters via ConfigureConventions
- Svenskt språk i domänmodell, engelskt för infrastruktur
- Varje modul exponerar ett publikt `Contracts/` interface
- Alla formulär adderar till synlig lista med bekräftelse (ingen "under utveckling" kvar)
- Workflows visar detaljerade resultatpaneler (ärendenummer, notifieringar, nästa steg)
- Destruktiva åtgärder har 10-sekunders undo via snackbar
- Formulärvalidering på nyckelformulär (Required, email-format, telefon-format)
- Copyleft-licens: AGPL-3.0

## Deployment
```bash
# Docker Compose (rekommenderat)
docker compose up -d

# CI/CD
# GitHub Actions kör automatiskt vid push till main:
# build → test → publish → artifact upload

# Health check
curl http://localhost:5076/health
```
