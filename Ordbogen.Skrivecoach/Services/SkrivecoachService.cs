using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Ordbogen.Skrivecoach.Models;

namespace Ordbogen.Skrivecoach.Services;

public sealed class SkrivecoachService
{
    private readonly OrdbogenClient _client;
    private readonly OrdbogenOptions _options;
    private readonly ILogger<SkrivecoachService> _log;

    private const string Instructions = """
Du er en dansk skrivecoach. Find stave-, grammatik- og stilfejl i den brugerleverede tekst.

Returnér KUN ét JSON-objekt med præcis denne form (ingen forklarende tekst udenfor):

{
  "corrections": [
    {
      "start": <int>,
      "end": <int>,
      "original": "<string>",
      "suggested": "<string>",
      "type": "stavning" | "grammatik" | "stil",
      "explanation": "<kort dansk forklaring>"
    }
  ]
}

Regler:
- 'start' og 'end' er nul-baserede UTF-16 char-offsets i den oprindelige tekst, hvor end er eksklusiv (ligesom JavaScript substring). Tæl tegn — ikke bytes.
- 'original' skal være den nøjagtige tekst mellem start og end.
- 'suggested' er den foreslåede rettelse (kan være tom string ved sletninger).
- 'type' er præcis én af: "stavning", "grammatik", "stil".
- 'explanation' er 1-2 sætninger på dansk om hvorfor det er en fejl.
- Marker kun reelle fejl. Ignorer stilistiske valg der er korrekte men anderledes.
- Hvis teksten er fejlfri, returnér {"corrections": []}.
""";

    public SkrivecoachService(OrdbogenClient client, IOptions<OrdbogenOptions> options, ILogger<SkrivecoachService> log)
    {
        _client = client;
        _options = options.Value;
        _log = log;
    }

    public int MaxInputCharacters => _options.MaxInputCharacters;

    private void EnsureInputWithinLimit(string input)
    {
        if (input.Length > _options.MaxInputCharacters)
        {
            throw new OrdbogenApiException(
                $"Teksten er for lang ({input.Length:N0} tegn). Maksimum er {_options.MaxInputCharacters:N0} tegn.");
        }
    }

