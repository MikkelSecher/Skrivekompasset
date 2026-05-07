using System.Text.Json.Serialization;

namespace Ordbogen.Skrivecoach.Models;

public enum CorrectionType
{
    Stavning,
    Grammatik,
    Stil
}

public sealed record Correction(
    [property: JsonPropertyName("start")] int Start,
    [property: JsonPropertyName("end")] int End,
    [property: JsonPropertyName("original")] string Original,
    [property: JsonPropertyName("suggested")] string Suggested,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("explanation")] string Explanation)
{
    public CorrectionType ParsedType => Type?.ToLowerInvariant() switch
    {
        "stavning" => CorrectionType.Stavning,
        "grammatik" => CorrectionType.Grammatik,
        "stil" => CorrectionType.Stil,
        _ => CorrectionType.Stil
    };
}

public sealed record CorrectionsPayload(
    [property: JsonPropertyName("corrections")] List<Correction> Corrections);

public sealed record SkrivecoachResultat(
    IReadOnlyList<Correction> Corrections,
    ResponsesUsage? Usage,
    long LatencyMs);
