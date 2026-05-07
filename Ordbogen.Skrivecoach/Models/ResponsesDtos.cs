using System.Text.Json.Serialization;

namespace Ordbogen.Skrivecoach.Models;

public sealed class ResponsesRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("input")]
    public required string Input { get; set; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    [JsonPropertyName("text")]
    public ResponsesTextConfig? Text { get; set; }

    [JsonPropertyName("store")]
    public bool? Store { get; set; }
}

public sealed class ResponsesTextConfig
{
    [JsonPropertyName("format")]
    public required ResponsesTextFormat Format { get; set; }
}

public sealed class ResponsesTextFormat
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    // For type = "json_schema", these are required at this level
    // (ordbogen.ai's format is flatter than OpenAI's — name/schema are siblings of type, not nested).
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("schema")]
    public object? Schema { get; set; }

    [JsonPropertyName("strict")]
    public bool? Strict { get; set; }
}

public sealed class ResponsesResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("error")]
    public ResponsesError? Error { get; set; }

    [JsonPropertyName("output")]
    public List<ResponsesOutputItem>? Output { get; set; }

    [JsonPropertyName("output_text")]
    public string? OutputText { get; set; }

    [JsonPropertyName("usage")]
    public ResponsesUsage? Usage { get; set; }
}

public sealed class ResponsesError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class ResponsesOutputItem
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public List<ResponsesOutputContent>? Content { get; set; }
}

public sealed class ResponsesOutputContent
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public sealed class ResponsesUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
