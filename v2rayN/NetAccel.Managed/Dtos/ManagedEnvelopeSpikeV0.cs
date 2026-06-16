namespace NetAccel.Managed.Dtos;

public sealed class ManagedEnvelopeSpikeV0
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "";
    [JsonPropertyName("payload_schema")] public string PayloadSchema { get; set; } = "";
    [JsonPropertyName("assignment_revision")] public int AssignmentRevision { get; set; }
    [JsonPropertyName("selection_revision")] public int SelectionRevision { get; set; }
    [JsonPropertyName("account_id")] public int AccountId { get; set; }
    [JsonPropertyName("instance_id")] public string InstanceId { get; set; } = "";
    [JsonPropertyName("issued_at")] public string IssuedAt { get; set; } = "";
    [JsonPropertyName("expires_at")] public string ExpiresAt { get; set; } = "";
    [JsonPropertyName("algorithm")] public string Algorithm { get; set; } = "";
    [JsonPropertyName("nonce")] public string Nonce { get; set; } = "";
    [JsonPropertyName("ciphertext")] public string Ciphertext { get; set; } = "";
}
