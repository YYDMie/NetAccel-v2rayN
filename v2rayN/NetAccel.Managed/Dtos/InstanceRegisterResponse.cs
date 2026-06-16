namespace NetAccel.Managed.Dtos;

public sealed class InstanceRegisterResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("credential")] public string Credential { get; set; } = "";
    [JsonPropertyName("expires_at")] public string ExpiresAt { get; set; } = "";
}
