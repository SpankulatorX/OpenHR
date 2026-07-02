# Våg 3 — Slice: ledighet (Ledighet → saldo + schema)

## Revisionens brist som stängs
"Godkänd semester drar aldrig saldo, påverkar inte schema, genererar ingen
lönetransaktion, notifierar inte." — Godkännandet i `Ledighet/Index.razor`
kallade bara `LeaveRequest.Godkann()` + `SaveChanges` och ingenting mer.

## Vad som nu sker vid GODKÄNNANDE
1. **Saldo** — endast `LeaveType.Semester` drar från `VacationBalance` (via
   `RegistreraUttag`). VAB, föräldraledighet, sjukfrånvaro m.fl. rör aldrig
   semestersaldot. Räcker inte saldot avbryts hela godkännandet (inget sparas).
2. **Schema** — överlappande, **planerade** schemapass (`ScheduledShift`) i
   perioden markeras `ShiftStatus.Avbokad`. Pågående/avslutade/redan avbokade/bytta
   pass lämnas orörda. Detta gör att lönekörningen (`PayrollInputBuilder` som redan
   exkluderar `Avbokad`/`Bytt`) inte dubbelräknar OB för pass den anställde inte
   arbetar, och schemavyn stämmer.
3. **Lönetransaktion** — kräver ingen ny kod: `PayrollInputBuilder` läser redan
   `LeaveRequestStatus.Godkand` som frånvaro (Sjuk/Semester/Föräldra). Statusen
   sätts fortsatt till `Godkand`, så löneunderlaget fångar semestern automatiskt.
4. **Notifiering** — den anställde (`Notification.UserId = request.AnstallId`) får
   en InApp-notis om godkännande (`Info`) resp. avslag (`Warning`), actionUrl
   `/minsida/ledighet`.

Allt persisteras atomiskt i **en** `SaveChanges` per åtgärd.

## Arkitektur
- **Domänlogiken** (statusövergång + saldo + val av berörda pass) ligger i
  `RegionHR.Leave.Services.LedighetGodkannandeService` (ren, DbContext-fri,
  enhetstestad). Schemapass abstraheras via `IPaverkbartPass` så logiken kan testas
  utan Scheduling-beroende.
- **Webborkestreringen** ligger i `RegionHR.Web.Services.LedighetService` som läser/
  uppdaterar `LeaveRequests`, `VacationBalances`, `ScheduledShifts` och `Notifications`
  via `IDbContextFactory<RegionHRDbContext>`, och adapterar `ScheduledShift` →
  `IPaverkbartPass` (inre klass `ScheduledShiftPass`, sätter `Status = Avbokad`).

## KRÄVER integration i skyddad fil (Program.cs)
`src/Web/Program.cs` — lägg till bredvid övriga `AddScoped`-registreringar
(t.ex. efter rad 72 `builder.Services.AddScoped<FlexService>();`):

```csharp
builder.Services.AddScoped<LedighetService>();
```

(`LedighetGodkannandeService` behöver INGEN registrering — den `new`:as internt.)

Utan denna rad kastar `Ledighet/Index.razor` vid rendering (DI kan inte lösa
`LedighetService`). Bygget påverkas inte, bara körtiden.

## Inga andra skyddade filer berörda
Inga ändringar i DependencyInjection.cs, RegionHRDbContext.cs, SeedData.cs,
NavMenu.razor, *.csproj eller Directory.*.props. Inga nya DbSets, inga nya
paket, inga nya seeds (alla entiteter fanns redan).

## Filer
Nya:
- src/Modules/Leave/Services/IPaverkbartPass.cs
- src/Modules/Leave/Services/LedighetGodkannandeService.cs
- src/Web/Services/LedighetService.cs
- tests/Leave.Tests/LedighetGodkannandeServiceTests.cs

Ändrade:
- src/Web/Components/Pages/Ledighet/Index.razor (Godkann/Neka → LedighetService)
