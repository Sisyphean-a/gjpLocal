namespace SwcsScanner.Api.Models.Responses;

public sealed record ApiEnvelope<T>(
    string Code,
    string Message,
    T? Data,
    string TraceId,
    object? Details = null);

public static class ApiEnvelopeFactory
{
    public static ApiEnvelope<T> Success<T>(T data, string traceId, string message = "")
    {
        return new ApiEnvelope<T>("OK", message, data, traceId);
    }

    public static ApiEnvelope<object?> Failure(ApiErrorResponse error, string traceId, object? details = null)
    {
        return new ApiEnvelope<object?>(error.Code, error.Message, null, traceId, details);
    }
}
