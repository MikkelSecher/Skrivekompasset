using System.Text.Json.Serialization;

namespace Ordbogen.Skrivecoach.Models;

public sealed record HjaelpTrin(
    [property: JsonPropertyName("titel")] string Titel,
    [property: JsonPropertyName("beskrivelse")] string Beskrivelse);

public sealed record OpgaveHjaelp(
    [property: JsonPropertyName("forstaa_opgaven")] string ForstaaOpgaven,
    [property: JsonPropertyName("trin")] List<HjaelpTrin> Trin,
    [property: JsonPropertyName("spoergsmaal_til_dig_selv")] List<string> SpoergsmaalTilDigSelv,
    [property: JsonPropertyName("pas_paa")] List<string> PasPaa,
    [property: JsonPropertyName("afslutning")] string Afslutning);

public sealed record OpgaveHjaelpResultat(
    OpgaveHjaelp? Hjaelp,
    Klassetrin Klassetrin,
    ResponsesUsage? Usage,
    long LatencyMs);
