namespace Ordbogen.Skrivecoach.Services;

public sealed class OrdbogenApiException : Exception
{
    public int? StatusCode { get; }
    public string? ErrorCode { get; }

    public OrdbogenApiException(string message, int? statusCode = null, string? errorCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}
