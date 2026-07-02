# OpenHR

**Öppen källkod-HR-system för svenska regioner och kommuner.**

OpenHR är ett personalhanteringssystem byggt för att ersätta HEROMA och andra proprietära HR-system inom svensk offentlig sektor. Byggt med öppen källkod (AGPL-3.0), svensk arbetsrätt, kollektivavtal och GDPR.

> **OpenHR 2.2** — 38 moduler, 214 sidor, 240 domänfiler, **2 165 tester (alla gröna)**, 329/330 i18n-nycklar (sv+en). Bygg: `dotnet test RegionHR.sln`.

## Status & begränsningar (läs först)

OpenHR är en **fungerande, testad demonstrator** — inte en skarp driftsatt Heroma-ersättare än, men täcker nu regionens nio funktionella delprojekt OCH större delen av integrationslandskapet (IT-arkitektur "Personalstöd v7"). Vad som är på riktigt vs demo:

- **På riktigt:** löneberäkning med korrekta 2026-värden (skattetabell, AB O-tillägg, arbetsgivaravgift, traktamente); hela HR-livscykeln (anställa→schema→tid→lön→utbetalningsfil); rollbaserad URL-behörighet (Anställd når aldrig lön/audit/admin); löneflöden (utmätning/fackavgift, tolkersättning, ersättning förtroendevalda); AKAP-KR-pension; LAS auto-kedja; behovsstyrd vårdschema-automatik; e-arkiv (arkivlagen); SCORM/e-learning; web push; BI/DW-export + realtids-beslutsstöd; KLASSA-informationsklassning; **integrationsramverk** (register + SFTP-transport + jobb-runner + körningslogg + övervakning) med filgeneratorer/adaptrar för AGI, pain.001, KPA-pension, FK-anmälan, SIE/Raindance-kontering, folkbokföring-import, KOLL-export, SCB/SKR-statistik. 2 165 gröna tester.
- **Demo/simulering (kräver externa avtal för skarp drift):** inloggning (BankID/SITHS simulerad; Entra/OIDC är config-ready via `appsettings` "Oidc"); HSA-katalogen (sandbox); skarp nätverkstransport till bank/Försäkringskassan/KPA/Skatteverket-Navet/Inera/Health Connect — filformaten är byggda + config-ready, bara den avtalsbundna kopplingen fattas. Se `docs/drift-reservloneplan.md`.
- **Återstår för produktion:** horisontell skalning för 11 000 användare (lasttest: ~1,1 MiB/circuit, median 14 ms → en 2 GB-instans klarar ~1 500 samtidiga; 11k = 2–4 instanser bakom lastbalanserare), skarpa integrationer (avtal), formell KLASSA-riskanalys, drift-SLA. Se [gap-analys](docs/gap-analysis-enterprise-hr.md).

## Funktionsstatus

### Beräkningsmotorer (kopplade till UI, korrekta 2026-värden)
Riktiga beräkningar med svensk lagstiftning, anropade direkt från lönekörningen:

- **PayrollCalculationEngine** — brutto→netto via seedad **skattetabell** (Skatteverket tabell 34); arbetsgivaravgift 31,42 % (äldre 10,21 % för födda ≤1958; 1937− = 0 %; temporär ungdomsnedsättning 20,81 %); statlig skatt över skiktgräns 643 000 kr/år
- **CollectiveAgreementRulesEngine** — AB §21 O-tillägg (kväll 25,60 / natt 56,70 / helg 66,10 / storhelg 126,90 kr/h), övertid §20, semester §27 per ålder; årsversionerat
- **TraktamentsCalculator** — inrikes 300/150 kr + måltidsavdrag enligt Skatteverket 2026 (SKV 354); årsversionerat
- **ConstraintScheduleSolver + ArbetstidslagenValidator** — schemaoptimering med ATL-kontroll (dygnsvila 11h, veckovila 36h)
- **Integrationsformat** — AGI-XML (Skatteverket SKV 269, fältkod + specifikationsnummer), pain.001.001.03 (ISO 20022, riktiga person-/bankuppgifter), nedladdas via /lon/export

### Kärnmoduler (DB-backed, domänlogik)
Fullständig datamodell med entities, domänmetoder, EF Core-konfiguration, migrationer och seed:

| Modul | Entities | Domänflöde | Routes |
|-------|----------|------------|--------|
| **Personalregister** | Employee, Employment, OrganizationUnit | Skapa, anställ, organisationsträd | `/anstallda`, `/anstallda/ny`, `/organisation` |
| **Ledighet** | LeaveRequest, VacationBalance, SickLeaveNotification | Skapa→SkickaIn→Godkänn/Avvisa; drar semestersaldo + avbokar schemapass vid godkännande | `/ledighet/*` (7 routes) |
| **Lön/Payroll** | PayrollRun, PayrollResult, PayrollResultLine, SalaryCode | Skapa→Påbörja→LäggTillResultat→Beräknad→Godkänd→Utbetald (riktig motor); retroaktiv körning | `/lon/*` |
| **Schema/Tid** | Schedule, ScheduledShift, Timesheet, TimeClockEvent, ShiftSwapRequest, StaffingTemplate, FlexBalance | Grundschema + visuell redigering, ATL-varning, stämpling, flexsaldo. Behovsstyrd automatik. **Shift Bidding**: OpenShift, ShiftBid | `/schema/*`, `/tidrapporter/*`, `/stampling`, `/minsida/stampling` |
| **Ärenden** | Case, CaseApproval, CaseComment | SkapaFrånvaroärende→Godkänn. **Grievance Management**: Grievance, GrievanceInvestigation, GrievanceHearing, GrievanceAppeal — formell klagomålsprocess med utredning och överklagan | `/arenden/*`, `/godkannanden` |
| **MBL** | MBLNegotiation | Skapa→Påbörja→Avsluta→RegistreraProtokoll | `/arenden/mbl` |
| **LAS** | LASAccumulation, LASPeriod, LASEvent | Perioder, statusberäkning, företrädesrätt; auto-kedja via anställningsevents; HR-varningar | `/las` |
| **HälsoSAM/Rehab** | RehabCase, RehabUppfoljning | Milstolpar förankrade i sjukdag 1 (dag 14/90/180/365), auto-triggning, FK-anmälan | `/halsosam/*` |
| **Kompetens** | Skill, EmployeeSkill, PositionSkillRequirement, Certification, MandatoryTraining | Gap-analys, certifieringsstatus, kopplad till medarbetarsamtal→utvecklingsplan | `/kompetens/*` |
| **Medarbetarsamtal** | PerformanceReview | Skapa→Självbedömning→Chefsbedömning→Genomförd | `/medarbetarsamtal/*` |
| **360-feedback** | FeedbackRound, FeedbackResponse | Skapa→Öppna→Stäng, betyg 1-5 | `/medarbetarsamtal/360` |
| **Pulsundersökning** | PulseSurvey, PulseSurveyQuestion, PulseSurveyResponse, PulseSurveyAnswer | Skapa→Öppna→Svara→Stäng. Anonyma svar. | `/admin/pulsundersokning/*` |
| **Policyer** | Policy, PolicyConfirmation | Skapa→Publicera→Arkivera, bekräftelse per anställd | `/dokument/policyer/*` |
| **Dokument & e-arkiv** | Document, DocumentTemplate, ArchivedDocument | Filuppladdning, mallgenerering; oföränderligt e-arkiv (arkivlagen) + gallringsfrist + legal hold | `/dokument/*`, `/dokument/earkiv` |
| **Notiser** | Notification, NotificationTemplate, NotificationPreference, **PushSubscription** | Skapa, markera läst, personliga preferenser, **web push** (VAPID) till mobil/webbläsare | `/notiser/*` |
| **Positioner** | Position, PositionHistorik, HeadcountPlan | Skapa→Tillsatt/Vakant/Frys | `/positioner` |
| **Successionsplanering** | SuccessionPlan | Position→Kandidat, beredskapsnivå | `/admin/succession` |
| **Rekrytering** | Vacancy, Application, OnboardingChecklist, Scorecard, InterviewSchedule, ReferenceCheck | Publicera→TaEmotAnsökan→Pipeline→Tillsätt→**skapar riktig anställd + onboarding** | `/rekrytering/*` |
| **Resor** | TravelClaim, ExpenseItem | Skapa→SättTraktamente→SkickaIn→Attestera (ingen självattest); kvittouppladdning; utlandstraktamente | `/resor` |
| **Offboarding** | OffboardingCase, OffboardingItem | Skapa (auto 8 steg)→MarkeraSomPågår→Slutför | `/offboarding/*` |
| **Löneöversyn** | SalaryReviewRound, SalaryProposal | Skapa→FackligAvstämning→Godkänd→Genomförd (applicerar ny lön); filimport av förslag | `/loneoversyn` |
| **Löneflöden** | Loneutmatning, Fackavgift, Facktillhorighet, Tolkersattning, ArvodePolitiker | Utmätning (KFM) + fackavgift i lönekörningen; tolkersättning; ersättning förtroendevalda | `/lon/utmatning`, `/lon/facktillhorighet`, `/lon/tolkersattning`, `/lon/fritidspolitiker` |
| **Pension** | (beräknad tjänst) | AKAP-KR premie (6 %/31,5 %, IBB 2026) + pensionsredovisningsfil | `/lon/pension` |
| **Benefits** | Benefit, EmployeeBenefit | Anmäl→Godkänn | `/formaner/*` |
| **Friskvård** | WellnessClaim | Skapa→Godkänn/Avvisa, max 5000 kr/år | `/formaner/friskvard` |
| **Försäkringar** | InsuranceCoverage | TGL, AGS, TFA, AFA, PSA | `/formaner/forsakringar` |
| **Anslagstavla** | Announcement | Skapa→Publicera→Arkivera, prioritetsnivåer | `/admin/anslagstavla` |
| **Peer Recognition** | Recognition | Ge beröm till kollega med kategori | `/admin/berom` |
| **Delegering** | DelegatedAccess | Skapa→ÄrGiltig→Avsluta | `/admin/delegering` |
| **E-learning** | Course, CourseEnrollment, LearningPath, Lesson, ScormPackage, ExternalParticipant | Anmäl→Påbörja→Genomförd; kursinnehåll/SCORM + externa deltagare | `/utbildning/*` |
| **GDPR** | DataSubjectRequest, RetentionRecord | Skapa→Tilldela→Slutför, registerutdrag | `/gdpr` |
| **KLASSA** | Informationsklassning (K/R/T nivå 1-4) | Informationssäkerhetsklassning av känsliga datamängder | `/admin/klassa` |
| **Audit** | AuditEntry | Create/Update/Delete-logg | `/audit` |
| **Talangpool** | TalentPoolEntry | Kandidater för framtida rekrytering | `/rekrytering/talangpool` |
| **Flight Risk** | (beräknad tjänst) | 4 signaler: tenure, anställningsform, bristyrke, deltid | `/rapporter/flight-risk` |
| **Workforce Planning** | HeadcountPlan | Budget per enhet per år. **Scenarios** — what-if-analys | `/rapporter/workforce-plan` |
| **Provisionering** | ProvisioningRule, ProvisioningEvent | Lokal registrering (ej extern AD/SCIM) | `/admin/provisionering` |
| **Journeys** | JourneyTemplate, JourneyInstance | Onboarding/offboarding-mallar med steg | `/journeys/*` |
| **Migreringsmotor** | MigrationProject, MigrationMapping, MigrationRun, m.fl. | PAXml 2.0, HEROMA (import→riktiga anställda), Personec P, Hogia, Fortnox, SIE 4i, Workday, SAP, Oracle, generisk CSV | `/admin/migration/*` |
| **Automatiseringsramverk** | AutomationRule, AutomationExecution, AutomationSchedule | Notify/Suggest/Autopilot, 22 regler, konfigurerbar per kategori | `/admin/automation/*` |
| **Pluggbara kollektivavtal** | CollectiveAgreement, AgreementRule, LokalAvtalsAvvikelse | AB, HÖK m.fl. DB-driven; lokala avtalsavvikelser per enhet | `/admin/avtal/*` |
| **Compensation Suite** | SalaryBand, BonusProgram, TotalRewardsStatement, CompensationModel | Löneband, bonus, total rewards, modellering | `/kompensation/*` |
| **Benefits Engine** | BenefitPlan, EligibilityRule, LifeEvent, EnrollmentWindow, BenefitStatement | Eligibility rules, life events, enrollment, statements | `/formaner/engine/*` |
| **Enterprise Analytics & BI** | KpiDefinition, PredictiveModel, AnalyticsDashboard | KPI:er, prediktiva modeller, self-service rapportbyggare, **BI/DW-export** + realtids-beslutsstöd | `/analytics/*`, `/rapporter/beslutsstod` |
| **VMS/Inhyrd personal** | Vendor, FrameworkAgreement, RateCard, ContingentWorker | Leverantörer, ramavtal, rate cards. **F-skatt Compliance** | `/vms/*` |
| **Avancerad WFM** | DemandForecast, FatigueScore, SchedulingRun, ShiftBid | Demand forecasting, fatigue scoring, optimering | `/schema/wfm/*` |
| **Talent Marketplace** | CareerPath, InternalPosting, Mentorship, SkillIntelligence | Karriärvägar, intern mobilitet, mentorskap | `/talang/*` |
| **Plattform** | WebhookSubscription, WebhookDelivery, ApiKey, CustomObject | Webhooks (HMAC-SHA256), API-nycklar, custom objects | `/admin/plattform/*` |
| **Integrationsramverk** | IntegrationDefinition, IntegrationRunLog, OutboxMessage | Register över 27 integrationer, SFTP-transport, jobb-runner, körningslogg, övervakning + omkörning | `/integrationer/oversikt` |
| **HR Service Delivery** | ServiceRequest, ServiceCategory, SLADefinition, HRQueue, CaseTemplate | Ärenderutt med SLA, agentarbetsyta, CSAT-mätning, mallar | `/helpdesk/*` |
| **AI HR-assistent** | KnowledgeArticle, ConversationSession, AssistantAction | Kunskapsartiklar, konversationspersistens, åtgärdsförslag | `/kunskapsbas/*` |
| **ONA** | (beräknad tjänst) | Organisational Network Analysis — samarbetsmönster | `/rapporter/ona` |

