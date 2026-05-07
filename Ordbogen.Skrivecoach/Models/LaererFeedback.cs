using System.Text.Json.Serialization;

namespace Ordbogen.Skrivecoach.Models;

public enum Klassetrin
{
    Indskoling,   // 0.-3. klasse
    Mellemtrin,   // 4.-6. klasse
    Udskoling,    // 7.-9. klasse
    Gymnasium,    // 1.g-3.g
    Voksen        // generel/voksen skribent
}

public sealed record UdfordringPunkt(
    [property: JsonPropertyName("omraade")] string Omraade,
    [property: JsonPropertyName("kommentar")] string Kommentar,
    [property: JsonPropertyName("eksempel_fra_tekst")] string? EksempelFraTekst,
    [property: JsonPropertyName("forslag")] string? Forslag);

public sealed record LaererFeedback(
    [property: JsonPropertyName("ros")] string Ros,
    [property: JsonPropertyName("udfordringer")] List<UdfordringPunkt> Udfordringer,
    [property: JsonPropertyName("naeste_skridt")] List<string> NaesteSkridt,
    [property: JsonPropertyName("samlet")] string Samlet);

public sealed record LaererFeedbackResultat(
    LaererFeedback? Feedback,
    Klassetrin Klassetrin,
    ResponsesUsage? Usage,
    long LatencyMs);
