# Reservlöneplan & driftkontinuitet (DR) — lön måste ut

> Syfte: säkerställa att **lön betalas ut i tid även om OpenHR eller dess databas
> är nere**. Löneutbetalning är en oeftergivlig, tidsstyrd process (utbetalning
> den 25:e). Denna plan beskriver hur utbetalning genomförs manuellt när systemet
> inte kan köra en ny lönekörning, samt hur driften härdats för att upptäcka
> haverier tidigt.

Skapad som del av drifthärdnings-slicen (Våg 3). Ersätter inget befintligt dokument.

---

## 1. Skyddsmål (RTO/RPO)

| Mål | Värde | Motivering |
|-----|-------|-----------|
| **RTO** (max acceptabel nedtid för utbetalning) | Utbetalning får **inte** missa bankdagen före den 25:e | Kollektivavtal/AB: lön ska vara disponibel senast utbetalningsdag |
| **RPO** (max acceptabel dataförlust) | Senast **godkända och exporterade** lönekörning | pain.001-filen är den auktoritativa artefakten, inte databasen |
| **Beslutsdeadline** för att aktivera reservplan | Bankdag −2 kl. 12:00 | Ger tid för manuell inlämning + bankens klipptider |

Kärnprincip: **den senast godkända pain.001-filen är den sanna, utbetalbara
artefakten.** Så länge den finns säkrad kan lön betalas ut helt utan OpenHR.

---

## 2. Varför systemet inte längre kan "misslyckas tyst"

Tidigare startade appen med en tom **InMemory-databas** i alla miljöer när
PostgreSQL var otillgänglig — lönedata hade då kunnat hamna i flyktigt minne utan
att någon märkte det. Detta är nu åtgärdat:

- **Produktion/Staging hård-failar** vid otillgänglig databas (loggar `FATALT …`
  och avslutar) i stället för att servera en tom InMemory-databas. Se
  `StartupDatabaseGuard`.
- **Endast Development** får falla tillbaka på InMemory, och då med en tydlig
  varningsbanner på konsolen.
- **Health checks**: `/health` (liveness — processen lever) och `/health/ready`
  (readiness — PostgreSQL nås och kärnschemat finns). InMemory rapporteras som
  **ohälsosam** på `/health/ready`, så en felstartad instans aldrig tas i trafik.
- **Adminvy**: `/admin/drift-status` visar samma hälsokontroll i klartext.

Ett tyst haveri ersätts alltså av (a) synligt krasch-vid-start i drift och (b)
readiness som failar → lastbalanserare/orkestrering plockar bort noden.

---

## 3. Förutsättning: säkra pain.001 vid varje godkänd lönekörning

OpenHR genererar bankfil via `NordeaPainGenerator.GeneratePain001(...)`
(ISO 20022 **pain.001.001.03**). Rutin:

1. Vid varje **godkänd** lönekörning exporteras pain.001-filen.
2. Filen arkiveras **utanför** applikationsdatabasen på minst två platser:
   - primär: säker filarea/objektlager med versionering,
   - sekundär: krypterad kopia hos löneansvarig (offline/annan site).
3. Arkivera även **utbetalningsunderlaget** (summering per anställd: belopp,
   bankkonto, clearing) som PDF/CSV tillsammans med pain.001 — det är beviset
   som möjliggör manuell inmatning om filen inte kan läsas in i banken.
4. Behåll de **tre senaste** månadernas filer (retroaktiv rättning kan behövas).

> RPO uppnås genom att denna arkivering sker **innan** OpenHR kan gå ner.
> Ingen pain.001 = ingen reservplan; steg 3 är därför obligatoriskt vid varje
> körning, inte bara vid incident.

---

## 4. Incidentflöde — lön betalas ut trots att OpenHR är nere

**Roller:** Incidentledare (IT-drift), Löneansvarig (HR/lön), Attestant (chef
med utbetalningsmandat), Bankkontakt (Nordea Corporate).

