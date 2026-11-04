# Workflow Tracker (.NET 8 / WPF)

Een Windows-app met een zwevende Play/Pauze-knop. Google Gemini leest de huidige screenshot en bouwt een doorlopend rapport op van wat de gebruiker doet en waarschijnlijk probeert te bereiken. Elke volgende screenshot wordt expliciet gekoppeld aan de vorige analyseresultaten. De webhook ontvangt **standaard alleen tekst**, inclusief het bijgewerkte rapport. Ook lopend werk wordt gerapporteerd.

## Downloaden via GitHub

Open [Actions > Build Windows downloads](https://github.com/niektuytel/TellX.Screener/actions/workflows/build.yml), kies een geslaagde run en download onder **Artifacts** de `WorkflowTracker-win-x64.zip` voor een gewone 64-bits pc. Pak de ZIP uit en dubbelklik op `WorkflowTracker.exe`. Ook x86, ARM64, broncode inclusief skill en checksums zijn beschikbaar. GitHub-aanmelding is vereist voor Actions-downloads; ze blijven 30 dagen beschikbaar.

Iedere push naar `main` krijgt automatisch een hogere appversie en publiceert na geslaagde tests een nieuwe [Release](https://github.com/niektuytel/TellX.Screener/releases). Download zonder GitHub-aanmelding de [laatste stabiele x64-ZIP](https://github.com/niektuytel/TellX.Screener/releases/latest/download/WorkflowTracker-win-x64.zip). De releasetitel blijft `Workflow Tracker`; ZIPs en `WorkflowTracker.exe` hebben geen versienummer in hun naam. Een push met meerdere commits publiceert de laatste commit van die push. Zie [PUBLISHING.md](PUBLISHING.md) voor de automatische versie- en releaseprocedure. De builds zijn nog niet digitaal ondertekend.

## Starten en instellen

1. Plaats `publish\WorkflowTracker.exe` op een vaste locatie en dubbelklik. Er is geen aparte .NET-installatie of administratoraccount nodig.
2. Vul je Google Gemini API-key in. De Google-endpoint, het model en de webhook zijn vooraf ingevuld. Zonder Google API-key begint de app niet met opnemen.
3. Klik op **Opslaan**. Klik op de zwevende knop voor Play/Pauze, sleep om te verplaatsen en rechtsklik voor instellingen of afsluiten. Groen/pauzepictogram = actief; donker/playpictogram = handmatig gepauzeerd; amber/playpictogram = automatisch gepauzeerd tot de volgende starttijd.

Het instellingenvenster toont alleen de API-key en webhook-URL. Onder het standaard ingeklapte **Geavanceerd** staan het dagelijkse opnamevenster, het activiteitsfilter, het opname-interval, de HTTP-timeout, de analyseprompt, het contextprefix, het model, de Vision-endpoint, een optionele webhook-token en de checkbox **Screenshots meesturen naar webhook**. Die laatste checkbox staat standaard **uit**. De knop **Standaardopname herstellen** herstelt interval, timeout, dagvenster en activiteitsfilter; prompts, credentials en screenshot-upload blijven behouden.

De app schrijft bij de eerste start een eigen waarde `WorkflowTracker` in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Daardoor start Play na aanmelden bij Windows automatisch zodra geldige instellingen aanwezig zijn. Pauze geldt voor de huidige sessie. Rechtsklik → **Starten bij aanmelden** schakelt toekomstige automatische starts uit; die voorkeur blijft bewaard. Windows kan autostart daarnaast via Taakbeheer/Instellingen blokkeren. Er is maximaal één instantie per Windows-sessie.

## Standaardwaarden en opslag

| Instelling | Standaard | Aanpassen |
| --- | --- | --- |
| `VisionEndpoint` | `https://generativelanguage.googleapis.com/v1beta/openai/chat/completions` | Geavanceerd |
| `VisionModel` | `gemini-3.1-flash-lite` | Geavanceerd |
| `VisionApiKey` | Leeg | Hoofdvenster instellingen; vereist voor Google |
| `WebhookUrl` | `https://tellx.nl/tuytelmachinery-assistent/screener` | Hoofdvenster instellingen |
| `WebhookBearerToken` | Leeg | Geavanceerd; optioneel |
| `CaptureIntervalSeconds` | `20` | Geavanceerd; 5–3600 seconden |
| `RequestTimeoutSeconds` | `90` | Geavanceerd; 5–600 seconden per verzoek |
| `SendScreenshots` | `false` | Geavanceerd; checkbox voor de huidige PNG naar webhook |
| `DailyScheduleEnabled` | `true` | Geavanceerd; dagelijks opnamevenster aan/uit |
| `RecordingStartTime` | `06:00` | Geavanceerd; lokale Windows-tijd in `HH:mm` |
| `RecordingEndTime` | `18:00` | Geavanceerd; lokale Windows-tijd in `HH:mm`; moet verschillen van de start |
| `SkipWhenIdle` | `true` | Geavanceerd; vóór de screenshot controleren op nieuwe muis-/toetsactiviteit |
| `AnalysisPrompt` | De ingebouwde instructie om schermtekst, activiteit en vermoedelijk doel te beschrijven | Geavanceerd; niet leeg, maximaal 10.000 tekens |
| `PreviousContextPrefix` | Inleiding die vorige analyseresultaten als samenhangende context voor de volgende screenshot aanduidt | Geavanceerd; maximaal 2.000 tekens; mag leeg zijn |

De instellingen staan voor jouw Windows-account versleuteld met DPAPI (`CurrentUser`) in `%LOCALAPPDATA%\WorkflowTracker\settings.dat`. Dezelfde ingebouwde defaults staan in `workflowtracker.defaults.json` en `Models/TrackerSettings.cs`; opnieuw publiceren is nodig om gewijzigde brondefaults in een exe op te nemen. Gebruikersinstellingen hebben voorrang. Exacte oude voorbeeld-URLs/modelnamen en oude standaardprompts worden bij laden bijgewerkt; eigen endpoints, aangepaste prompts en tokens blijven behouden.

Bij upgraden van een oudere versie worden de oude instellingen `MaxBufferedScreenshots` en `MaxBufferMegabytes` niet meer gebruikt. Er is geen PNG-geschiedenis meer. Ontbreekt `SendScreenshots` in bestaande instellingen, dan geldt automatisch `false`.

De Google-endpoint gebruikt het [officiële compatibiliteitsformaat](https://aistudio.google.com/docs/openai#image-understanding), met base64-afbeeldingen en Bearer-authenticatie. [Gemini 3.1 Flash-Lite](https://aistudio.google.com/docs/models/gemini-3.1-flash-lite) ondersteunt afbeeldingen en is op lage kosten gericht. API-verbruik hangt af van de hoeveelheid beeld- en teksttokens; zie [Google-tarieven](https://aistudio.google.com/docs/pricing#gemini-3.1-flash-lite). De checkbox beïnvloedt alleen de webhook: Google ontvangt altijd de huidige screenshot om die te kunnen lezen.

## Analyse en tekstcontext

### Automatische pauze en activiteit

Vanaf release **1.3.0** neemt de app standaard dagelijks op van **06:00 tot 18:00**, volgens de lokale Windows-tijdzone, inclusief zomer- en wintertijd. In een normale sessie stopt hij om 18:00 met opnemen en annuleert hij nog lopende AI-/webhookverzoeken. Een al door de server ontvangen upload kan niet worden teruggehaald. Zonder handmatige override worden buiten het venster geen screenshots gemaakt en geen AI- of webhookverzoeken gestart. De app blijft wel draaien en hervat om 06:00 zolang Windows en de app actief zijn. Hij wekt een slapende of uitgeschakelde laptop niet; na hervatten wordt het venster opnieuw gecontroleerd. Ook als de laptop de hele nacht sliep, begint de ochtend met een frisse rapportcontext.

Vanaf **1.3.1** start een klik op de amberkleurige Play-knop buiten het opnamevenster meteen een **handmatige sessie**. Ook vanuit een gewone pauze werkt Play buiten uren zo. Die sessie negeert het dagelijkse tijdschema en blijft actief totdat je op Pauze klikt of de app afsluit. Ook de volgende ochtend en eindtijd stoppen deze sessie niet automatisch. Het activiteitsfilter, identieke-beeldencontrole en alle overige instellingen blijven gelden. Instellingen openen/opslaan behoudt deze handmatige modus; ook remote tijdwijzigingen onderbreken hem niet.

De override geldt alleen voor de huidige sessie en wordt niet opgeslagen of via de webhook ingesteld. Bij opnieuw starten van de app of Windows geldt weer het opgeslagen dagvenster. Overdag op Play klikken start een normale sessie die op de eindtijd automatisch pauzeert. Een klik op de groene Pauze-knop stopt zowel een normale als een handmatige sessie; een gestopte sessie hervat niet vanzelf om 06:00.

De tijden gelden voor alle weekdagen. Onder **Geavanceerd** kun je het schema uitschakelen of de tijden wijzigen; een venster over middernacht is ook toegestaan.

Tijdens actieve uren vergelijkt de app vóór iedere screenshot het laatste invoertijdstip met dat bij de vorige opname. Zonder nieuwe muis- of toetsenbordactiviteit slaat hij **de screenshot, AI-analyse en webhookupload** over. Er worden geen toetsaanslagen, muiscoördinaten of ingevoerde teksten geregistreerd. De eerste opname na Play of na de nachtelijke pauze wordt wel gemaakt. Ook een retry na een AI-fout en de eenmalige heranalyse na een gewijzigde remote configuratie blijven mogelijk zonder nieuwe invoer. Is het Windows-invoertijdstip niet beschikbaar, dan vervalt het filter tijdelijk.

Invoer tijdens een lopende analyse wordt bij de volgende tick meegenomen. Na nieuwe invoer blijft de controle op identieke screenshots gelden: hetzelfde beeld veroorzaakt geen AI-aanroep. Achtergrondwijzigingen zonder invoer, zoals een afgeronde build of veranderde klok, worden met `SkipWhenIdle=true` bewust pas bij volgende gebruikersactiviteit bekeken. Zet die optie uit als zulke veranderingen ook zonder invoer moeten worden beoordeeld. Tijdens activiteitloos wachten blijft de tekstcontext behouden; de dagelijkse pauze wist context en fingerprints zodat de volgende ochtend met een nieuw rapport begint.

### De prompt en het prefix aanpassen

Open **rechtsklik → Instellingen → Geavanceerd**. In **Analyseprompt** kun je de opdracht aan het model aanpassen, bijvoorbeeld extra aandacht vragen voor machinecode, foutmeldingen of administratieve handelingen. In **Contextprefix voor de vorige opnamen** bepaal je de inleidende tekst bij de twee eerdere schermteksten, bijvoorbeeld:

```text
Gebruik deze eerdere schermteksten om de huidige handeling te begrijpen.
Ze zijn context en beschrijven niet noodzakelijk het huidige scherm:
```

Het tekstdeel van het API-verzoek wordt opgebouwd als jouw analyseprompt, daarna jouw contextprefix en vervolgens de eerdere analyseresultaten in chronologische volgorde. Daarna staat expliciet dat de huidige screenshot de volgende opname is en dat het model het voorgaande rapport moet voortzetten. De huidige afbeelding wordt afzonderlijk toegevoegd. Bij de eerste opname is er nog geen geschiedenis; dan worden alleen de analyseprompt en de huidige afbeelding verstuurd. Het contextprefix wordt dus alleen toegevoegd als er eerdere analyses zijn.

Klik op **Opslaan** om wijzigingen toe te passen; er is geen nieuwe build nodig. Tijdens instellingen wijzigen staat de opname stil en daarna hervat die met de opgeslagen prompt als hij eerder actief was. **Standaardprompts herstellen** zet uitsluitend de analyseprompt en het contextprefix terug. Bestaande instellingen zonder deze velden krijgen automatisch de standaardteksten.

De vaste systeeminstructies voor de JSON-structuur blijven apart aanwezig. Je kunt de inhoudelijke analyse sturen; de webhook blijft de velden hieronder verwachten. Een opdracht om een ander antwoordformaat te gebruiken kan daarom niet worden verwerkt.

### Scherminhoud en eerdere analyses

Iedere Play-sessie begint zonder geschiedenis. Binnen het opnamevenster maakt de app direct een screenshot en controleert daarna standaard elke **20 seconden** of er nieuwe invoer is. Per AI-aanroep wordt **één afbeelding** meegestuurd: de huidige screenshot. De volledige tekstresultaten van maximaal twee eerdere succesvolle analyses worden chronologisch toegevoegd: schermtekst, activiteit, doel, applicaties, waargenomen stappen, voltooiingsstatus, tijdstip en het eerdere doorlopende rapport. Er worden geen eerdere afbeeldingen naar Google gestuurd.

Het model vergelijkt de huidige screenshot met het vorige resultaat en werkt het veld `rapport` verder bij. Daardoor kan relevante informatie uit oudere opnamen blijven meekomen via het voorgaande rapport, ook als die opname buiten de laatste twee valt. De app nestelt geen volledige voorgeschiedenis in eerdere resultaten; het rapport blijft een samenvatting van maximaal 8.000 tekens. Bij een duidelijke taakwissel moet het model dat benoemen en alleen onderbouwde verbanden leggen. Dit verbetert de samenhang, maar blijft een AI-interpretatie van de beschikbare momentopnamen.

Het model retourneert een JSON-object met:

| Veld | Betekenis |
| --- | --- |
| `screenshot_tekst` | Werkelijk zichtbare schermtekst; maximaal 20.000 tekens |
| `activiteit` | Beschrijving van wat er nu gebeurt; maximaal 4.000 tekens |
| `doel` | Vermoedelijk doel, met onzekerheid benoemd; leeg als onbekend; maximaal 4.000 tekens |
| `applicaties` | Herkende applicaties, als stringarray |
| `stappenplan` | Alleen waargenomen handelingen, als stringarray |
| `voltooid` | Boolean; alleen `true` bij zichtbare bevestiging van voltooiing |
| `rapport` | Doorlopend, samenhangend verslag tot en met de huidige opname; maximaal 8.000 tekens |

Ook bij `voltooid: false` wordt de analyse doorgestuurd. Lege schermtekst, een onbekend doel en lege arrays zijn toegestaan. Het model mag `null` teruggeven als de screenshot niet te beoordelen is; dan volgt geen webhookaanroep. Een ongeldige analyse wordt evenmin doorgestuurd.

De laatste twee geslaagde analyses blijven in RAM, ook als een webhookupload daarvan mislukt. Een mislukte AI-analyse vervangt de geschiedenis niet. Pauze, instellingen wijzigen en afsluiten geven de teksthistorie vrij; .NET ruimt de tekstobjecten via garbage collection op. Afbeeldingen worden door de app niet op schijf opgeslagen en alleen tijdens de huidige verwerking in geheugen gehouden. De tekstgeschiedenis is geen uploadwachtrij.

## Webhook-contract

Na iedere geldige analyse verstuurt de app een HTTP POST met `multipart/form-data`. De boundary wordt door .NET ingesteld.

| Part | Content-Type | Wanneer aanwezig |
| --- | --- | --- |
| `workflow` | `application/json; charset=utf-8` | Altijd; analyse en tekstcontext |
| `screenshot` | `image/png` | Alleen als `SendScreenshots` expliciet is aangezet; uitsluitend de huidige PNG |

Voorbeeld van de inhoud van `workflow`:

```json
{
  "doel": "Waarschijnlijk een releasebuild maken",
  "applicaties": ["Visual Studio"],
  "stappenplan": ["Project bewerken", "Build starten"],
  "screenshot_tekst": "Build started...",
  "activiteit": "De gebruiker bouwt het project; het resultaat is nog niet zichtbaar.",
  "voltooid": false,
  "rapport": "De gebruiker heeft Program.cs bewerkt en vervolgens een build gestart. Het resultaat is nog niet zichtbaar.",
  "vastgelegd_op_utc": "2026-11-03T12:00:40+00:00",
  "vorige_opnamen": [
    {
      "vastgelegd_op_utc": "2026-11-03T12:00:00+00:00",
      "screenshot_tekst": "Program.cs",
      "activiteit": "Code bewerken",
      "doel": "Waarschijnlijk de applicatie aanpassen",
      "applicaties": ["Visual Studio"],
      "stappenplan": ["Program.cs bewerken"],
      "voltooid": false,
      "rapport": "De gebruiker past Program.cs aan."
    },
    {
      "vastgelegd_op_utc": "2026-11-03T12:00:20+00:00",
      "screenshot_tekst": "Build Solution",
      "activiteit": "Build starten",
      "doel": "Waarschijnlijk een releasebuild maken",
      "applicaties": ["Visual Studio"],
      "stappenplan": ["Program.cs bewerken", "Build starten"],
      "voltooid": false,
      "rapport": "De gebruiker heeft Program.cs aangepast en start nu een build."
    }
  ]
}
```

`vastgelegd_op_utc` en `vorige_opnamen` worden door de app ingevuld, niet door het model. Bij de eerste opname is `vorige_opnamen` leeg; bij de tweede bevat het één element. Daarna zijn het maximaal twee elementen. Er zit geen afbeeldingsdata of base64 in dit JSON-object.

Een eventuele webhook-token gaat als `Authorization: Bearer ...` mee. Vision en webhook gebruiken afzonderlijke tokens. Alleen HTTPS is toegestaan en automatische HTTP-redirects zijn uitgeschakeld. Elk HTTP 2xx-resultaat geldt als geslaagd. Bij HTTP 200 met JSON kan de response daarnaast instellingen wijzigen of de hieronder beschreven verwijderopdracht bevatten.

## Instellingen op afstand via de webhook

Vanaf release **1.2.0** kan de ingestelde webhook een `settings`-object teruggeven in het antwoord op een analyse-upload. Gebruik **HTTP 200** met **Content-Type: application/json**, bijvoorbeeld:

```json
{
  "settings": {
    "AnalysisPrompt": "Beschrijf de huidige activiteit en werk het doorlopende rapport bij.",
    "PreviousContextPrefix": "De onderstaande resultaten gaan chronologisch vooraf aan de huidige screenshot:",
    "VisionModel": "gemini-3.1-flash-lite",
    "CaptureIntervalSeconds": 20,
    "RequestTimeoutSeconds": 90,
    "SendScreenshots": false,
    "DailyScheduleEnabled": true,
    "RecordingStartTime": "06:00",
    "RecordingEndTime": "18:00",
    "SkipWhenIdle": true
  }
}
```

Je hoeft alleen de velden op te nemen die je wilt wijzigen. Ontbrekende velden blijven behouden. De namen zijn hoofdlettergevoelig en volgen de exacte C#-/configuratienamen hieronder.

| Toegestaan veld | Type en voorwaarden |
| --- | --- |
| `AnalysisPrompt` | Niet-lege string; maximaal 10.000 tekens |
| `PreviousContextPrefix` | String; maximaal 2.000 tekens; leeg mag |
| `VisionModel` | Niet-lege string met de gewenste modelnaam |
| `VisionEndpoint` | Volledige HTTPS chat-completions-URL zonder gebruikersgegevens in de URL |
| `VisionApiKey` | String met de Vision API-key; leeg alleen als de gekozen endpoint dat toelaat. Voor Google is een key vereist |
| `CaptureIntervalSeconds` | Geheel getal; 5–3600 |
| `RequestTimeoutSeconds` | Geheel getal; 5–600 |
| `SendScreenshots` | JSON-boolean `true` of `false`; geen string |
| `DailyScheduleEnabled` | JSON-boolean; dagelijks opnamevenster aan/uit |
| `RecordingStartTime` | String in exact `HH:mm`, bijvoorbeeld `06:00` |
| `RecordingEndTime` | String in exact `HH:mm`, bijvoorbeeld `18:00`; start en einde moeten verschillen |
| `SkipWhenIdle` | JSON-boolean; zonder nieuwe invoer de screenshot overslaan |

**Niet toegestaan:** `WebhookUrl`, `WebhookBearerToken` en alle andere/onbekende velden. De webhook kan zijn eigen bestemming of authenticatie dus niet aanpassen. `null`, dubbele sleutels en onjuiste types worden geweigerd. Een leeg `settings`-object verandert niets. Normale extra responsevelden zoals `ok` mogen naast `settings` staan. `action` en `confirm` zijn gereserveerd voor verwijderen; combineer die niet met instellingen. Een gecombineerde response activeert geen van beide acties.

De app valideert eerst de **hele samengevoegde configuratie**. Als één veld ongeldig is, wordt niets toegepast of opgeslagen. Bij een geldige wijziging worden de instellingen met DPAPI versleuteld bewaard en vervolgens direct in de lopende sessie actief. Als opslaan mislukt, blijft de vorige configuratie actief. De volgende opname gebruikt de nieuwe prompt, het model, endpoint, key, timeout en screenshotoptie; het opname-interval wordt zonder herstart aangepast. De bestaande twee eerdere resultaten en het doorlopende rapport blijven behouden, behalve als een gewijzigd dagvenster in een normale sessie meteen een automatische pauze veroorzaakt; die begint de volgende ochtend met een frisse context.

Een gewijzigde configuratie beoordeelt hetzelfde scherm één keer opnieuw. Een herhaalde response met exact dezelfde waarden veroorzaakt geen nieuwe opslag of voortdurende heranalyse van hetzelfde beeld. De lokale instellingen tonen bij opnieuw openen de bijgewerkte waarden. Expliciet gekozen prompts worden ook na herstart exact behouden.

De algemene responsebody wordt begrensd gelezen tot **128 KiB**; grotere antwoorden worden niet als instellingen verwerkt. De bestaande verwijderopdracht behoudt zijn aparte **4096-byte**-limiet en exacte tweeveldscontract. Er is geen apart bevestigingsbericht naar de server: de lokale status toont toegepast/geweigerd en foutlogs bevatten uitsluitend vaste foutteksten en fouttypen, geen responsebody of API-keys.

Dit is **responsegestuurde configuratie, geen aparte commandopoll**. Er kan alleen een update binnenkomen bij de volgende succesvolle analyse-upload. Tijdens handmatige/automatische pauze, een overgeslagen opname zonder nieuwe invoer, een identiek overgeslagen scherm, een onleesbare opname of een AI-fout wordt de webhook niet aangeroepen. Verkeerde modelnamen/API-keys kunnen toekomstige analyses blokkeren; herstel die dan via de lokale instellingen. De vaste systeeminstructies en het rapport-JSON blijven gelden bij een aangepaste prompt.

## Verwijderen via de webhookrespons

Het afgesproken contract blijft gelden, ook bij uploads zonder PNG:

```http
HTTP/1.1 200 OK
Content-Type: application/json

{"action":"uninstall","confirm":"WorkflowTracker"}
```

Alleen HTTP 200, `application/json` (eventueel met charset) en een JSON-object met exact deze twee verschillende sleutels en hoofdlettergevoelige waarden activeren verwijdering. Witruimte en veldvolgorde mogen verschillen. Extra of dubbele velden, andere waarden/types, andere statuscodes, HTML/tekst, ongeldige JSON en bodies groter dan 4096 bytes activeren niets. Bestandspaden of shellcommando's worden niet geaccepteerd.

De worker stopt direct en geeft de huidige screenshot en teksthistorie vrij. Een vast ingebouwd, verborgen Windows PowerShell-opruimproces controleert proces-ID, starttijd, exe-pad, SHA-256 en de vaste appgegevensmap. Na de ready/proceed-handshake verwijdert de app haar eigen bijpassende HKCU Run-waarde en eigen voorkeurensleutel, en sluit af. De helper wacht daarop en verwijdert de gestopte exe, eigen `settings.dat`, eventuele `settings.dat.tmp` en `logs\tracker-*.log`-bestanden. Lege appgegevensmappen worden verwijderd.

Er is geen recursieve verwijdering. De exe-map, broncode, andere bestanden, andere exe-kopieën en Windows/.NET-systeem- of extractiecaches blijven behouden. Opruimen via symlinks/junctions wordt geweigerd. Er worden geen beveiligingsinstellingen gewijzigd of administratorrechten gevraagd.

Bij een voorbereidingsfout blijft de app gepauzeerd met een melding. Bij een latere opruimfout toont de helper een melding met verwijzing naar `%TEMP%\WorkflowTracker-uninstall-error.log`. Als Windows PowerShell door je organisatie wordt geblokkeerd, is handmatig verwijderen nodig. De respons wordt alleen gelezen na een analyse-upload; er is geen aparte commandopoll.

## Fouten, timing en geheugen

- De capture-loop draait buiten de UI-thread en controleert tijdens actieve uren standaard iedere 20 seconden of een nieuwe opname nodig is. Er is maximaal één iteratie actief. Duurt analyse/upload langer dan 20 seconden, dan worden gemiste timer-ticks samengevoegd; de app bouwt geen stapel taken op.
- Een identieke opeenvolgende screenshot wordt vóór de AI-aanroep overgeslagen: geen analyse en geen webhook. De app bewaart alleen een SHA-256-fingerprint van 32 bytes. Het vergelijkt de volledige PNG van dezelfde encoder; zichtbare veranderingen, ook een knipperende tekstcursor of een veranderde taakbalkklok, gelden als nieuw beeld. Een reeks A → B → A wordt opnieuw beoordeeld zodra het beeld verandert.
- Na een mislukte AI-aanroep wordt hetzelfde beeld opnieuw geprobeerd. Na een geslaagde `null`-analyse worden identieke vervolgcaptures overgeslagen. Pauze/herstart begint een nieuwe sessie: het eerste beeld wordt weer geanalyseerd.
- Screenshot-, AI- en webhookfouten worden lokaal gelogd. De volgende tick probeert een nieuwe opname; er is geen duurzame uploadwachtrij. Een mislukte webhook wordt niet opnieuw verstuurd door een ongewijzigd beeld opnieuw te analyseren. De screenshotcontrole is geen deduplicatie op de betekenis van rapporten.
- Pauze annuleert HTTP-verzoeken en wachten en wacht op de oude loop. Een reeds door de server ontvangen verzoek kan niet worden teruggehaald.
- `Bitmap`, `Graphics` en de huidige screenshotstream worden gedisposed. Base64- en HTTP-buffers gebruiken tijdelijk extra geheugen. Er worden geen oude PNG's bewaard.
- Logs staan in `%LOCALAPPDATA%\WorkflowTracker\logs`. Bestanden ouder dan 14 dagen worden bij een volgende logactie verwijderd. Logs bevatten fouttypen/statuscodes, geen schermtekst, screenshots, tokens of API-responses.

## Taakbalk en venstergedrag

De overlay start met `ShowActivated=false` en gebruikt de native toolwindow-stijl zonder geforceerde appwindow-stijl. Zowel de overlay als het instellingenvenster hebben `ShowInTaskbar=false`. De capture-route gebruikt alleen native schermmaten, een bitmap en GDI CopyFromScreen; er wordt geen Forms.Screen, screenshotprogramma of nieuw proces gestart. Identieke statusmeldingen worden niet opnieuw op de UI gezet.

Dit voorkomt taakbalkknoppen van deze app en beperkt focuswisselingen. De gemelde taakbalkbeweging kon niet live worden gereproduceerd; dat elke beweging van een andere app hiermee verdwijnt is niet vastgesteld. De venstereigenschappen en native stijl zijn wel automatisch gecontroleerd. Zie [Microsoft over toolwindow- en appwindow-stijlen](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles).

## Windows 10 / Windows 11 en processortypen

Er is één actuele release, versie **1.3.2**. De vaste losse executable is `publish\WorkflowTracker.exe` (x64). Architectuurpakketten van dezelfde bronversie staan onder `publish\packages`; elke ZIP bevat precies één `WorkflowTracker.exe`, inclusief de benodigde runtime.

| Pakket | Voor welke computer |
| --- | --- |
| `WorkflowTracker-win-x64.zip` | Gebruikelijke 64-bits Windows-pc met Intel/AMD; dezelfde build voor Windows 10 en Windows 11 |
| `WorkflowTracker-win-x86.zip` | 32-bits Windows 10; ook bruikbaar via WoW64 op een 64-bits pc |
| `WorkflowTracker-win-arm64.zip` | Windows op ARM64-hardware; geen Intel/AMD-build |

Pak het passende ZIP-bestand uit naar een vaste map en start de exe. Het is een desktopapp voor een aangemelde gebruiker, geen serverdienst; in een Windows-service of sessie zonder interactief bureaublad kan het gebruikersscherm niet op deze manier worden gevolgd.

Technische uitvoerbaarheid en formele Microsoft-ondersteuning zijn verschillend. Controleer de [Windows-installatiematrix](https://learn.microsoft.com/en-us/dotnet/core/install/windows) en de [ondersteunde .NET 8-OS-versies](https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md) voor je specifieke Windows 10-editie en lifecycle. Reguliere Windows 10 22H2 staat daar inmiddels buiten reguliere ondersteuning; ondersteunde LTSC/Enterprise/IoT-edities verschillen. Deze projectuitvoer blijft .NET 8 zoals oorspronkelijk gevraagd; [.NET 8-servicing eindigt op 10 november 2026](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/). Een runtime-upgrade naar .NET 10 is een afzonderlijke vervolgstap. Er is hier geen Windows 10- of ARM64-testmachine beschikbaar; de huidige testmachine draait Windows 11 x64.

## Bouwen en publiceren

Op Windows met de .NET 8 SDK of nieuwer:

```powershell
dotnet build .\WorkflowTracker.csproj -c Release
dotnet run --project .\Tests\WorkflowTracker.Tests.csproj -c Release
dotnet publish .\WorkflowTracker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o .\publish
```

Publiceer de volledige huidige release met de drie architectuur-ZIPs via:

```powershell
.\scripts\publish.ps1
```

Of bouw alleen x86 met `.\scripts\publish.ps1 -RuntimeIdentifiers win-x86`. Het script bouwt sequentieel in `obj\release-packages` en controleert vooraf of de vaste x64-exe nog draait. Sluit die app voor publicatie. Het resultaat bevat de architectuur-ZIPs, een schoon bronpakket inclusief skill en `SHA256SUMS.txt`; er worden geen oude appbestanden verwijderd. Met `-SkipCanonicalCopy` kun je alle pakketten bouwen zonder de draaiende lokale exe te vervangen. Zie [PUBLISHING.md](PUBLISHING.md) voor de volledige publicatieprocedure en [CHANGELOG.md](CHANGELOG.md) voor wijzigingen.

Het enige benodigde distributiebestand per processorarchitectuur is de bijbehorende `WorkflowTracker.exe`, inclusief .NET-runtime. Native runtimebestanden mogen bij starten worden uitgepakt. Trimming is uitgeschakeld voor WPF.

De tests gebruiken fake HTTP en fake screenshots en controleren tekst-only uploads, de optionele PNG, context van twee teksten, lopende activiteiten, defaults, annulering en het verwijdercontract. De cleanup-integratietest verwijdert uitsluitend een gekopieerd testproces en dummy-bestanden binnen een geverifieerde tijdelijke map. De tests wijzigen geen echte registry-instellingen en versturen niets naar externe diensten. Een live test met Google en de webhook vereist je API-key.

De testharness controleert ook remote configuratie: selectieve merges, geblokkeerde webhookvelden, types en grenzen, idempotentie, opslagfouten, begrensde responses en live wijzigingen in echte mock-HTTP-verzoeken. Een handmatige testklok verifieert dat het nieuwe interval werkelijk wordt gebruikt zonder te wachten of de worker te herstarten. De opnamebeleid-tests controleren exacte daggrenzen, Nederlandse zomer-/wintertijd, annulering van lopende requests, hervatten na slaapstand, handmatige pauze, invoer tijdens analyse en het overslaan vóór de screenshot. Voor release 1.3.2 slagen alle 170 controles op zowel x64 als de self-contained x86-build op deze Windows 11 x64-laptop. De drie ZIPs zijn gecontroleerd op precies één exe, correcte PE-processorarchitectuur en een gelijke x64-exe aan de vaste uitvoer. ARM64 is gebouwd en structureel gecontroleerd, niet native uitgevoerd; Windows 10 is niet live getest.

Voor een complete build- en testcontrole gebruik je `.\scripts\verify.ps1`. Voor alleen een bronpakket gebruik je `.\scripts\package-source.ps1`. Het bronpakket gebruikt een expliciete bestandenlijst en bevat geen lokale instellingen, credentials, logs of buildmappen.

## Codex-skill

De skill `$screener-workflow-tracker` staat ook in deze repository: [SKILL.md](.agents/skills/screener-workflow-tracker/SKILL.md), met bijbehorende UI-metadata in [agents/openai.yaml](.agents/skills/screener-workflow-tracker/agents/openai.yaml). Voeg de map `.agents/skills/screener-workflow-tracker` samen met de projectbestanden toe aan Git; zo gaat de skill mee naar GitHub. De skill gebruikt de projectlocatie van de checkout en bevat geen vast lokaal gebruikerspad.
