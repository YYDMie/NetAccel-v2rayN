namespace NetAccel.Managed.Dtos;

public sealed class ManagedSelectionRequest
{
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
    [JsonPropertyName("profile_id")] public string? ProfileId { get; set; }
    [JsonPropertyName("expected_selection_revision")] public int ExpectedSelectionRevision { get; set; }
}
