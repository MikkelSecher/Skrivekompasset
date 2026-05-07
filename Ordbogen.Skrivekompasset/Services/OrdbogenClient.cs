using System.Net.Http.Json;
using System.Text.Json;
using Ordbogen.Skrivekompasset.Models;

namespace Ordbogen.Skrivekompasset.Services;

public sealed class OrdbogenClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OrdbogenClient> _log;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public OrdbogenClient(HttpClient http, ILogger<OrdbogenClient> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<ResponsesResponse> CreateResponseAsync(ResponsesRequest request, CancellationToken ct)
    {
        using var httpResponse = await _http.PostAsJsonAsync("responses", request, JsonOptions, ct);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await SafeReadAsync(httpResponse, ct);
            _log.LogWarning("ordbogen.ai returned {Status}: {Body}", (int)httpResponse.StatusCode, body);
            throw new OrdbogenApiException(
                $"ordbogen.ai returned HTTP {(int)httpResponse.StatusCode}: {Truncate(body, 500)}",
                statusCode: (int)httpResponse.StatusCode);
        }

        var parsed = await httpResponse.Content.ReadFromJsonAsync<ResponsesResponse>(JsonOptions, ct)
            ?? throw new OrdbogenApiException("ordbogen.ai returnerede et tomt svar.");

        if (parsed.Error is { Message: not null })
        {
            throw new OrdbogenApiException(parsed.Error.Message, errorCode: parsed.Error.Code);
        }

        return parsed;
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return "<kunne ikke læse responskrop>";
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
