# Ordbogen Skrivecoach

En lille Blazor Server-webapp der demonstrerer hvad [ordbogen.ai](https://www.ordbogen.ai)-API'et kan bruges til. App'en hjælper skribenter på dansk med tre forskellige typer feedback — fra inline stave-/grammatikfejl til pædagogisk lærer-tilbagemelding tilpasset klassetrin.

App'et bygger på ordbogen.ai's `POST /v1/responses`-endpoint (en OpenAI-style Responses API) og bruger samme model — `odin-large` — i alle tre modes. Forskellen ligger i hvordan modellen prompts, og hvordan svaret parses og vises.

## Indhold

- [Sådan kommer du i gang](#sådan-kommer-du-i-gang)
- [De fire funktioner](#de-fire-funktioner)
  - [1. Stavning & grammatik](#1-stavning--grammatik)
  - [2. Forbedringsforslag](#2-forbedringsforslag)
  - [3. Lærer-feedback](#3-lærer-feedback)
  - [4. Hjælp i gang](#4-hjælp-i-gang)
- [Arkitektur](#arkitektur)
- [Filstruktur](#filstruktur)
- [Kendte forbehold](#kendte-forbehold)

## Sådan kommer du i gang

Krav:
- .NET 10 SDK (eller .NET 9 — bare opdater `<TargetFramework>` i `.csproj`)
- En API-nøgle fra ordbogen.ai

### Opsætning

```powershell
# Stå i Ordbogen.Skrivecoach-mappen
cd Ordbogen.Skrivecoach

# Læg API-nøglen i user-secrets (ligger uden for projektet, ikke i Git)
dotnet user-secrets set "Ordbogen:ApiKey" "<din-nøgle>"

# Kør appen
dotnet run --launch-profile https
```

Åbn https://localhost:7065 i en browser.

### Konfiguration

| Nøgle                | Hvor                              | Default                          |
|----------------------|-----------------------------------|----------------------------------|
| `Ordbogen:BaseUrl`   | `appsettings.json`                | `https://api.ordbogen.ai/v1`     |
| `Ordbogen:Model`     | `appsettings.json`                | `odin-large`                     |
| `Ordbogen:ApiKey`    | `dotnet user-secrets` (eller env) | *(skal sættes)*                  |

I produktion bruges miljøvariablen `Ordbogen__ApiKey` (dobbelt underscore = `:` i config-keys).

## De fire funktioner

App'en har en mode-toggle øverst hvor du vælger mellem de tre funktioner. Tilstanden nulstilles når du skifter mode, så du kan eksperimentere med samme tekst på tværs af modes.

### 1. Stavning & grammatik

**Hvad det gør:** Finder konkrete stave-, grammatik- og stilfejl i teksten. Hver fejl bliver markeret inline i teksten med farvekoder (rød = stavning, blå = grammatik, grøn = stil) og listet i sidepanelet med et forslag til rettelse og en kort forklaring.

**Sådan bruges det:**
1. Skriv eller paste dansk tekst i feltet.
2. Klik **Tjek tekst**.
3. Klik **Anvend** på en rettelse for at indsætte forslaget i teksten — eller **Ignorer** for at lade fejlen blive.
4. **Anvend alle** ændrer alle fundne fejl på én gang.

**Hvilket API-kald:** `POST /v1/responses`

```jsonc
{
  "model": "odin-large",
  "input": "<brugerens tekst>",
  "instructions": "Du er en dansk skrivecoach. Find stave-, grammatik- og stilfejl ...",
  "store": false,
  "text": { "format": { "type": "json_object" } }
}
```

**Forventet svar (fra modellen):**

```json
{
  "corrections": [
    {
      "start": 4,
      "end": 10,
      "original": "spilst",
      "suggested": "spiste",
      "type": "stavning",
      "explanation": "Forkert datid — det skal være 'spiste'."
    }
  ]
}
```

Servicekoden ([`SkrivecoachService.TjekTekstAsync`](Ordbogen.Skrivecoach/Services/SkrivecoachService.cs)) re-aligner offsets efter kaldet: modellen er god til at identificere `original`, men ofte upræcis på `start`/`end`. Vi finder den faktiske position af `original` i teksten via string-search og bruger modellens offset som hint til at vælge rigtig forekomst når samme ord står flere steder.

### 2. Forbedringsforslag

**Hvad det gør:** Splitter teksten i afsnit og foreslår en omformulering pr. afsnit der kan blive skarpere — fokus på klarhed, flow, ordvalg og struktur uden at ændre indholdet. Afsnit der allerede er gode, springes over.

**Sådan bruges det:**
1. Indsæt en tekst med flere afsnit (adskilt af blanke linjer).
2. Klik **Foreslå forbedringer**.
3. Editor-pane viser teksten med 💡-markering på afsnit der har forslag.
4. Sidepanelet viser hver forbedring som "Original" → "Forslag" + forklaring.
5. **Anvend** udskifter ét afsnit; resten af forslagene forbliver gyldige siden de er indekseret pr. afsnit, ikke pr. char-offset.

**Hvilket API-kald:** `POST /v1/responses`

Teksten splittes klient-side på blanke linjer, og afsnittene sendes nummererede til modellen:

```
[0]
Vi har i lang tid haft et stort fokus på at forbedre kvaliteten ...

[1]
I løbet af det sidste år, har vi lavet en masse ændringer ...
```

```jsonc
{
  "model": "odin-large",
  "input": "<nummereret afsnitsliste>",
  "instructions": "Du er en dansk skrivecoach. Du får en nummereret liste af afsnit ...",
  "store": false,
  "text": { "format": { "type": "json_object" } }
}
```

**Forventet svar:**

```json
{
  "improvements": [
    {
      "paragraph_index": 0,
      "suggested": "Vi prioriterer høj kvalitet og har et vedvarende fokus på at forbedre vores produkter.",
      "explanation": "Strammet op — fjernet redundans og gjort budskabet mere direkte."
    }
  ]
}
```

Vi bruger `paragraph_index` til at mappe forslag tilbage til den oprindelige afsnitsarray ([`SkrivecoachService.ForbedrTekstAsync`](Ordbogen.Skrivecoach/Services/SkrivecoachService.cs)). Modellen behøver ikke gentage `original`-teksten — vi har den allerede klient-side.

### 3. Lærer-feedback

**Hvad det gør:** Giver pædagogisk tilbagemelding som en lærer ville — startende med ros, dernæst 1-4 udfordringer med konkrete eksempler og forslag, og afsluttende med næste-skridt-handlinger. Tone, fokusområder og fagudtryk tilpasses klassetrin.

**Klassetrin og hvad de gør anderledes:**

| Trin           | Tone                          | Typisk fokus                                                            |
|----------------|-------------------------------|------------------------------------------------------------------------|
| Indskoling     | Meget rosende, helt enkelt    | Store/små bogstaver, mellemrum, punktum, fuldstændige sætninger        |
| Mellemtrin     | Venlig og konkret             | Variation i sætninger og ordvalg, opbygning af afsnit, tegnsætning      |
| Udskoling      | Konstruktivt-kritisk          | Argumentation, kohæsion, bindeord, præcision, syntaktisk variation     |
| Gymnasium      | Fagligt seriøs, direkte       | Register, retoriske virkemidler, faglig stil, koherens                  |
| Voksen         | Peer-til-peer kollegialt      | Klarhed, struktur, tone, målgruppebevidsthed                            |

**Sådan bruges det:**
1. Vælg klassetrin i dropdown'en.
2. Indsæt elevens tekst.
3. Klik **Hent feedback**.
4. Sidepanelet viser tilbagemeldingen i fire sektioner: ros, udfordringer, næste skridt, og en afsluttende kommentar.

Skift klassetrin på samme tekst og kør igen — du vil se at både sprog og fokusområder ændres markant.

**Hvilket API-kald:** `POST /v1/responses`

```jsonc
{
  "model": "odin-large",
  "input": "<elevens tekst>",
  "instructions": "Du er en dansklærer der taler til en elev i 4.-6. klasse ... Tone: ...",
  "store": false,
  "text": { "format": { "type": "json_object" } }
}
```

Prompten bygges dynamisk i [`SkrivecoachService.BuildLaererInstructions`](Ordbogen.Skrivecoach/Services/SkrivecoachService.cs) — rolle, fokusområder og tone vælges ud fra `Klassetrin`-enum'en.

**Forventet svar:**

```json
{
  "ros": "Du har en god, klar fortælling med konkrete detaljer ...",
  "udfordringer": [
    {
      "omraade": "Sætningsvariation",
      "kommentar": "Du bruger mange korte sætninger efter hinanden — det gør teksten lidt hakkende.",
      "eksempel_fra_tekst": "Vi var ved stranden hver dag. Vi spiste is. Det var sjovt.",
      "forslag": "Prøv at binde nogle af sætningerne sammen med 'og' eller 'mens'."
    }
  ],
  "naeste_skridt": [
    "Skriv din næste tekst igennem og prøv at binde 2-3 korte sætninger sammen til én længere."
  ],
  "samlet": "Rigtig fin tekst — du er allerede god til at fortælle, og nu skal vi bare arbejde med variationen."
}
```

### 4. Hjælp i gang

**Hvad det gør:** Eleven indsætter en *opgavebeskrivelse* (ikke deres svar), og får en trin-tilpasset guide til at komme i gang. Modellen er instrueret meget strengt om **ikke** at løse opgaven — den må kun hjælpe eleven med at forstå opgaven og planlægge deres egen tilgang. Det er en bedre måde at bede om hjælp på end "skriv det for mig".

Bruger samme `Klassetrin`-enum som lærer-feedback, så samme dropdown styrer begge funktioner.

**Sådan bruges det:**
1. Vælg klassetrin.
2. Indsæt opgavebeskrivelsen — fx "Skriv en kort tekst på 250-400 ord om en oplevelse fra din sommerferie. Brug mindst tre sansebeskrivelser..."
3. Klik **Hjælp mig i gang**.
4. Sidepanelet viser fem sektioner: Sådan forstår jeg opgaven, Trin du kan tage, Spørgsmål til dig selv, Pas på, og en opmuntrende afslutning.

**Hvilket API-kald:** `POST /v1/responses`

```jsonc
{
  "model": "odin-large",
  "input": "<opgavebeskrivelsen>",
  "instructions": "Du er en mentor der vejleder en elev ... ABSOLUTTE REGLER: Du må IKKE løse opgaven for eleven ...",
  "store": false,
  "text": { "format": { "type": "json_object" } }
}
```

System-prompten ([`SkrivecoachService.BuildHjaelpInstructions`](Ordbogen.Skrivecoach/Services/SkrivecoachService.cs)) varierer rolle, tone og fokus efter klassetrin, men holder den hårde regel konstant: ingen konkrete svar, ingen beregninger, ingen færdige tekster eller analyser.

**Forventet svar:**

```json
{
  "forstaa_opgaven": "Det jeg tror du skal gøre, er at fortælle om en oplevelse fra din sommerferie ...",
  "trin": [
    {
      "titel": "Læs opgaven igen",
      "beskrivelse": "Streg ord under som 'sansebeskrivelser' og '250-400 ord' så du husker kravene."
    },
    {
      "titel": "Vælg én oplevelse",
      "beskrivelse": "Tænk på en sommerferie-oplevelse hvor du kan huske mange detaljer ..."
    }
  ],
  "spoergsmaal_til_dig_selv": [
    "Hvilken oplevelse husker jeg flest detaljer fra?",
    "Hvilke sanser brugte jeg under den oplevelse?"
  ],
  "pas_paa": [
    "Det er nemt at bruge for mange korte sætninger — øv dig i at variere længden."
  ],
  "afslutning": "Du har god tid — gå roligt i gang med trin 1, så hjælper det resten på vej."
}
```

## Arkitektur

```
Browser ──HTTPS──► Blazor Server (interactive)
                        │
                        │  SkrivecoachService.{TjekTekst,ForbedrTekst,LaererFeedback}Async
                        │
                        ▼
                   OrdbogenClient (typed HttpClient)
                        │
                        │  POST /v1/responses
                        │  Authorization: Bearer <api-key>
                        ▼
                   api.ordbogen.ai
```

**Vigtige principper:**

- **API-nøglen forlader aldrig serveren.** Blazor Server-rendering betyder at alle servicekald sker server-side, og browseren kommunikerer kun via SignalR. `OrdbogenClient` er registreret som typed `HttpClient` i `Program.cs` med `Authorization: Bearer`-headeren sat én gang.
- **Tre tynde lag:**
  - `OrdbogenClient` — kender kun rå HTTP og deserialisering. Kaster `OrdbogenApiException` på fejl.
  - `SkrivecoachService` — kender kun ordbogen.ai's prompt-mønstre. Bygger schema-prompts og parser model-output til domænetyper.
  - `Components/Pages/Home.razor` — UI-state og brugerinteraktion. Kalder service'en og rendrer resultatet pr. mode.
- **Strukturerede svar via prompt frem for json_schema.** Vi sender `"text": { "format": { "type": "json_object" } }` og specificerer schemaet i prompten i stedet for via `text.format.type = "json_schema"`. Sidstnævnte returnerer pt. en truncated chunked-respons fra ordbogen.ai's backend (se [Kendte forbehold](#kendte-forbehold)).

## Filstruktur

```
Ordbogen.Skrivecoach/
├── Program.cs                            DI-opsætning, typed HttpClient
├── appsettings.json                      BaseUrl, Model (ingen nøgle)
├── Models/
│   ├── OrdbogenOptions.cs                Konfigurations-binding
│   ├── ResponsesDtos.cs                  DTO'er for ordbogen.ai's request/response
│   ├── Correction.cs                     Stave-/grammatik-rettelse + payload
│   ├── Forbedring.cs                     Afsnits-forbedring + payload
│   ├── LaererFeedback.cs                 Lærer-feedback-rapport + Klassetrin enum
│   └── OpgaveHjaelp.cs                   "Hjælp i gang"-guide + payload
├── Services/
│   ├── OrdbogenClient.cs                 HTTP-kald til POST /responses
│   ├── OrdbogenApiException.cs           Domæne-specifik exception
│   └── SkrivecoachService.cs             Fire prompt-flows + parsing
├── Components/
│   ├── AnnotatedText.razor               Renderer tekst med <mark>-spans
│   ├── Layout/
│   │   ├── MainLayout.razor              Top-bar med link til ordbogen.ai docs
│   │   └── NavMenu.razor
│   └── Pages/
│       └── Home.razor                    UI med mode-toggle og state
└── wwwroot/app.css                       Styling
```

## Kendte forbehold

**`text.format.type = "json_schema"` er broken pt. (verificeret 2026-05-07).**
ordbogen.ai's backend returnerer en chunked HTTP 200-respons der bliver afbrudt efter ~30 bytes (`{"ordbogen-identifier": "xxx[]`), uanset om `strict: true` eller `false`. Vi bruger derfor `json_object` og specificerer schemaet i prompten. Skemaet skal i øvrigt have et fladere format end OpenAI's API — `name`/`schema` er søskende til `type`, ikke nested i et `json_schema`-objekt.

**LLM'er er dårlige til at tælle char-offsets.**
I stavnings-mode kan modellen returnere `start`/`end`-offsets der er off-by-N. `RealignAndFilter` i `SkrivecoachService` kompenserer ved at finde `original` i teksten og rewrite offsets til de faktiske positioner. Hvis `original` ikke kan findes, droppes rettelsen.

**Output indeholder både `reasoning` og `message` items.**
ordbogen.ai's `output[]`-array har typisk to entries: en `"reasoning"` (uden tekst) og en `"message"` (med teksten i `content[0].text`). `OrdbogenClient.ExtractText` itererer og returnerer det første ikke-tomme tekst-felt.

**Ingen streaming.**
Alt går via klassisk request/response. Streaming kunne tilføjes via SSE hvis ordbogen.ai understøtter det (Responses API gør det typisk).

**Persistens slået fra.**
Alle requests sender `"store": false` for at undgå at samle tekst-historik på ordbogen-siden. Hvis du vil bygge fortløbende samtaler oven på samme `conversation`, skal det slås til.
