# Wave 7 — Slice `webpush`: Web Push-notiser (VAPID + service worker + sändning)

## Sammanfattning
Full Web Push-kedja (RFC 8030/8291/8292) ovanpå den befintliga `PushSubscription`-entiteten:

1. **Sändning** via FOSS-paketet **`WebPush`** (aes128gcm + VAPID) — `WebPushSender` gör det
   enda nätverksanropet; allt ovanför (payload, VAPID-nycklar, fan-out) är rent och enhetstestat.
2. **DEMO-VAPID-nyckelpar** genererat (giltigt P-256) och inbakat i `VapidKeyProvider` så push
   fungerar direkt på localhost. **Tydligt märkt DEMO** — produktion sätter egna nycklar via config
   (`WebPush:PublicKey`/`PrivateKey`, privat nyckel via secret).
3. **Klient-JS** (`push-notifications.js`, omskriven) prenumererar och **returnerar** subscription
   till Blazor-komponenten över SignalR-kretsen (IJSRuntime) — **ingen HTTP** — som sparar via
   `IDbContextFactory` (arkitekturregeln "ALDRIG HTTP" respekterad).
4. **Mottagning**: den redan registrerade root-service-workern `/service-worker.js` (registreras i
   `App.razor`) har redan en korrekt `push`+`notificationclick`-hanterare → **återanvänds** (regeln
   "återuppfinn inte"). Nyskapad `/sw-push.js` är en fristående push-only-worker som klient-JS
   faller tillbaka på **endast** om ingen root-worker finns (t.ex. avskalad deploy).
5. **Koppling notis→push**: `PushDispatchService.DispatchAsync(Notification)` pushar en notis till
   alla aktiva prenumerationer och avaktiverar utgångna (HTTP 404/410). `NotificationService`
   (impl. av den befintliga men oimplementerade `INotificationService`) persistar OCH pushar i
   samma anrop — den återanvändbara vägen "när en Notification skapas, pusha den också".

Registrerings-UI ligger som `PushDeviceCard`-komponent inbäddad på `/notiser/installningar`
(Aktivera / Inaktivera / Skicka testnotis). Testnotis-knappen kör HELA kedjan skarpt:
skapar en riktig `Notification`, sparar den, och pushar den via `PushDispatchService`.

## Nya filer
- `src/Modules/Notifications/Domain/PushPayload.cs` — ren payload-byggare (`ToJson`, `FromNotification`), testad.
- `src/Infrastructure/Notifications/VapidKeyProvider.cs` — VAPID-nycklar (demo-fallback + config), `CreateVapidDetails`/`CreateSignedHeaders`.
- `src/Infrastructure/Notifications/WebPushSender.cs` — `WebPush`-wrapper, `PushSendResult` (Sent/Expired/Failed).
- `src/Infrastructure/Notifications/PushDispatchService.cs` — fan-out till aktiva prenumerationer + auto-avaktivering av utgångna.
- `src/Infrastructure/Notifications/NotificationService.cs` — `INotificationService`-impl (persist + push).
- `src/Web/wwwroot/sw-push.js` — fristående push-only service worker (fallback).
- `src/Web/Components/Pages/Notiser/PushDeviceCard.razor` — registrerings-/test-komponent.
- Tester: `tests/Notifications.Tests/PushPayloadTests.cs`, `VapidKeyProviderTests.cs`, `PushDispatchServiceTests.cs`.

## Ändrade filer (inom slice)
- `src/Web/wwwroot/js/push-notifications.js` — omskriven: `getSubscription()` returnerar keys till
  Blazor (via `subscription.toJSON()` = base64url, exakt vad server-libbet vill ha) i stället för att
  POST:a; `ensureRegistration()` föredrar root-workern och faller tillbaka på `/sw-push.js`. Inga
  kvarvarande anropare av de gamla `init`/`subscribe`-metoderna fanns (verifierat med grep).
- `src/Web/Components/Pages/Notiser/Installningar.razor` — lade till EN rad `<PushDeviceCard />`
  efter spara-knappen (inom `else`-blocket för kopplad anställd). Övrig sida orörd.

## Så byggs payloaden (det testade)
`PushPayload` serialiseras till exakt den JSON service-workern läser i sin `push`-hanterare:
`{ "title", "body", "url", "tag", "icon", "requireInteraction" }` (camelCase, null `url`/`tag`
utelämnas, svenska tecken behålls literalt via `UnsafeRelaxedJsonEscaping`).
`FromNotification` mappar `Title/Message/ActionUrl(→"/notiser" som fallback)/Id(→tag)` och sätter
`requireInteraction=true` för `NotificationType.Action`.

