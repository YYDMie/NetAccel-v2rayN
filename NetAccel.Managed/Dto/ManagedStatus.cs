using System.Text.Json.Serialization;

namespace NetAccel.Managed.Dto;

public sealed class ProfileQuality
{
    [JsonPropertyName("latency_ms")]
    public double? LatencyMs { get; set; }

    [JsonPropertyName("stability")]
    public string Stability { get; set; } = string.Empty;

    [JsonPropertyName("sampled_at")]
    public string? SampledAt { get; set; }
}

public sealed class ProfileSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("recommended")]
    public bool Recommended { get; set; }

    [JsonPropertyName("available")]
    public bool Available { get; set; }

    [JsonPropertyName("maintenance")]
    public bool Maintenance { get; set; }

    [JsonPropertyName("capability_status")]
    public string CapabilityStatus { get; set; } = string.Empty;

    [JsonPropertyName("quality")]
    public ProfileQuality Quality { get; set; } = new();

    [JsonPropertyName("unavailable_reason")]
    public string? UnavailableReason { get; set; }
}

public sealed class ManagedStatus
{
    [JsonPropertyName("desired_revision")]
    public int DesiredRevision { get; set; }

    [JsonPropertyName("assignment_revision")]
    public int AssignmentRevision { get; set; }

    [JsonPropertyName("selection_revision")]
    public int SelectionRevision { get; set; }

    [JsonPropertyName("applied_revision")]
    public int? AppliedRevision { get; set; }

    [JsonPropertyName("policy_revision")]
    public int PolicyRevision { get; set; }

    [JsonPropertyName("selection_mode")]
    public string SelectionMode { get; set; } = string.Empty;

    [JsonPropertyName("recommended_profile_id")]
    public string? RecommendedProfileId { get; set; }

    [JsonPropertyName("selected_profile_id")]
    public string? SelectedProfileId { get; set; }

    [JsonPropertyName("effective_profile_id")]
    public string? EffectiveProfileId { get; set; }

    [JsonPropertyName("fallback_profile_ids")]
    public List<string> FallbackProfileIds { get; set; } = [];

    [JsonPropertyName("profiles")]
    public List<ProfileSummary> Profiles { get; set; } = [];

    [JsonPropertyName("instance_revoked")]
    public bool InstanceRevoked { get; set; }

    [JsonPropertyName("account_disabled")]
    public bool AccountDisabled { get; set; }

    [JsonPropertyName("emergency_stop")]
    public bool EmergencyStop { get; set; }

    [JsonPropertyName("mandatory_update")]
    public bool MandatoryUpdate { get; set; }

    [JsonPropertyName("min_client_version")]
    public string MinClientVersion { get; set; } = string.Empty;

    [JsonPropertyName("server_time")]
    public string ServerTime { get; set; } = string.Empty;
}