### Integrationer mot regionens ekosystem (IT-arkitektur "Personalstöd v7")
Filgeneratorer/adaptrar byggda och config-ready mot integrationsmotorn Health Connect (skarp nätverkstransport kräver avtal):
- **Skatteverket** — AGI-XML (SKV 269) + skattetabell; folkbokföring-import (Navet-format, skyddad identitet)
- **Nordea/bank** — pain.001.001.03 (ISO 20022) lönefil
- **KPA** — AKAP-KR pensionsredovisning
- **Försäkringskassan** — sjukanmälan/rehab-underlag
- **CGI-Raindance** — konteringsfil (Raindance-CSV eller SIE typ 4)
- **KOLL (RÖL katalogtjänst)** — anställningsmasterdata-export
- **SCB** — lönestatistik + sjuklönestatistik
- **SKR** — novemberstatistik
- **BI/DW** — dimensionsmodellerad export (CSV+JSON) för Power BI/Diver + realtids-dashboard

### Rapporter & Analytics (DB-backed)
Alla rapportvyer läser från verklig DB-data:
- **Workforce Analytics** — headcount, anställningsformer, snittålder, per-enhet-breakdown
- **Lönekartering** — löneskillnadsanalys per befattning (diskrimineringslagen)
- **Kostnadssimulering** — total lönekostnad + AG-avgifter per enhet
- **Beslutsstöd** — realtids-KPI:er (personalomsättning, sjukfrånvaro %, bemanningsgrad, LAS-risk, lönekostnad/enhet)
- **EU Pay Transparency** — lönetransparensrapportering enligt EU-direktivet 2023/970, pay gap-analys per kohort

