namespace NetAccel.Managed.Dtos;

public sealed class ManagedSelectionResponse
{
    [JsonPropertyName("assignment_revision")] public int AssignmentRevision { get; set; }
    [JsonPropertyName("selection_revision")] public int SelectionRevision { get; set; }
    [JsonPropertyName("selection_mode")] public string SelectionMode { get; set; } = "";
    [JsonPropertyName("selected_profile_id")] public string? SelectedProfileId { get; set; }
    [JsonPropertyName("effective_profile_id")] public string? EffectiveProfileId { get; set; }
}
