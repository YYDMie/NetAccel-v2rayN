using System.Text.Json.Serialization;

namespace NetAccel.Managed.Dto;

public sealed class ManagedSelectionRequest
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    [JsonPropertyName("profile_id")]
    public string? ProfileId { get; set; }

    [JsonPropertyName("expected_selection_revision")]
    public int ExpectedSelectionRevision { get; set; }
}

public sealed class ManagedSelectionResponse
{
    [JsonPropertyName("assignment_revision")]
    public int AssignmentRevision { get; set; }

    [JsonPropertyName("selection_revision")]
    public int SelectionRevision { get; set; }

    [JsonPropertyName("selection_mode")]
    public string SelectionMode { get; set; } = string.Empty;

    [JsonPropertyName("selected_profile_id")]
    public string? SelectedProfileId { get; set; }

    [JsonPropertyName("effective_profile_id")]
    public string? EffectiveProfileId { get; set; }
}
