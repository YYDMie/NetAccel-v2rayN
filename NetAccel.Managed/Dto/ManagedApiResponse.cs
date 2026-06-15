using System.Text.Json.Serialization;

namespace NetAccel.Managed.Dto;

/// <summary>
/// Unified API response envelope: { code, message, error_code, data }
/// </summary>
public sealed class ManagedApiResponse<T>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("error_code")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public T Data { get; set; } = default!;
}

/// <summary>
/// Untyped envelope for deserializing when the payload shape is not yet known.
/// </summary>
public sealed class ManagedApiResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("error_code")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}
