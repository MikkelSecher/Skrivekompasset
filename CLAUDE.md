# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Blazor Server-app der demonstrerer ordbogen.ai's Responses API. Fire modes — stavning/grammatik, forbedringsforslag på afsnitsniveau, lærer-feedback, og opgave-hjælp — alle drevet af samme `POST /v1/responses`-endpoint via forskellige prompts og JSON-strukturer. Se `README.md` for end-user-orienteret feature-beskrivelse og API-eksempler.

## Commands

Run from `Ordbogen.Skrivekompasset/` (the project subfolder, not the repo root):

```powershell
dotnet build
dotnet run --launch-profile https     # https://localhost:7065
dotnet user-secrets set "Ordbogen:ApiKey" "<key>"   # required first time
dotnet user-secrets list                            # verify
```

App'en kører på .NET 10 (8/9 fungerer hvis `<TargetFramework>` opdateres). Ingen tests pt.

**Vigtigt ved gen-build under udvikling:** Hvis dev-serveren kører, holder den `Ordbogen.Skrivekompasset.exe` låst og rebuild fejler med MSB3027. Stop processen før du builder igen.

## Architecture

### Three-layer split (alle requests går samme vej)

```
Home.razor (UI state)
  → SkrivekompassetService (prompt-bygning + JSON-parsing)
    → OrdbogenClient (typed HttpClient, kun rå HTTP)
      → POST /v1/responses
```

`OrdbogenClient` ved kun om HTTP og deserialisering. `SkrivekompassetService` ejer alle prompts og er det eneste sted modellens output omsættes til domænetyper. UI'et indeholder al brugerinteraktion og mode-state. Tilføj nye AI-features ved at lægge en ny metode på `SkrivekompassetService` — undgå at skrive direkte mod `OrdbogenClient` fra UI'et.

### Prompt-mønster: alle fire modes bruger `json_object` + schema-i-prompt

**Vigtigt forbehold:** ordbogen.ai's `text.format.type = "json_schema"` returnerer en truncated chunked-respons (~30 bytes, `{"ordbogen-identifier": "xxx[]`). Verificeret 2026-05-07. Vi bruger `text.format.type = "json_object"` og specificerer schemaet i `instructions`-prompten. Hvis du tilføjer en ny mode, gør det samme — test ikke `json_schema` igen før du har bekræftet det er fixet på serveren.

ordbogen.ai's response-shape afviger også fra OpenAI's: `output[]` indeholder typisk både en `"reasoning"`-item (uden tekst) og en `"message"`-item (med `content[0].text`). `OrdbogenClient.ExtractText` itererer og returnerer det første ikke-tomme tekstfelt — genbrug den når du parser nye endpoint-svar.

### Klassetrin-tilpassede prompts

`Klassetrin`-enum'en (Indskoling/Mellemtrin/Udskoling/Gymnasium/Voksen) styrer både Lærer-feedback og Opgave-hjælp. Begge bygger prompt dynamisk via `BuildLaererInstructions` / `BuildHjaelpInstructions` der vælger rolle/tone/fokus pr. trin. Ved tilføjelse af nye trin-tilpassede features: følg samme switch-pattern, og hold tone/fokus-strenge centralt sammenlignelige på tværs af features.

### Stavnings-mode: realignment af LLM offsets

LLM'er er notorisk dårlige til char-counting. Modellen returnerer `start`/`end` der ofte er off-by-N. `SkrivekompassetService.RealignAndFilter` finder `original`-strengen i teksten med `IndexOf` og rewriter offsets — modellens claim'ede `start` bruges kun som hint til at vælge nærmeste forekomst. `Home.razor.LokaliserOriginal` gør samme tjek belt-and-braces før substitution. Lige tilføjelse af nye features der bruger char-offsets: brug samme mønster (string-search med claim'et offset som hint) i stedet for at stole på modellens tal.

Forbedringer-mode bruger `paragraph_index` i stedet for offsets — meget mere robust. Foretræk index-baserede patterns over offset-baserede for nye features hvor det er muligt.

### Per-mode state i Home.razor — Hjælp er undtagelsen

Stavning, Forbedringer og Lærer **deler** `_tekst`-input og rydder hinandens resultater på mode-skift (eksisterende adfærd, "fresh start" pr. mode). Hjælp har sin **egen** `_hjaelpTekst` og `_opgaveHjaelp`, der bevares på tværs af mode-skift, fordi en opgavebeskrivelse er semantisk anderledes end en skribents tekst.

Latency/usage er per-mode (`_stavningLatencyMs`, `_hjaelpLatencyMs` osv.) og eksponeres via beregnede properties `_latencyMs` / `_usage` der vælger ud fra `_mode`. Bind-pattern: brug `CurrentInput`-property i textarea i stedet for at hardcode `_tekst`, så den binder til det rigtige felt pr. mode.

Når du tilføjer en ny mode: bestem først om den tematisk hører til S/F/L-gruppen (deler `_tekst`) eller skal være selvstændig som Hjælp.

### Hjælp-mode har en hård "ikke løs"-regel

`BuildHjaelpInstructions` indeholder eksplicitte regler om at modellen ikke må give konkrete svar, beregninger, færdige formuleringer eller analyser. Hvis du modificerer prompten, bevar disse regler — de er kernen i hvad der skiller Hjælp fra "skriv det for mig". Reglerne er omhyggeligt formuleret med flere eksempler (regneopgave, skriveopgave, analyse) og bør ikke trimmes ned.

## Konfiguration

`OrdbogenOptions` (BaseUrl, Model, ApiKey) bindes fra config-section `Ordbogen` i `Program.cs`. ApiKey må aldrig stå i `appsettings.json` — det er user-secrets eller env-variabel (`Ordbogen__ApiKey`) ved deploy. `OrdbogenClient` registreres som typed `HttpClient` med Bearer-headeren sat én gang i client-factory'en, så servicekoden ikke håndterer auth.

## Dansk vs engelsk

Al UI-tekst, prompts og kommentarer er på dansk for at matche målgruppen. Bevar dansk i nye prompts/UI. Variabel- og typenavne følger dansk domænesprog (`Skrivekompasset`, `Forbedring`, `Klassetrin`, `Afsnit`) — hold den linje for konsistens.
