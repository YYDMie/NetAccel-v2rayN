using System.Text.Json;
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

[JsonConverter(typeof(ManagedProfileJsonConverter))]
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

    [JsonIgnore]
    public VlessCredentials? VlessCredentials { get; set; }

    [JsonIgnore]
    public Hysteria2Credentials? Hysteria2Credentials { get; set; }

    [JsonPropertyName("transport")]
    public TransportInfo Transport { get; set; } = new();

    [JsonIgnore]
    public VlessRealitySecurity? VlessRealitySecurity { get; set; }

    [JsonIgnore]
    public Hysteria2Security? Hysteria2Security { get; set; }

    [JsonPropertyName("policy")]
    public ProfilePolicy Policy { get; set; } = new();
}

public sealed class ManagedRoutingPolicy
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;
}

public sealed class ManagedDnsPolicy
{
    [JsonPropertyName("normal_dns")]
    public string NormalDns { get; set; } = string.Empty;

    [JsonPropertyName("tun_dns")]
    public string TunDns { get; set; } = string.Empty;
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

    public ManagedRoutingPolicy? GetRoutingPolicy()
        => ConvertObject<ManagedRoutingPolicy>(RoutingPolicy);

    public ManagedDnsPolicy? GetDnsPolicy()
        => ConvertObject<ManagedDnsPolicy>(DnsPolicy);

    private static T? ConvertObject<T>(object? value)
    {
        return value switch
        {
            null => default,
            T typed => typed,
            JsonElement element => element.Deserialize<T>(),
            _ => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value)),
        };
    }
}

internal sealed class ManagedProfileJsonConverter : JsonConverter<ManagedProfile>
{
    public override ManagedProfile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var profile = new ManagedProfile
        {
            Id = ReadString(root, "id"),
            Revision = ReadInt(root, "revision"),
            DisplayName = ReadString(root, "display_name"),
            Region = ReadNullableString(root, "region"),
            Priority = ReadInt(root, "priority"),
            Recommended = ReadBool(root, "recommended"),
            Available = ReadBool(root, "available"),
            Protocol = ReadString(root, "protocol"),
            CorePreference = ReadString(root, "core_preference"),
            Endpoint = ReadObject<EndpointInfo>(root, "endpoint", options) ?? new EndpointInfo(),
            Transport = ReadObject<TransportInfo>(root, "transport", options) ?? new TransportInfo(),
            Policy = ReadObject<ProfilePolicy>(root, "policy", options) ?? new ProfilePolicy(),
        };

        if (root.TryGetProperty("credentials", out var credentials))
        {
            if (profile.Protocol.Equals("vless", StringComparison.OrdinalIgnoreCase))
            {
                profile.VlessCredentials = credentials.Deserialize<VlessCredentials>(options);
            }
            else if (profile.Protocol.Equals("hysteria2", StringComparison.OrdinalIgnoreCase))
            {
                profile.Hysteria2Credentials = credentials.Deserialize<Hysteria2Credentials>(options);
            }
        }

        if (root.TryGetProperty("security", out var security))
        {
            var type = security.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : string.Empty;
            if (type?.Equals("reality", StringComparison.OrdinalIgnoreCase) == true)
            {
                profile.VlessRealitySecurity = security.Deserialize<VlessRealitySecurity>(options);
            }
            else if (type?.Equals("tls", StringComparison.OrdinalIgnoreCase) == true)
            {
                profile.Hysteria2Security = security.Deserialize<Hysteria2Security>(options);
            }
        }

        return profile;
    }

    public override void Write(Utf8JsonWriter writer, ManagedProfile value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteNumber("revision", value.Revision);
        writer.WriteString("display_name", value.DisplayName);
        if (value.Region != null)
        {
            writer.WriteString("region", value.Region);
        }
        writer.WriteNumber("priority", value.Priority);
        writer.WriteBoolean("recommended", value.Recommended);
        writer.WriteBoolean("available", value.Available);
        writer.WriteString("protocol", value.Protocol);
        writer.WriteString("core_preference", value.CorePreference);
        writer.WritePropertyName("endpoint");
        JsonSerializer.Serialize(writer, value.Endpoint, options);
        writer.WritePropertyName("credentials");
        JsonSerializer.Serialize(writer, value.Protocol.Equals("vless", StringComparison.OrdinalIgnoreCase)
            ? value.VlessCredentials
            : value.Hysteria2Credentials as object, options);
        writer.WritePropertyName("transport");
        JsonSerializer.Serialize(writer, value.Transport, options);
        writer.WritePropertyName("security");
        JsonSerializer.Serialize(writer, value.VlessRealitySecurity != null
            ? value.VlessRealitySecurity
            : value.Hysteria2Security as object, options);
        writer.WritePropertyName("policy");
        JsonSerializer.Serialize(writer, value.Policy, options);
        writer.WriteEndObject();
    }

    private static string ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string? ReadNullableString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static bool ReadBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static T? ReadObject<T>(JsonElement root, string name, JsonSerializerOptions options)
        => root.TryGetProperty(name, out var value) ? value.Deserialize<T>(options) : default;
}