### Auth & personalisering
- **Rollbaserad session** (ClaimsPrincipal via AuthenticationStateProvider) med central path→roll-policy — Anställd når aldrig lön/audit/admin ens via direkt-URL
- **Demo-inloggning** med profilval (Anna/Anställd, Eva/Chef, Karl/HR, Admin); **Entra/OIDC config-ready** för skarp federation
- **MinSida** — schema, lön, ledighet, ärenden, profil, lönespecifikationer, stämpling, saldon
- **Chefsportal** — teamvy filtrerad på chefens enhet (org-scoping), frånvarokalender, godkännanden

### Internationalisering (i18n)
- **329/330 nycklar** i sv + en (SharedResources.sv.resx / SharedResources.en.resx)
- NavMenu, TopBar, formulärlabels, felmeddelanden — allt via IStringLocalizer
- Språkväxling via cookie + page reload; förberett för fler språk (lägg till .resx-fil)

### Infrastruktur
- **CI/CD** — GitHub Actions (build + test + publish)
- **Docker Compose** — PostgreSQL + app
- **PWA** — service worker med offline-cache, background sync, push-notiser (Web Push), bottom navigation
- **Säkerhet** — CSP headers, rate limiting, X-Frame-Options, CSRF; InMemory-DB hård-failar i produktion
- **Drift** — `/health` (liveness) + `/health/ready` (readiness), `/admin/drift-status`, reservlöneplan (`docs/drift-reservloneplan.md`)
- **Bakgrundsjobb** — NotificationReminder, RetentionCleanup, CertificationReminder, LASAlert, RehabAutoTrigger

