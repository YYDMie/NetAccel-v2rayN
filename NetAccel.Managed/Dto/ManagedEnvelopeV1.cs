using System.Text.Json.Serialization;

namespace NetAccel.Managed.Dto;

/// <summary>
/// Stable managed-envelope/v1 DTO.
/// Fields are frozen by contracts/managed-envelope-v1.schema.json.
/// </summary>
public sealed class ManagedEnvelopeV1
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = string.Empty;

    [JsonPropertyName("payload_schema")]
    public string PayloadSchema { get; set; } = string.Empty;

    [JsonPropertyName("assignment_revision")]
    public int AssignmentRevision { get; set; }

    [JsonPropertyName("selection_revision")]
    public int SelectionRevision { get; set; }

    [JsonPropertyName("account_id")]
    public int AccountId { get; set; }

    [JsonPropertyName("instance_id")]
    public string InstanceId { get; set; } = string.Empty;

    [JsonPropertyName("key_id")]
    public string KeyId { get; set; } = string.Empty;

    [JsonPropertyName("issued_at")]
    public string IssuedAt { get; set; } = string.Empty;

    [JsonPropertyName("expires_at")]
    public string ExpiresAt { get; set; } = string.Empty;

    [JsonPropertyName("min_client_version")]
    public string MinClientVersion { get; set; } = string.Empty;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("ephemeral_public_key")]
    public string EphemeralPublicKey { get; set; } = string.Empty;

    [JsonPropertyName("salt")]
    public string Salt { get; set; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = string.Empty;

    [JsonPropertyName("ciphertext")]
    public string Ciphertext { get; set; } = string.Empty;

    [JsonPropertyName("signature_algorithm")]
    public string SignatureAlgorithm { get; set; } = string.Empty;

    [JsonPropertyName("signature_key_id")]
    public string SignatureKeyId { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;
}