    public async Task<SkrivecoachResultat> TjekTekstAsync(string tekst, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tekst))
        {
            return new SkrivecoachResultat(Array.Empty<Correction>(), null, 0);
        }
        EnsureInputWithinLimit(tekst);

        var request = new ResponsesRequest
        {
            Model = _options.Model,
            Input = tekst,
            Instructions = Instructions,
            Store = false,
            // ordbogen.ai's json_schema mode returnerer en truncated chunked response
            // (kun ~30 bytes) — bekræftet via direkte API-test på 2026-05-07.
            // json_object virker fint og prompt-stilen er stram nok til at få gyldigt JSON.
            Text = new ResponsesTextConfig
            {
                Format = new ResponsesTextFormat
                {
                    Type = "json_object"
                }
            }
        };

        var sw = Stopwatch.StartNew();
        var response = await _client.CreateResponseAsync(request, ct);
        sw.Stop();

        var raw = ExtractText(response);
        if (string.IsNullOrWhiteSpace(raw))
        {
            _log.LogWarning("Tomt output fra ordbogen.ai (id={Id}, status={Status})", response.Id, response.Status);
            return new SkrivecoachResultat(Array.Empty<Correction>(), response.Usage, sw.ElapsedMilliseconds);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<CorrectionsPayload>(raw)
                ?? new CorrectionsPayload(new List<Correction>());
            var realigned = RealignAndFilter(tekst, payload.Corrections);
            return new SkrivecoachResultat(realigned, response.Usage, sw.ElapsedMilliseconds);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Kunne ikke parse model-output som corrections-JSON. Raw: {Raw}", raw);
            throw new OrdbogenApiException(
                "Modellen returnerede et svar der ikke matchede det forventede JSON-format. Prøv igen.",
                inner: ex);
        }
    }

    private const string ImprovementsInstructions = """
Du er en dansk skrivecoach. Du får en nummereret liste af afsnit fra en tekst.
For hvert afsnit der reelt kan forbedres (skarpere formuleringer, klarere struktur,
bedre flow eller præcision), returnér en fuld omformulering. Spring afsnit over
der allerede er gode.

Returnér KUN ét JSON-objekt med præcis denne form (ingen forklarende tekst udenfor):

{
  "improvements": [
    {
      "paragraph_index": <int, 0-baseret, matcher [N]-præfikset i input>,
      "suggested": "<den omformulerede tekst, uden [N]-præfiks>",
      "explanation": "<kort dansk forklaring (1-2 sætninger) på hvad du har ændret og hvorfor>"
    }
  ]
}

Regler:
- Bevar afsnittets oprindelige betydning og tone — du må gerne flytte rundt på sætninger og forenkle, men ikke ændre påstande eller faktuelt indhold.
- Returnér KUN forslag til afsnit der reelt kan forbedres. Spring perfekte afsnit over.
- 'suggested' skal være den nye tekst alene — ingen citater, ingen [N]-præfiks, ingen markdown.
- Hvis intet skal ændres, returnér {"improvements": []}.
""";

    public async Task<ForbedringsResultat> ForbedrTekstAsync(string tekst, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tekst))
        {
            return new ForbedringsResultat(Array.Empty<Forbedring>(), Array.Empty<string>(), null, 0);
        }
        EnsureInputWithinLimit(tekst);

        var afsnit = SplitParagraphs(tekst);
        if (afsnit.Length == 0)
        {
            return new ForbedringsResultat(Array.Empty<Forbedring>(), Array.Empty<string>(), null, 0);
        }

        var nummereret = string.Join("\n\n", afsnit.Select((a, i) => $"[{i}]\n{a}"));

        var request = new ResponsesRequest
        {
            Model = _options.Model,
            Input = nummereret,
            Instructions = ImprovementsInstructions,
            Store = false,
            Text = new ResponsesTextConfig
            {
                Format = new ResponsesTextFormat { Type = "json_object" }
            }
        };

        var sw = Stopwatch.StartNew();
        var response = await _client.CreateResponseAsync(request, ct);
        sw.Stop();

        var raw = ExtractText(response);
        if (string.IsNullOrWhiteSpace(raw))
        {
            _log.LogWarning("Tomt output fra ordbogen.ai (forbedringer)");
            return new ForbedringsResultat(Array.Empty<Forbedring>(), afsnit, response.Usage, sw.ElapsedMilliseconds);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<ImprovementsPayload>(raw)
                ?? new ImprovementsPayload(new List<Forbedring>());

            var valid = payload.Improvements
                .Where(f => f.ParagraphIndex >= 0 && f.ParagraphIndex < afsnit.Length)
                .Where(f => !string.IsNullOrWhiteSpace(f.Suggested))
                .Where(f => !string.Equals(f.Suggested.Trim(), afsnit[f.ParagraphIndex].Trim(), StringComparison.Ordinal))
                .GroupBy(f => f.ParagraphIndex)
                .Select(g => g.First() with { Original = afsnit[g.Key] })
                .OrderBy(f => f.ParagraphIndex)
                .ToList();

            return new ForbedringsResultat(valid, afsnit, response.Usage, sw.ElapsedMilliseconds);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Kunne ikke parse model-output som improvements-JSON. Raw: {Raw}", raw);
            throw new OrdbogenApiException(
                "Modellen returnerede et svar der ikke matchede det forventede JSON-format. Prøv igen.",
                inner: ex);
        }
    }

    private static readonly Regex ParagraphSplitter = new(@"\n\s*\n+", RegexOptions.Compiled);

    internal static string[] SplitParagraphs(string tekst)
    {
        if (string.IsNullOrWhiteSpace(tekst)) return Array.Empty<string>();
        var normalized = tekst.Replace("\r\n", "\n").Replace("\r", "\n");
        return ParagraphSplitter.Split(normalized)
            .Select(p => p.Trim('\n', ' ', '\t'))
            .Where(p => p.Length > 0)
            .ToArray();
    }

    public async Task<LaererFeedbackResultat> LaererFeedbackAsync(string tekst, Klassetrin trin, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tekst))
        {
            return new LaererFeedbackResultat(null, trin, null, 0);
        }
        EnsureInputWithinLimit(tekst);

        var instructions = BuildLaererInstructions(trin);

        var request = new ResponsesRequest
        {
            Model = _options.Model,
            Input = tekst,
            Instructions = instructions,
            Store = false,
            Text = new ResponsesTextConfig
            {
                Format = new ResponsesTextFormat { Type = "json_object" }
            }
        };

        var sw = Stopwatch.StartNew();
        var response = await _client.CreateResponseAsync(request, ct);
        sw.Stop();

        var raw = ExtractText(response);
        if (string.IsNullOrWhiteSpace(raw))
        {
            _log.LogWarning("Tomt output fra ordbogen.ai (lærer-feedback)");
            return new LaererFeedbackResultat(null, trin, response.Usage, sw.ElapsedMilliseconds);
        }

        try
        {
            var feedback = JsonSerializer.Deserialize<LaererFeedback>(raw);
            if (feedback is null)
            {
                throw new OrdbogenApiException("Modellen returnerede et tomt feedback-objekt.");
            }
            return new LaererFeedbackResultat(feedback, trin, response.Usage, sw.ElapsedMilliseconds);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Kunne ikke parse model-output som lærer-feedback. Raw: {Raw}", raw);
            throw new OrdbogenApiException(
                "Modellen returnerede et svar der ikke matchede det forventede JSON-format. Prøv igen.",
                inner: ex);
        }
    }

    private static string BuildLaererInstructions(Klassetrin trin)
    {
        var (rolle, fokus, tone) = trin switch
        {
            Klassetrin.Indskoling => (
                "en venlig dansklærer der taler til en elev i 0.-3. klasse (6-9 år)",
                "store og små bogstaver, mellemrum, punktum og spørgsmålstegn, fuldstændige sætninger, at beskrive med flere ord. Brug helt enkelt sprog — undgå svære ord som 'syntaks', 'register' eller 'kohæsion'.",
                "Vær meget rosende og opmuntrende. Tal til eleven, ikke om eleven. Brug 'du' og giv masser af konkret ros før du nævner noget der kan blive bedre."
            ),
            Klassetrin.Mellemtrin => (
                "en dansklærer der taler til en elev i 4.-6. klasse (10-12 år)",
                "variation i sætningslængde og ordvalg, struktur og opbygning af afsnit, tegnsætning (komma, kolon), beskrivende sprog, fortællingens rød tråd. Brug forståeligt sprog uden tunge fagudtryk.",
                "Vær venlig og konkret. Ros først, peg så på 1-3 ting der kan blive bedre. Forklar hvorfor noget virker — ikke bare hvad der er forkert."
            ),
            Klassetrin.Udskoling => (
                "en dansklærer der taler til en elev i 7.-9. klasse (13-16 år)",
                "argumentation, sammenhæng mellem afsnit (kohæsion), brug af bindeord, præcision i ordvalg, syntaktisk variation, tone og målgruppebevidsthed. Du må gerne bruge fagudtryk og forklare dem kort.",
                "Vær konstruktivt-kritisk. Ros det specifikt der virker, og udfordr eleven til at hæve niveauet på 1-3 områder. Citér gerne fra teksten."
            ),
            Klassetrin.Gymnasium => (
                "en gymnasielærer der taler til en gymnasieelev (1.g-3.g)",
                "register, præcision og kompleksitet, argumentationsstruktur, retoriske virkemidler, kohæsion og koherens, syntaktisk variation, faglig stil. Brug fagsprog uden at forklare det.",
                "Vær fagligt seriøs og direkte. Ros sigende træk i teksten, identificér 2-4 områder med konkrete forbedringer. Citér fra teksten og foreslå alternativer."
            ),
            Klassetrin.Voksen => (
                "en erfaren dansk skrivekonsulent der giver kollegial feedback til en voksen skribent",
                "klarhed, præcision, struktur, tone og målgruppebevidsthed, argumentationsstyrke. Tilpas fokus til hvad teksten ser ud til at være (e-mail, rapport, blogindlæg osv.).",
                "Vær direkte, konstruktiv og respektfuld. Skriv som peer-til-peer — ikke som overlærer. Ros specifikt, og udpeg de 2-4 vigtigste forbedringspunkter."
            ),
            _ => (
                "en dansklærer",
                "klarhed, struktur og sprog",
                "Vær konstruktiv og venlig."
            )
        };

        return $$"""
Du er {{rolle}}. Du får en tekst som eleven har skrevet og skal give pædagogisk tilbagemelding.

Fokus på dette niveau: {{fokus}}

Tone: {{tone}}

Returnér KUN ét JSON-objekt med præcis denne form (ingen forklarende tekst udenfor):

{
  "ros": "<2-4 sætninger om hvad der konkret virker godt i teksten — citér gerne kort>",
  "udfordringer": [
    {
      "omraade": "<kort overskrift, fx 'Sætningsvariation' eller 'Tegnsætning'>",
      "kommentar": "<2-3 sætninger der forklarer hvorfor det er en udfordring>",
      "eksempel_fra_tekst": "<et kort citat fra teksten der viser udfordringen, eller null hvis ikke relevant>",
      "forslag": "<konkret forslag til hvordan eleven kan øve eller forbedre dette, eller null>"
    }
  ],
  "naeste_skridt": [
    "<konkret handlingsorienteret skridt eleven kan tage>",
    "<…1-3 i alt>"
  ],
  "samlet": "<1-2 opmuntrende sætninger der lukker tilbagemeldingen>"
}

Regler:
- Returnér 1-4 udfordringer. Færre er bedre hvis teksten er stærk.
- Brug dansk gennem hele svaret.
- Tilpas både sprog og indhold til niveauet beskrevet ovenfor.
- Hvis teksten er meget kort eller ufuldstændig, må du gerne nævne det venligt i 'ros' eller 'samlet'.
""";
    }

    public async Task<OpgaveHjaelpResultat> HjaelpIGangAsync(string opgave, Klassetrin trin, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opgave))
        {
            return new OpgaveHjaelpResultat(null, trin, null, 0);
        }
        EnsureInputWithinLimit(opgave);

        var instructions = BuildHjaelpInstructions(trin);

        var request = new ResponsesRequest
        {
            Model = _options.Model,
            Input = opgave,
            Instructions = instructions,
            Store = false,
            Text = new ResponsesTextConfig
            {
                Format = new ResponsesTextFormat { Type = "json_object" }
            }
        };

        var sw = Stopwatch.StartNew();
        var response = await _client.CreateResponseAsync(request, ct);
        sw.Stop();

        var raw = ExtractText(response);
        if (string.IsNullOrWhiteSpace(raw))
        {
            _log.LogWarning("Tomt output fra ordbogen.ai (opgave-hjælp)");
            return new OpgaveHjaelpResultat(null, trin, response.Usage, sw.ElapsedMilliseconds);
        }

        try
        {
            var hjaelp = JsonSerializer.Deserialize<OpgaveHjaelp>(raw);
            if (hjaelp is null)
            {
                throw new OrdbogenApiException("Modellen returnerede et tomt hjælpe-objekt.");
            }
            return new OpgaveHjaelpResultat(hjaelp, trin, response.Usage, sw.ElapsedMilliseconds);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Kunne ikke parse model-output som opgave-hjælp. Raw: {Raw}", raw);
            throw new OrdbogenApiException(
                "Modellen returnerede et svar der ikke matchede det forventede JSON-format. Prøv igen.",
                inner: ex);
        }
    }

    private static string BuildHjaelpInstructions(Klassetrin trin)
    {
        var (rolle, tone, fokus) = trin switch
        {
            Klassetrin.Indskoling => (
                "en venlig voksen der hjælper et barn på 6-9 år med at komme i gang med en opgave",
                "Brug et meget enkelt sprog og en varm, opmuntrende tone. Forklar som om du fortæller en historie. Brug korte sætninger.",
                "Hjælp eleven med at læse opgaven igen og forstå hvad der spørges om. Foreslå helt små, konkrete første skridt (fx 'find dine farveblyanter', 'læs opgaven én gang mere'). Undgå svære ord."
            ),
            Klassetrin.Mellemtrin => (
                "en hjælpsom dansklærer der vejleder en elev i 4.-6. klasse (10-12 år)",
                "Brug venligt og forståeligt sprog. Tal til eleven, ikke om eleven.",
                "Hjælp eleven med at forstå opgaven, lægge en simpel plan, og bryde den ned i overskuelige skridt. Introducér gerne tanken om at lave en lille brainstorm eller disposition."
            ),
            Klassetrin.Udskoling => (
                "en mentor der vejleder en elev i 7.-9. klasse (13-16 år)",
                "Vær konstruktivt-rådgivende. Stil eleven spørgsmål der får dem til at tænke selv.",
                "Hjælp eleven med at læse opgaven kritisk: hvad spørger den om, hvilke nøgleord er der, hvad er kriterierne. Strukturér tilgangen i metodiske skridt. Brug fagudtryk og forklar dem kort."
            ),
            Klassetrin.Gymnasium => (
                "en studievejleder der hjælper en gymnasieelev (1.g-3.g) med at komme i gang med en opgave",
                "Vær fagligt klar og direkte uden at være kølig.",
                "Identificér opgavetypen (analyse, redegørelse, diskussion, essay osv.) og hvad det indebærer metodisk. Foreslå en struktur. Brug fagsprog uden at forklare det."
            ),
            Klassetrin.Voksen => (
                "en erfaren mentor der hjælper en voksen med at strukturere en opgave eller arbejdsopgave",
                "Vær professionel og kollegial — peer-til-peer.",
                "Hjælp med at definere scope, deliverables og rækkefølge. Foreslå en pragmatisk tilgang og hvilken type tekst/analyse opgaven peger mod."
            ),
            _ => (
                "en hjælpsom vejleder",
                "Vær konstruktiv og venlig.",
                "Hjælp eleven med at forstå opgaven og komme i gang."
            )
        };

        return $$"""
Du er {{rolle}}. En elev har sendt dig en opgavebeskrivelse og beder om hjælp til at komme i gang.

⚠️ ABSOLUTTE REGLER — DU MÅ IKKE BRYDE DEM:
- Du må IKKE løse opgaven for eleven.
- Giv ALDRIG konkrete svar, beregninger, færdige formuleringer, færdige analyser, færdige tekster eller færdige disposition-indhold.
- Hvis opgaven beder om en analyse — udfør IKKE analysen. Forklar hvordan eleven selv kan analysere.
- Hvis opgaven er en regneopgave — udregn IKKE noget. Hjælp med at identificere metoden.
- Hvis opgaven beder om en tekst — skriv IKKE teksten eller udkast til den. Hjælp med at planlægge og strukturere.
- Hvis du er i tvivl om noget krydser grænsen — så lad være.

Dit job er at hjælpe eleven med at FORSTÅ opgaven og PLANLÆGGE deres egen tilgang. Du skal vejlede eleven til at tænke selv.

Tone: {{tone}}

Fokus for dette niveau: {{fokus}}

Returnér KUN ét JSON-objekt med præcis denne form (ingen forklarende tekst udenfor):

{
  "forstaa_opgaven": "<2-4 sætninger der genfortæller opgaven med ord eleven forstår, fx 'Det jeg tror du skal gøre, er...'>",
  "trin": [
    {
      "titel": "<kort overskrift som fx 'Læs opgaven igen' eller 'Lav en kort plan'>",
      "beskrivelse": "<1-3 sætninger der forklarer hvad eleven konkret kan GØRE for at komme videre — IKKE hvad svaret er>"
    }
  ],
  "spoergsmaal_til_dig_selv": [
    "<refleksionsspørgsmål eleven kan stille sig selv for at komme videre>",
    "<...2-4 i alt>"
  ],
  "pas_paa": [
    "<en faldgrube eller noget eleven typisk overser>",
    "<...0-3 i alt>"
  ],
  "afslutning": "<1-2 opmuntrende sætninger>"
}

Regler for indhold:
- Returnér 3-6 trin afhængig af opgavens kompleksitet og elevens niveau.
- Brug dansk gennem hele svaret.
- Tilpas sprog og kompleksitet til niveauet beskrevet ovenfor.
- Hvis opgaven er uklar eller mangler kontekst — foreslå venligt at eleven beder læreren om mere information.
- Hvis input ikke ligner en opgave (fx hvis eleven har skrevet sit svar i stedet for opgaven), så sig det venligt i 'forstaa_opgaven' og bed om at få selve opgavebeskrivelsen.
""";
    }

    // Modellen er god til at identificere hvad der er forkert (Original-feltet),
    // men ofte off-by-N på Start/End. Vi bruger Original som sandhed og søger
    // dens faktiske position i teksten — modellens offsets bruges kun som hint
    // til at vælge den nærmeste forekomst når Original optræder flere gange.
    internal IReadOnlyList<Correction> RealignAndFilter(string tekst, IEnumerable<Correction> raw)
    {
        var aligned = new List<Correction>();
        var unchanged = 0;
        var realigned = 0;
        var dropped = 0;

        foreach (var c in raw)
        {
            if (string.IsNullOrEmpty(c.Original))
            {
                dropped++;
                continue;
            }

            // 1. Eksakt match på modellens offsets — behold uændret
            if (c.Start >= 0 && c.End <= tekst.Length && c.Start < c.End
                && tekst.AsSpan(c.Start, c.End - c.Start).SequenceEqual(c.Original))
            {
                aligned.Add(c);
                unchanged++;
                continue;
            }

            // 2. Slå op i teksten og find forekomsten tættest på modellens claim
            var occurrence = FindClosestOccurrence(tekst, c.Original, c.Start);
            if (occurrence < 0)
            {
                _log.LogWarning("Kunne ikke lokalisere correction.original=\"{Original}\" i tekst — droppet.",
                    c.Original);
                dropped++;
                continue;
            }

            aligned.Add(c with { Start = occurrence, End = occurrence + c.Original.Length });
            realigned++;
        }

        // Sortér og fjern overlap (foretræk tidligere correction)
        aligned.Sort((a, b) => a.Start.CompareTo(b.Start));
        var noOverlap = new List<Correction>(aligned.Count);
        var cursor = 0;
        foreach (var c in aligned)
        {
            if (c.Start < cursor)
            {
                dropped++;
                continue;
            }
            noOverlap.Add(c);
            cursor = c.End;
        }

        if (realigned > 0 || dropped > 0)
        {
            _log.LogInformation(
                "Realignment: {Unchanged} uændret, {Realigned} flyttet, {Dropped} droppet (af {Total} fra modellen)",
                unchanged, realigned, dropped, unchanged + realigned + dropped);
        }

        return noOverlap;
    }

    private static int FindClosestOccurrence(string tekst, string needle, int hint)
    {
        var first = tekst.IndexOf(needle, StringComparison.Ordinal);
        if (first < 0) return -1;

        var bestPos = first;
        var bestDistance = Math.Abs(first - hint);
        var pos = first;
        while ((pos = tekst.IndexOf(needle, pos + 1, StringComparison.Ordinal)) >= 0)
        {
            var distance = Math.Abs(pos - hint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestPos = pos;
            }
        }
        return bestPos;
    }

    private static string? ExtractText(ResponsesResponse response)
    {
        if (!string.IsNullOrEmpty(response.OutputText))
        {
            return response.OutputText;
        }

        if (response.Output is null)
        {
            return null;
        }

        foreach (var item in response.Output)
        {
            if (item.Content is null) continue;
            foreach (var c in item.Content)
            {
                if (!string.IsNullOrEmpty(c.Text))
                {
                    return c.Text;
                }
            }
        }

        return null;
    }
}
