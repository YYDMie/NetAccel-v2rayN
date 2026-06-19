using System.Text.Json.Serialization;

namespace NetAccel.Managed.Session;

public static class ManagedSessionEventCode
{
    public const string Closed = "closed";
    public const string SessionCreateFailed = "session_create_failed";
    public const string EngineStartFailed = "engine_start_failed";
    public const string EngineExited = "engine_exited";
    public const string ProxyCleanupFailed = "proxy_cleanup_failed";
    public const string ClientCrashed = "client_crashed";
    public const string ConnectCancelled = "connect_cancelled";
}

public static class ManagedSessionReason
{
    public const string UserDisconnect = "user_disconnect";
    public const string RouteSwitch = "route_switch";
    public const string ApplicationExit = "app_exit";
    public const string StartFailed = "start_failed";
    public const string CoreExited = "core_exited";
    public const string Cancelled = "cancelled";
}

public sealed record ManagedSessionHandle(
    string LocalSessionId,
    string InstanceId,
    string ProfileId,
    int PlanId);

public sealed record ManagedSessionCreateResult
{
    public bool Accepted { get; init; }
    public bool Deferred { get; init; }
    public ManagedSessionHandle? Handle { get; init; }
    public string? ErrorCode { get; init; }
}

public sealed record ManagedSessionReportResult
{
    public bool Accepted { get; init; }
    public bool Deferred { get; init; }
    public string? ErrorCode { get; init; }

    public static ManagedSessionReportResult Delivered()
        => new() { Accepted = true };

    public static ManagedSessionReportResult Queued(string? errorCode = null)
        => new() { Accepted = true, Deferred = true, ErrorCode = errorCode };

    public static ManagedSessionReportResult Rejected(string? errorCode)
        => new() { ErrorCode = errorCode };
}

public sealed record ManagedQualityTarget(string Host, int Port);

public sealed record ManagedQualityMeasurement
{
    public double? LatencyMs { get; init; }
    public double? JitterMs { get; init; }
    public double? LossRate { get; init; }
    public double? DownloadMbps { get; init; }
    public double? UploadMbps { get; init; }
    public string Source { get; init; } = "unknown";
    public string Accuracy { get; init; } = "unknown";
    public DateTimeOffset SampledAt { get; init; }
}

public sealed record ManagedQualityMetrics
{
    [JsonPropertyName("latency_ms")]
    public double? LatencyMs { get; init; }

    [JsonPropertyName("jitter_ms")]
    public double? JitterMs { get; init; }

    [JsonPropertyName("loss_rate")]
    public double? LossRate { get; init; }

    [JsonPropertyName("download_mbps")]
    public double? DownloadMbps { get; init; }

    [JsonPropertyName("upload_mbps")]
    public double? UploadMbps { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = "unknown";

    [JsonPropertyName("accuracy")]
    public string Accuracy { get; init; } = "unknown";

    [JsonPropertyName("sampled_at")]
    public DateTimeOffset SampledAt { get; init; }
}

public sealed record ManagedQualitySnapshot(string QualityState, ManagedQualityMetrics Metrics);

public static class ManagedQualityMapper
{
    public static ManagedQualitySnapshot Map(ManagedQualityMeasurement measurement)
    {
        var latency = NormalizeNonNegative(measurement.LatencyMs);
        var jitter = NormalizeNonNegative(measurement.JitterMs);
        var loss = measurement.LossRate is >= 0 and <= 1 ? measurement.LossRate : null;
        var download = NormalizePositive(measurement.DownloadMbps);
        var upload = NormalizePositive(measurement.UploadMbps);

        var state = latency == null && jitter == null && loss == null && download == null && upload == null
            ? "unknown"
            : loss is >= 0.1 || latency is >= 300 || jitter is >= 100
                ? "degraded"
                : loss is >= 0.03 || latency is >= 150 || jitter is >= 50
                    ? "fair"
                    : "good";

        return new ManagedQualitySnapshot(
            state,
            new ManagedQualityMetrics
            {
                LatencyMs = latency,
                JitterMs = jitter,
                LossRate = loss,
                DownloadMbps = download,
                UploadMbps = upload,
                Source = string.IsNullOrWhiteSpace(measurement.Source) ? "unknown" : measurement.Source,
                Accuracy = string.IsNullOrWhiteSpace(measurement.Accuracy) ? "unknown" : measurement.Accuracy,
                SampledAt = measurement.SampledAt == default ? DateTimeOffset.UtcNow : measurement.SampledAt,
            });
    }

    private static double? NormalizeNonNegative(double? value)
        => value is >= 0 && double.IsFinite(value.Value) ? value : null;

    private static double? NormalizePositive(double? value)
        => value is > 0 && double.IsFinite(value.Value) ? value : null;
}

public sealed record ManagedSessionCreateRequest
{
    [JsonPropertyName("idempotency_key")]
    public required string IdempotencyKey { get; init; }

    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("plan_id")]
    public required int PlanId { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("client_started_at")]
    public required DateTimeOffset ClientStartedAt { get; init; }

    [JsonPropertyName("selection_revision")]
    public int? SelectionRevision { get; init; }

    [JsonPropertyName("preferred_plan_id")]
    public int? PreferredPlanId { get; init; }

    [JsonPropertyName("fallback_reason")]
    public string? FallbackReason { get; init; }
}

public sealed record ManagedSessionHeartbeatRequest
{
    [JsonPropertyName("latest_metrics")]
    public required ManagedQualityMetrics LatestMetrics { get; init; }

    [JsonPropertyName("quality_state")]
    public required string QualityState { get; init; }
}

public sealed record ManagedSessionCloseRequest
{
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;

    [JsonPropertyName("latest_metrics")]
    public ManagedQualityMetrics? LatestMetrics { get; init; }
}

public sealed record ManagedConnectionSession
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("idempotency_key")]
    public string IdempotencyKey { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;
}
