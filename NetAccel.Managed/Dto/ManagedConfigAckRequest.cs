using System.Text.Json.Serialization;

namespace NetAccel.Managed.Dto;

/// <summary>
/// Client request body for POST /client/managed/config/{revision}/ack.
/// </summary>
public sealed class ManagedConfigAckRequest
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("client_version")]
    public string ClientVersion { get; set; } = string.Empty;

    [JsonPropertyName("core_versions")]
    public Dictionary<string, string> CoreVersions { get; set; } = new();

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("error_detail")]
    public string? ErrorDetail { get; set; }
}
