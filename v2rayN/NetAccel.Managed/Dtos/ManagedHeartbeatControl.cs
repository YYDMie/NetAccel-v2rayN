namespace NetAccel.Managed.Dtos;

public sealed class ManagedHeartbeatControl
{
    [JsonPropertyName("desired_revision")] public int DesiredRevision { get; set; }
    [JsonPropertyName("assignment_revision")] public int AssignmentRevision { get; set; }
    [JsonPropertyName("selection_revision")] public int SelectionRevision { get; set; }
    [JsonPropertyName("selection_mode")] public string SelectionMode { get; set; } = "";
    [JsonPropertyName("effective_profile_id")] public string? EffectiveProfileId { get; set; }
    [JsonPropertyName("policy_revision")] public int PolicyRevision { get; set; }
    [JsonPropertyName("instance_revoked")] public bool InstanceRevoked { get; set; }
    [JsonPropertyName("account_disabled")] public bool AccountDisabled { get; set; }
    [JsonPropertyName("emergency_stop")] public bool EmergencyStop { get; set; }
    [JsonPropertyName("mandatory_update")] public bool MandatoryUpdate { get; set; }
    [JsonPropertyName("min_client_version")] public string MinClientVersion { get; set; } = "";
    [JsonPropertyName("server_time")] public string ServerTime { get; set; } = "";
}
