using System.Text.Json.Serialization;

namespace NetAccel.Managed.Dto;

public sealed class VlessRealitySecurity
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("server_name")]
    public string ServerName { get; set; } = string.Empty;

    [JsonPropertyName("public_key")]
    public string PublicKey { get; set; } = string.Empty;

    [JsonPropertyName("short_id")]
    public string ShortId { get; set; } = string.Empty;

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;
}

public sealed class Hysteria2Security
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("server_name")]
    public string ServerName { get; set; } = string.Empty;

    [JsonPropertyName("insecure")]
    public bool Insecure { get; set; }

    [JsonPropertyName("alpn")]
    public List<string>? Alpn { get; set; }
}

public sealed class VlessCredentials
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("flow")]
    public string? Flow { get; set; }
}

public sealed class Hysteria2Credentials
{
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

public sealed class EndpointInfo
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; }
}

public sealed class TransportInfo
{
    [JsonPropertyName("network")]
    public string Network { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }

    [JsonPropertyName("service_name")]
    public string? ServiceName { get; set; }
}

public sealed class ProfilePolicy
{
    [JsonPropertyName("allow_system_proxy")]
    public bool AllowSystemProxy { get; set; }

    [JsonPropertyName("allow_tun")]
    public bool AllowTun { get; set; }

    [JsonPropertyName("allow_local_proxy")]
    public bool AllowLocalProxy { get; set; }
}

public sealed class ManagedProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("revision")]
    public int Revision { get; set; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("recommended")]
    public bool Recommended { get; set; }

    [JsonPropertyName("available")]
    public bool Available { get; set; }

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = string.Empty;

    [JsonPropertyName("core_preference")]
    public string CorePreference { get; set; } = string.Empty;

    [JsonPropertyName("endpoint")]
    public EndpointInfo Endpoint { get; set; } = new();

    [JsonPropertyName("credentials")]
    public object? Credentials { get; set; }

    [JsonPropertyName("transport")]
    public TransportInfo Transport { get; set; } = new();

    [JsonPropertyName("security")]
    public object? Security { get; set; }

    [JsonPropertyName("policy")]
    public ProfilePolicy Policy { get; set; } = new();
}

public sealed class ClientPolicy
{
    [JsonPropertyName("allow_classic_mode")]
    public bool AllowClassicMode { get; set; }

    [JsonPropertyName("allow_tun")]
    public bool AllowTun { get; set; }

    [JsonPropertyName("allow_local_proxy")]
    public bool AllowLocalProxy { get; set; }

    [JsonPropertyName("allow_manual_selection")]
    public bool AllowManualSelection { get; set; }

    [JsonPropertyName("allow_automatic_failover")]
    public bool AllowAutomaticFailover { get; set; }

    [JsonPropertyName("offline_grace_seconds")]
    public int OfflineGraceSeconds { get; set; }
}

public sealed class ManagedConfigPayload
{
    [JsonPropertyName("payload_schema")]
    public string PayloadSchema { get; set; } = string.Empty;

    [JsonPropertyName("assignment_revision")]
    public int AssignmentRevision { get; set; }

    [JsonPropertyName("selection_revision")]
    public int SelectionRevision { get; set; }

    [JsonPropertyName("recommended_profile_id")]
    public string? RecommendedProfileId { get; set; }

    [JsonPropertyName("fallback_profile_ids")]
    public List<string> FallbackProfileIds { get; set; } = [];

    [JsonPropertyName("profiles")]
    public List<ManagedProfile> Profiles { get; set; } = [];

    [JsonPropertyName("routing_policy")]
    public object? RoutingPolicy { get; set; }

    [JsonPropertyName("dns_policy")]
    public object? DnsPolicy { get; set; }

    [JsonPropertyName("client_policy")]
    public ClientPolicy? ClientPolicy { get; set; }
}
