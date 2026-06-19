using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetAccel.Managed.Session;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ManagedSessionOutboxEventType
{
    CreateSession,
    ActivateSession,
    SessionHeartbeat,
    CloseSession,
    FailSession,
}

public sealed record ManagedSessionOutboxEvent
{
    [JsonPropertyName("type")]
    public required ManagedSessionOutboxEventType Type { get; init; }

    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("local_session_id")]
    public required string LocalSessionId { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; init; }

    [JsonPropertyName("instance_id")]
    public string? InstanceId { get; init; }

    [JsonPropertyName("fallback_session_id")]
    public string? FallbackSessionId { get; init; }

    [JsonPropertyName("plan_id")]
    public int? PlanId { get; init; }

    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    [JsonPropertyName("client_started_at")]
    public DateTimeOffset? ClientStartedAt { get; init; }

    [JsonPropertyName("selection_revision")]
    public int? SelectionRevision { get; init; }

    [JsonPropertyName("preferred_plan_id")]
    public int? PreferredPlanId { get; init; }

    [JsonPropertyName("fallback_reason")]
    public string? FallbackReason { get; init; }

    [JsonPropertyName("quality_state")]
    public string? QualityState { get; init; }

    [JsonPropertyName("latest_metrics")]
    public ManagedQualityMetrics? LatestMetrics { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }
}

public sealed record ManagedSessionOutboxState
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("pending_outbox")]
    public List<ManagedSessionOutboxEvent> PendingOutbox { get; init; } = [];

    [JsonPropertyName("session_id_mappings")]
    public Dictionary<string, string> SessionIdMappings { get; init; } = new(StringComparer.Ordinal);
}

public interface IManagedSessionOutboxStore
{
    Task<ManagedSessionOutboxState> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(ManagedSessionOutboxState state, CancellationToken ct = default);
}

public sealed class FileManagedSessionOutboxStore : IManagedSessionOutboxStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public FileManagedSessionOutboxStore(string path)
    {
        _path = path;
    }

    public async Task<ManagedSessionOutboxState> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path))
        {
            return new ManagedSessionOutboxState();
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var state = await JsonSerializer.DeserializeAsync<ManagedSessionOutboxState>(stream, JsonOptions, ct);
            return state is { SchemaVersion: 1 } &&
                   state.PendingOutbox != null &&
                   state.SessionIdMappings != null
                ? state
                : new ManagedSessionOutboxState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ManagedSessionOutboxState();
        }
    }

    public async Task SaveAsync(ManagedSessionOutboxState state, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Session outbox path has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _path + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, ct);
                await stream.FlushAsync(ct);
            }

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
