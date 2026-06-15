namespace NetAccel.Managed.Api;

/// <summary>
/// Exception thrown when a managed API call fails with a structured error.
/// </summary>
public sealed class ManagedApiException : Exception
{
    public int HttpStatusCode { get; }
    public string ErrorCode { get; }
    public string RequestId { get; }

    public ManagedApiException(string message, int httpStatusCode, string errorCode, string requestId)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        ErrorCode = errorCode;
        RequestId = requestId;
    }
}

/// <summary>
/// Exception for network timeout.
/// </summary>
public sealed class ManagedNetworkTimeoutException : Exception
{
    public ManagedNetworkTimeoutException(string message) : base(message) { }
}

/// <summary>
/// Exception for operation cancellation.
/// </summary>
public sealed class ManagedOperationCancelledException : Exception
{
    public ManagedOperationCancelledException(string message) : base(message) { }
}
