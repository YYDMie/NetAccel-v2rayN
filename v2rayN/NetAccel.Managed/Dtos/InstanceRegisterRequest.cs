namespace NetAccel.Managed.Dtos;

public sealed class InstanceRegisterRequest
{
    [JsonPropertyName("installation_key")] public string InstallationKey { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("client_product")] public string ClientProduct { get; set; } = "";
    [JsonPropertyName("platform")] public string Platform { get; set; } = "";
    [JsonPropertyName("platform_version")] public string PlatformVersion { get; set; } = "";
    [JsonPropertyName("arch")] public string Arch { get; set; } = "";
    [JsonPropertyName("client_version")] public string ClientVersion { get; set; } = "";
    [JsonPropertyName("core_versions")] public Dictionary<string, string> CoreVersions { get; set; } = new();
    [JsonPropertyName("capabilities")] public List<string> Capabilities { get; set; } = new();
}
