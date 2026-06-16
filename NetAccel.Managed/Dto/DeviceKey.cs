using System.Text.Json.Serialization;

namespace NetAccel.Managed.Dto;

/// <summary>
/// Request to register a P-256 device public key for the current instance.
/// </summary>
public sealed class DeviceKeyRegisterRequest
{
    [JsonPropertyName("key_id")]
    public string KeyId { get; set; } = string.Empty;

    [JsonPropertyName("public_key_pem")]
    public string PublicKeyPem { get; set; } = string.Empty;
}

/// <summary>
/// Response from a successful device key registration.
/// </summary>
public sealed class DeviceKeyRegisterResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("key_id")]
    public string KeyId { get; set; } = string.Empty;

    [JsonPropertyName("instance_id")]
    public string InstanceId { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>
/// In-memory device key metadata (never includes the private key).
/// </summary>
public sealed class DeviceKeyInfo
{
    public string KeyId { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
}