### Trust & Security
OpenHR har en dedikerad [/trust](/trust)-sida med säkerhetsarkitektur, OWASP ASVS-självbedömning, GDPR-complianceguide, DPA-mall och deployment-guide. Se även `docs/security/`.

### Uttryckligen utanför nuvarande scope (kräver kostnad/avtal)
Dessa kräver betalda avtal eller livekopplingar som regionen tecknar — filformaten och adaptrarna bakom dem är byggda och config-ready, men den skarpa transporten är medvetet inte aktiverad:
- Riktig BankID/SITHS-legitimation (nuvarande inloggning är demo-simulering; Entra/OIDC är config-ready)
- Skarp nätverkstransport till bank, Försäkringskassan, KPA, Skatteverket/Navet, Inera/HSA och RÖL:s Health Connect
- Native mobilapp (PWA med offline-stöd och push-notiser används i stället)
- Horisontell skalning för 11 000 samtidiga användare (driftbeslut: 2–4 instanser bakom lastbalanserare)

## Tech Stack

| Komponent | Teknologi |
|-----------|-----------|
| Backend | .NET 9, ASP.NET Core |
| Frontend | Blazor Server, MudBlazor 9.1 |
| Databas | PostgreSQL 17 |
| ORM | EF Core 9 med migrationer |
| Arkitektur | Modulär monolit (38 moduler) |
| Tema | Nordic Refined (light/dark mode) |
| Auth | Rollbaserad (ClaimsPrincipal); demo-login + Entra/OIDC config-ready |
| i18n | 329/330 nycklar, sv + en (IStringLocalizer) |
| PWA | Offline-cache, push-notiser, background sync |
| CI/CD | GitHub Actions |
| Container | Docker Compose |
| Licens | AGPL-3.0 |

## Snabbstart

### Med Docker (rekommenderat)
```bash
docker compose up -d
```
Öppna http://localhost:5076

### Utan Docker
```bash
dotnet build RegionHR.sln
dotnet run --project src/Web/RegionHR.Web.csproj
```
Öppna http://localhost:5076/login

### Demo-användare
Logga in via "BankID" (simulerad — vänta igenom animationen) och välj profil:

| Användare | Roll | Ser |
|-----------|------|-----|
| Admin | Admin | Allt |
| Karl Berg | HR | Personal, Lön, Admin |
| Eva Nilsson | Chef | Team, Godkännanden (egen enhet) |
| Anna Svensson | Anställd | Min sida, Ledighet |

## Datamodell

**240 domänfiler** fördelade på 38 moduler. Alla med EF Core-konfiguration och seeddata.

Nyckelentities (urval): Employee, Employment, OrganizationUnit, PayrollRun, PayrollResult, LeaveRequest, VacationBalance, Case, ScheduledShift, Timesheet, FlexBalance, Position, Vacancy, TravelClaim, OffboardingCase, RehabCase, LASAccumulation, Certification, Skill, Course, Notification, PushSubscription, AuditEntry, Document, ArchivedDocument, CollectiveAgreement, Loneutmatning, Fackavgift, IntegrationRunLog, SalaryReviewRound, KpiDefinition, Grievance, OpenShift, ShiftBid, PayTransparencyReport.

## Utveckling

```bash
dotnet build RegionHR.sln       # 0 errors
dotnet test RegionHR.sln        # 2 165 tester, 0 failures
dotnet run --project src/Web/RegionHR.Web.csproj
```

## Licens

AGPL-3.0 — Alla forks måste hålla koden öppen.
