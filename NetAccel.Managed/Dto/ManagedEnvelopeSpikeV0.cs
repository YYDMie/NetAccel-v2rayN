using System.Text.Json.Serialization;

namespace NetAccel.Managed.Dto;

/// <summary>
/// R1-only spike envelope. Decrypt using access-token SHA-256 derived AES-256-GCM key.
/// Explicitly marked spike-only; WP-04/R2 will replace with stable managed-envelope/v1.
/// </summary>
public sealed class ManagedEnvelopeSpikeV0
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

    [JsonPropertyName("issued_at")]
    public string IssuedAt { get; set; } = string.Empty;

    [JsonPropertyName("expires_at")]
    public string ExpiresAt { get; set; } = string.Empty;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = string.Empty;

    [JsonPropertyName("ciphertext")]
    public string Ciphertext { get; set; } = string.Empty;
}
