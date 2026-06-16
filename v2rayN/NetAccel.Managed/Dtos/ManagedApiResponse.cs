namespace NetAccel.Managed.Dtos;

public sealed class ManagedApiResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("error_code")]
    public string ErrorCode { get; set; } = "";

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }
}
