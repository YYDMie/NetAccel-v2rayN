namespace NetAccel.Managed.Dtos;

public sealed class RefreshRequest
{
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
}