## VAPID / demo-nycklar
`VapidKeyProvider` läser `WebPush:Subject/PublicKey/PrivateKey` ur config; saknas publik ELLER privat
nyckel används **hela** demo-paret (så halvorna aldrig mismatchar). `IsUsingDemoKeys` exponeras och
`PushDeviceCard` visar en tydlig info-banner när demo-nycklar körs.
- DEMO PublicKey: `BMK8fLgyA2D-a9cZBF1nE61YjqiFb96L_WVojktuFbJl2Lup-wHKfBKtAVHy5Uos_5id5O11cfZs7JhSkNVCqFM`
- DEMO PrivateKey: `j5m9KAKpZU7bzpPsXcp6q3MCRp0Wnr18rZ16Q9-Ue1U`  ⚠️ **DEMO — byt i produktion.**

`CreateSignedHeaders(audience)` bygger de signerade VAPID-headrarna (ES256-JWT) utan nätverk — testet
använder den för att bevisa att demo-nyckelparet är kryptografiskt giltigt.

---

## INTEGRATION — snuttar till förbjudna filer

### package_refs
`Directory.Packages.props` (ny rad, t.ex. efter `<!-- Email -->`-blocket):
```xml
<!-- Web Push -->
<PackageVersion Include="WebPush" Version="1.0.13" />
```
`src/Infrastructure/RegionHR.Infrastructure.csproj` (ny rad i paket-`<ItemGroup>`):
```xml
<PackageReference Include="WebPush" />
```
> `WebPush` 1.0.13 har explicit **net9.0**-target och enda beroendet `Portable.BouncyCastle`
> (MIT/FOSS). API:t (`WebPushClient.SendNotificationAsync(sub, payload, VapidDetails, ct)`,
> `VapidHelper.GetVapidHeaders(...)`, `new PushSubscription(endpoint, p256dh, auth)`) är verifierat
> mot källan och matchar koden. Tester behöver INTE egen paketreferens (går transitivt via Infrastructure).

### di_registrations
`src/Infrastructure/DependencyInjection.cs` — lägg bredvid befintliga Notifications-registreringar
(kring rad 122–125, efter `services.AddSingleton<SmsNotificationSender>();`):
```csharp
// Web Push (VAPID + sändning). VapidKeyProvider läser "WebPush"-config, demo-fallback annars.
services.AddSingleton<RegionHR.Infrastructure.Notifications.VapidKeyProvider>();
services.AddSingleton<RegionHR.Infrastructure.Notifications.WebPushSender>();
services.AddScoped<RegionHR.Infrastructure.Notifications.PushDispatchService>();
// Återanvändbar väg: persistar en notis OCH pushar den till prenumererande enheter.
services.AddScoped<RegionHR.Notifications.Services.INotificationService,
                   RegionHR.Infrastructure.Notifications.NotificationService>();
```
> Livstider: VapidKeyProvider/WebPushSender = Singleton (WebPushClient återanvänds); PushDispatchService
> + NotificationService = Scoped (skapar egna DbContext via `IDbContextFactory`). Inga captive deps.

### dbsets
Inga nya. `DbSet<PushSubscription> PushSubscriptions` finns redan i `RegionHRDbContext` (rad 119)
och `PushSubscriptionConfiguration`/kolumner är oförändrade.

### nav_entries
Inga. Registrerings-UI ligger på befintliga `/notiser/installningar`.

### route_policy
Inga ändringar behövs. `/notiser` = `AllaInloggade` i `RouteAccessPolicy.cs` (rad 77) täcker
`/notiser/installningar`. Komponenten kräver dessutom `Auth.EmployeeId` (kopplad anställd).

### program_cs_changes
Inga. Klient-JS laddas redan via `App.razor` (`<script src="js/push-notifications.js">`), och
root-service-workern registreras redan där. `sw-push.js` serveras av befintliga `UseStaticFiles()`.

### config (valfritt — produktion)
`src/Web/appsettings.json` (frivillig sektion; utan den körs demo-nycklarna):
```json
"WebPush": {
  "Subject": "mailto:noreply@din-region.se",
  "PublicKey": "<egen base64url VAPID public key>",
  "PrivateKey": "<egen base64url VAPID private key — via secret/env i produktion>"
}
```
Generera ett par med `WebPush.VapidHelper.GenerateVapidKeys()`.

## Auto-push på fler notis-skapande-ställen (valfritt, framtid)
Notiser skapas idag ad hoc som `db.Notifications.Add(Notification.Create(...))` på flera ställen
(t.ex. `LedighetService`, `FlexService`, `NotificationReminderService`). För att även dessa ska
pusha: injicera `PushDispatchService` och lägg `await pushDispatch.DispatchAsync(notis, ct);` direkt
efter `SaveChangesAsync()`. Alternativt migrera anropet till `INotificationService.SendAsync(...)`
som gör båda. Lämnas som notering — dessa filer ägs av andra slices och rörs inte här.

## Build-risk
Låg. All ny kod matchar verifierade signaturer. **Enda** hårda beroendet är att integratören lägger
`WebPush`-paketet (package_refs ovan) före bygget — annars fallerar `using WebPush;` i Infrastructure.
Inget bygge körts lokalt (maskinfrys-regeln).
