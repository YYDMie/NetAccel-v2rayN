namespace NetAccel.Managed.Dtos;

public sealed class HeartbeatResponse
{
    [JsonPropertyName("instance_status")] public string InstanceStatus { get; set; } = "";
    [JsonPropertyName("control")] public ManagedHeartbeatControl Control { get; set; } = new();
}
