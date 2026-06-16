namespace NetAccel.Managed.Dtos;

public sealed class ManagedConfigAck
{
    [JsonPropertyName("revision")] public int Revision { get; set; }
    [JsonPropertyName("stage")] public string Stage { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("applied_revision")] public int? AppliedRevision { get; set; }
    [JsonPropertyName("client_version")] public string ClientVersion { get; set; } = "";
    [JsonPropertyName("core_versions")] public Dictionary<string, string> CoreVersions { get; set; } = new();
    [JsonPropertyName("error_code")] public string? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("error_detail")] public string? ErrorDetail { get; set; }
    [JsonPropertyName("client_applied_at")] public string ClientAppliedAt { get; set; } = "";
}
