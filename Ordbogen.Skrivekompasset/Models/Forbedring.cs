using System.Text.Json.Serialization;

namespace Ordbogen.Skrivekompasset.Models;

public sealed record Forbedring(
    [property: JsonPropertyName("paragraph_index")] int ParagraphIndex,
    [property: JsonPropertyName("suggested")] string Suggested,
    [property: JsonPropertyName("explanation")] string Explanation)
{
    // Udfyldes klient-side ud fra ParagraphIndex efter parsing — modellen sender den ikke retur.
    [JsonIgnore]
    public string Original { get; init; } = string.Empty;
}

public sealed record ImprovementsPayload(
    [property: JsonPropertyName("improvements")] List<Forbedring> Improvements);

public sealed record ForbedringsResultat(
    IReadOnlyList<Forbedring> Forbedringer,
    string[] Afsnit,
    ResponsesUsage? Usage,
    long LatencyMs);
