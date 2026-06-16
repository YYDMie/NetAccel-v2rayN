namespace NetAccel.Managed.Dtos;

public sealed class ManagedApiError : Exception
{
    public int HttpStatusCode { get; }
    public string ErrorCode { get; }
    public string? RequestId { get; }

    public ManagedApiError(int httpStatusCode, string errorCode, string message, string? requestId = null)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        ErrorCode = errorCode;
        RequestId = requestId;
    }
}
