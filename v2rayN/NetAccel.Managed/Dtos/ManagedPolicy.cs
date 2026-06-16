namespace NetAccel.Managed.Dtos;

public sealed class ManagedPolicy
{
    [JsonPropertyName("policy_revision")] public int PolicyRevision { get; set; }
    [JsonPropertyName("ui_mode")] public string UiMode { get; set; } = "";
    [JsonPropertyName("allow_classic_mode")] public bool AllowClassicMode { get; set; }
    [JsonPropertyName("allow_tun")] public bool AllowTun { get; set; }
    [JsonPropertyName("allow_local_proxy")] public bool AllowLocalProxy { get; set; }
    [JsonPropertyName("allow_manual_selection")] public bool AllowManualSelection { get; set; }
    [JsonPropertyName("allow_automatic_failover")] public bool AllowAutomaticFailover { get; set; }
    [JsonPropertyName("offline_grace_seconds")] public int OfflineGraceSeconds { get; set; }
    [JsonPropertyName("mandatory_update")] public bool MandatoryUpdate { get; set; }
    [JsonPropertyName("min_client_version")] public string MinClientVersion { get; set; } = "";
    [JsonPropertyName("server_time")] public string ServerTime { get; set; } = "";
}