### Steg
1. **Upptäck** — larm från `/health/ready` (503) eller uteblivet `/health`-svar,
   alternativt manuell observation. Notera tidpunkt.
2. **Bedöm** — kan systemet återställas före beslutsdeadline (avsnitt 1)?
   - **Ja** → följ återställning (avsnitt 5), kör ordinarie körning.
   - **Nej** → aktivera reservplan (fortsätt).
3. **Hämta senaste godkända pain.001** + utbetalningsunderlag från arkivet
   (avsnitt 3). Verifiera checksumma/att filen är fullständig.
4. **Rimlighetskontroll** (fyra ögon): Löneansvarig + Attestant jämför totalbelopp
   och antal betalningsmottagare mot föregående månad. Avvikelse > tröskel utreds.
5. **Manuell bankinlämning** hos Nordea Corporate:
   - **Alternativ A (helst):** ladda upp den arkiverade pain.001-filen direkt i
     bankens företagsportal.
   - **Alternativ B (om filen är oläsbar/föråldrad):** mata in betalningar manuellt
     från det arkiverade utbetalningsunderlaget (batch eller per mottagare).
6. **Attest i banken** enligt bankens tvåmansregel. Bekräfta klippt tid/valutadag.
7. **Dokumentera** — spara kvittens från banken i incidentloggen; notera vilken
   pain.001 (period + checksumma) som användes.
8. **Kommunicera** — informera berörda chefer/anställda om ev. avvikelser
   (t.ex. att retroaktiva justeringar hanteras i nästa ordinarie körning).

### Efterarbete när OpenHR är uppe igen
9. Registrera i systemet **att utbetalning skedde manuellt** för perioden.
10. Alla ändringar som inte hann med (nyanställda, avslut, avvikelser) hanteras som
    **retroaktiva justeringar** i nästa körning — inte genom att köra om samma
    period mot banken (dubbelutbetalningsrisk).

---

## 5. Återställning av OpenHR (om tid finns före deadline)

1. Diagnostisera via `/admin/drift-status` eller `/health/ready` — visar om felet
   är anslutning (PostgreSQL nere) eller schema (tabeller saknas).
2. **PostgreSQL nere:** starta om/failover databasen; återställ från senaste
   nattliga backup vid behov. Appen hård-failar tills databasen svarar (medvetet).
3. **Schema saknas** (t.ex. efter en redeploy som wipeat DB — projektet använder
   `EnsureCreatedAsync` + seed): starta appinstansen, som återskapar schema och
   seedar. Läs in senaste dumpen av produktionsdata.
4. Verifiera att `/health/ready` blir grön (Frisk) innan noden åter tas i trafik.
5. Kör ordinarie lönekörning, godkänn, exportera och **arkivera pain.001 på nytt**
   (avsnitt 3).

---

## 6. Snabb checklista (skriv ut / anslå)

- [ ] Senaste godkända **pain.001** finns arkiverad på ≥ 2 platser
- [ ] Utbetalningsunderlag (belopp + konton) arkiverat bredvid filen
- [ ] Beslut om reservplan taget senast **bankdag −2 kl. 12:00**
- [ ] Fyra-ögon-kontroll av totalbelopp mot föregående månad
- [ ] Manuell inlämning + bankattest genomförd, kvittens sparad
- [ ] Manuell utbetalning registrerad i OpenHR i efterhand
- [ ] Diff mot systemet hanteras som **retroaktiv** justering nästa körning

---

## 7. Övning & ägarskap

- **Ägare:** Lönechef (process) + IT-driftansvarig (teknik).
- **Öva** manuell inlämning (Alternativ B) minst **en gång per år** mot bankens
  testmiljö, samt vid byte av bank/format.
- Revidera denna plan när bankintegration, format (pain.001-version) eller
  utbetalningsdatum ändras.
