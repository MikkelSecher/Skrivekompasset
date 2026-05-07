namespace Ordbogen.Skrivecoach.Models;

public sealed class OrdbogenOptions
{
    public const string SectionName = "Ordbogen";

    public string BaseUrl { get; set; } = "https://api.ordbogen.ai/v1";
    public string Model { get; set; } = "odin-large";
    public string ApiKey { get; set; } = string.Empty;
    public int MaxInputCharacters { get; set; } = 5000;
}
