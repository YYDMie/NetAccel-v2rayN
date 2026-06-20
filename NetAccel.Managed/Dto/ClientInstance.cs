using System.Text.Json.Serialization;

namespace NetAccel.Managed.Dto;

public sealed class ClientCapabilities
{
    [JsonPropertyName("tun")]
    public bool Tun { get; set; }

    [JsonPropertyName("system_proxy")]
    public bool SystemProxy { get; set; }

    [JsonPropertyName("process_rule")]
    public bool ProcessRule { get; set; }

    [JsonPropertyName("clash_api")]
    public bool ClashApi { get; set; }

    [JsonPropertyName("v2ray_stats")]
    public bool V2rayStats { get; set; }

    [JsonPropertyName("latency_probe_tcp")]
    public bool LatencyProbeTcp { get; set; }

    [JsonPropertyName("latency_probe_udp")]
    public bool LatencyProbeUdp { get; set; }

    [JsonPropertyName("secure_storage")]
    public bool SecureStorage { get; set; }
}

public sealed class InstanceCredential
{
    [JsonPropertyName("credential")]
    public string Credential { get; set; } = string.Empty;

    [JsonPropertyName("scope")]
    public List<string> Scope { get; set; } = [];

    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }
}

public sealed class ClientInstanceRegisterRequest
{
    [JsonPropertyName("installation_key")]
    public string InstallationKey { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("platform_version")]
    public string PlatformVersion { get; set; } = string.Empty;

    [JsonPropertyName("arch")]
    public string Arch { get; set; } = string.Empty;

    [JsonPropertyName("client_version")]
    public string ClientVersion { get; set; } = string.Empty;

    [JsonPropertyName("engine_version")]
    public string EngineVersion { get; set; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public ClientCapabilities Capabilities { get; set; } = new();

    /// <summary>
    /// Old instance credential proof when recovering an existing binding.
    /// </summary>
}

public sealed class ClientInstanceRegisterResponse
{
    [JsonPropertyName("instance")]
    public RegisteredClientInstance Instance { get; set; } = new();

    [JsonPropertyName("instance_credential")]
    public InstanceCredential InstanceCredential { get; set; } = new();
}

public sealed class RegisteredClientInstance
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

public sealed class HeartbeatRequest
{
    [JsonPropertyName("client_version")]
    public string ClientVersion { get; set; } = string.Empty;

    [JsonPropertyName("engine_version")]
    public string EngineVersion { get; set; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public ClientCapabilities Capabilities { get; set; } = new();

}

public sealed class HeartbeatControl
{
    [JsonPropertyName("desired_revision")]
    public int DesiredRevision { get; set; }

    [JsonPropertyName("assignment_revision")]
    public int AssignmentRevision { get; set; }

    [JsonPropertyName("selection_revision")]
    public int SelectionRevision { get; set; }

    [JsonPropertyName("selection_mode")]
    public string SelectionMode { get; set; } = string.Empty;

    [JsonPropertyName("effective_profile_id")]
    public string? EffectiveProfileId { get; set; }

    [JsonPropertyName("policy_revision")]
    public int PolicyRevision { get; set; }

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

public sealed class HeartbeatResponse
{
    [JsonPropertyName("instance_status")]
    public string InstanceStatus { get; set; } = string.Empty;

    [JsonPropertyName("control")]
    public HeartbeatControl Control { get; set; } = new();
}

public sealed class CredentialRotateResponse
{
    [JsonPropertyName("instance_credential")]
    public InstanceCredential InstanceCredential { get; set; } = new();
}
