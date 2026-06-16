namespace NetAccel.Managed.Dtos;

public sealed class HeartbeatRequest
{
    [JsonPropertyName("client_version")] public string ClientVersion { get; set; } = "";
    [JsonPropertyName("core_versions")] public Dictionary<string, string> CoreVersions { get; set; } = new();
    [JsonPropertyName("capabilities")] public List<string> Capabilities { get; set; } = new();
    [JsonPropertyName("current_profile_id")] public string? CurrentProfileId { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
}
