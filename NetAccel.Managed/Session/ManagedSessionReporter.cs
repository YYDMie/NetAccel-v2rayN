using System.Collections.Concurrent;
using NetAccel.Managed.Api;

namespace NetAccel.Managed.Session;

public interface IManagedSessionReporter : IAsyncDisposable
{
    void StartBackgroundWork();

    Task<ManagedSessionCreateResult> CreateAsync(
        string profileId,
        string mode,
        DateTimeOffset clientStartedAt,
        CancellationToken ct = default,
        int? selectionRevision = null,
        string? preferredProfileId = null,
        bool isFallback = false);

    Task<ManagedSessionReportResult> ActivateAsync(
        ManagedSessionHandle handle,
        ManagedQualityTarget? qualityTarget,
        CancellationToken ct = default);

    Task<ManagedSessionReportResult> HeartbeatAsync(
        ManagedSessionHandle handle,
        ManagedQualityMeasurement measurement,
        CancellationToken ct = default);

    Task<ManagedSessionReportResult> CloseAsync(
        ManagedSessionHandle handle,
        string reason,
        string code,
        ManagedQualityMeasurement? measurement = null,
        CancellationToken ct = default);

    Task<ManagedSessionReportResult> FailAsync(
        ManagedSessionHandle handle,
        string reason,
        string errorCode,
        string? detail = null,
        ManagedQualityMeasurement? measurement = null,
        CancellationToken ct = default);

    Task<ManagedSessionReportResult> ReplayAsync(CancellationToken ct = default);
}

public sealed class ManagedSessionReporter : IManagedSessionReporter
{
    private const int MaxPendingEvents = 256;

    private readonly IManagedSessionTransport _transport;
    private readonly IManagedSessionOutboxStore _store;
    private readonly IManagedQualityProbe _qualityProbe;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _replayInterval;
    private readonly Func<string, Task>? _logAsync;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<string, HeartbeatRegistration> _heartbeats = new(StringComparer.Ordinal);
    private readonly object _backgroundSync = new();
    private Task? _backgroundTask;

    public ManagedSessionReporter(
        IManagedSessionTransport transport,
        IManagedSessionOutboxStore store,
        IManagedQualityProbe? qualityProbe = null,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? replayInterval = null,
        Func<string, Task>? logAsync = null)
    {
        _transport = transport;
        _store = store;
        _qualityProbe = qualityProbe ?? new TcpManagedQualityProbe();
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(30);
        _replayInterval = replayInterval ?? TimeSpan.FromSeconds(15);
        _logAsync = logAsync;
    }

    public void StartBackgroundWork()
    {
        lock (_backgroundSync)
        {
            _backgroundTask ??= Task.Run(() => RunReplayLoopAsync(_lifetime.Token));
        }
    }

    public async Task<ManagedSessionCreateResult> CreateAsync(
        string profileId,
        string mode,
        DateTimeOffset clientStartedAt,
        CancellationToken ct = default,
        int? selectionRevision = null,
        string? preferredProfileId = null,
        bool isFallback = false)
    {
        if (!TryParsePlanId(profileId, out var planId))
        {
            return new ManagedSessionCreateResult { ErrorCode = ManagedSessionEventCode.SessionCreateFailed };
        }
        int? preferredPlanId = null;
        if (!string.IsNullOrWhiteSpace(preferredProfileId))
        {
            if (!TryParsePlanId(preferredProfileId, out var parsedPreferredPlanId))
            {
                return new ManagedSessionCreateResult { ErrorCode = ManagedSessionEventCode.SessionCreateFailed };
            }
            preferredPlanId = parsedPreferredPlanId;
        }

        string instanceId;
        try
        {
            instanceId = await _transport.GetInstanceIdAsync();
        }
        catch (ManagedSessionIdentityUnavailableException)
        {
            return new ManagedSessionCreateResult { ErrorCode = "missing_instance_identity" };
        }

        var localSessionId = Guid.NewGuid().ToString("D");
        var handle = new ManagedSessionHandle(localSessionId, instanceId, profileId, planId);
        var replay = await EnqueueAndReplayAsync(
            new ManagedSessionOutboxEvent
            {
                Type = ManagedSessionOutboxEventType.CreateSession,
                EventId = Guid.NewGuid().ToString("D"),
                LocalSessionId = localSessionId,
                FallbackSessionId = localSessionId,
                IdempotencyKey = Guid.NewGuid().ToString("D"),
                InstanceId = instanceId,
                PlanId = planId,
                Mode = NormalizeMode(mode),
                ClientStartedAt = clientStartedAt.ToUniversalTime(),
                SelectionRevision = selectionRevision,
                PreferredPlanId = preferredPlanId,
                FallbackReason = isFallback ? "automatic_failover" : null,
            },
            ct);

        return new ManagedSessionCreateResult
        {
            Accepted = replay.Accepted,
            Deferred = replay.Deferred,
            Handle = replay.Accepted ? handle : null,
            ErrorCode = replay.ErrorCode,
        };
    }

    public async Task<ManagedSessionReportResult> ActivateAsync(
        ManagedSessionHandle handle,
        ManagedQualityTarget? qualityTarget,
        CancellationToken ct = default)
    {
        var result = await EnqueueAndReplayAsync(
            NewEvent(ManagedSessionOutboxEventType.ActivateSession, handle),
            ct);
        if (result.Accepted && qualityTarget is { Port: > 0 } && !string.IsNullOrWhiteSpace(qualityTarget.Host))
        {
            StartHeartbeat(handle, qualityTarget);
        }

        return result;
    }

    public Task<ManagedSessionReportResult> HeartbeatAsync(
        ManagedSessionHandle handle,
        ManagedQualityMeasurement measurement,
        CancellationToken ct = default)
    {
        var snapshot = ManagedQualityMapper.Map(measurement);
        return EnqueueAndReplayAsync(
            NewEvent(ManagedSessionOutboxEventType.SessionHeartbeat, handle) with
            {
                QualityState = snapshot.QualityState,
                LatestMetrics = snapshot.Metrics,
            },
            ct);
    }

    public async Task<ManagedSessionReportResult> CloseAsync(
        ManagedSessionHandle handle,
        string reason,
        string code,
        ManagedQualityMeasurement? measurement = null,
        CancellationToken ct = default)
    {
        await StopHeartbeatAsync(handle.LocalSessionId);
        return await EnqueueAndReplayAsync(
            NewEvent(ManagedSessionOutboxEventType.CloseSession, handle) with
            {
                Reason = NormalizeIdentifier(reason, ManagedSessionReason.UserDisconnect),
                Code = NormalizeIdentifier(code, ManagedSessionEventCode.Closed),
                Detail = string.Empty,
                LatestMetrics = measurement == null ? null : ManagedQualityMapper.Map(measurement).Metrics,
            },
            ct);
    }

    public async Task<ManagedSessionReportResult> FailAsync(
        ManagedSessionHandle handle,
        string reason,
        string errorCode,
        string? detail = null,
        ManagedQualityMeasurement? measurement = null,
        CancellationToken ct = default)
    {
        await StopHeartbeatAsync(handle.LocalSessionId);
        return await EnqueueAndReplayAsync(
            NewEvent(ManagedSessionOutboxEventType.FailSession, handle) with
            {
                Reason = NormalizeIdentifier(reason, ManagedSessionReason.StartFailed),
                ErrorCode = NormalizeIdentifier(errorCode, ManagedSessionEventCode.EngineStartFailed),
                Detail = SanitizeDetail(detail),
                LatestMetrics = measurement == null ? null : ManagedQualityMapper.Map(measurement).Metrics,
            },
            ct);
    }

    public async Task<ManagedSessionReportResult> ReplayAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = await _store.LoadAsync(ct);
            return await ReplayLockedAsync(state, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        foreach (var registration in _heartbeats.Values)
        {
            registration.Cancellation.Cancel();
        }

        var tasks = _heartbeats.Values.Select(item => item.Task).ToList();
        Task? background;
        lock (_backgroundSync)
        {
            background = _backgroundTask;
        }
        if (background != null)
        {
            tasks.Add(background);
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }

        foreach (var registration in _heartbeats.Values)
        {
            registration.Cancellation.Dispose();
        }
        _heartbeats.Clear();
        _lifetime.Dispose();
        _gate.Dispose();
    }

    private async Task<ManagedSessionReportResult> EnqueueAndReplayAsync(
        ManagedSessionOutboxEvent outboxEvent,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = await _store.LoadAsync(ct);
            AppendEvent(state, outboxEvent);
            await _store.SaveAsync(state, ct);
            return await ReplayLockedAsync(state, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ManagedSessionReportResult> ReplayLockedAsync(
        ManagedSessionOutboxState state,
        CancellationToken ct)
    {
        while (state.PendingOutbox.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var outboxEvent = state.PendingOutbox[0];
            try
            {
                await SendAsync(state, outboxEvent, ct);
                state.PendingOutbox.RemoveAt(0);
                await _store.SaveAsync(state, ct);
            }
            catch (Exception ex) when (IsDeferred(ex, ct))
            {
                await LogAsync($"Managed session outbox deferred: {ex.GetType().Name}");
                return ManagedSessionReportResult.Queued(GetErrorCode(ex));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await LogAsync($"Managed session event rejected: {ex.GetType().Name}");
                DropSession(state, outboxEvent.LocalSessionId);
                await _store.SaveAsync(state, ct);
                return ManagedSessionReportResult.Rejected(GetErrorCode(ex));
            }
        }

        return ManagedSessionReportResult.Delivered();
    }

    private async Task SendAsync(
        ManagedSessionOutboxState state,
        ManagedSessionOutboxEvent outboxEvent,
        CancellationToken ct)
    {
        if (outboxEvent.Type == ManagedSessionOutboxEventType.CreateSession)
        {
            var created = await _transport.CreateAsync(
                new ManagedSessionCreateRequest
                {
                    IdempotencyKey = Required(outboxEvent.IdempotencyKey, "idempotency_key"),
                    InstanceId = Required(outboxEvent.InstanceId, "instance_id"),
                    PlanId = outboxEvent.PlanId ?? throw new InvalidDataException("Missing plan_id."),
                    Mode = Required(outboxEvent.Mode, "mode"),
                    ClientStartedAt = outboxEvent.ClientStartedAt ?? throw new InvalidDataException("Missing client_started_at."),
                    SelectionRevision = outboxEvent.SelectionRevision,
                    PreferredPlanId = outboxEvent.PreferredPlanId,
                    FallbackReason = outboxEvent.FallbackReason,
                },
                ct);
            state.SessionIdMappings[outboxEvent.LocalSessionId] = created.Id;
            return;
        }

        if (!state.SessionIdMappings.TryGetValue(outboxEvent.LocalSessionId, out var sessionId))
        {
            throw new InvalidDataException("Session event has no server id mapping.");
        }

        switch (outboxEvent.Type)
        {
            case ManagedSessionOutboxEventType.ActivateSession:
                await _transport.ActivateAsync(sessionId, ct);
                break;
            case ManagedSessionOutboxEventType.SessionHeartbeat:
                await _transport.HeartbeatAsync(
                    sessionId,
                    new ManagedSessionHeartbeatRequest
                    {
                        QualityState = Required(outboxEvent.QualityState, "quality_state"),
                        LatestMetrics = outboxEvent.LatestMetrics ?? UnknownMetrics(),
                    },
                    ct);
                break;
            case ManagedSessionOutboxEventType.CloseSession:
            case ManagedSessionOutboxEventType.FailSession:
                await _transport.CloseAsync(
                    sessionId,
                    new ManagedSessionCloseRequest
                    {
                        Reason = Required(outboxEvent.Reason, "reason"),
                        Code = Required(
                            outboxEvent.Type == ManagedSessionOutboxEventType.FailSession
                                ? outboxEvent.ErrorCode
                                : outboxEvent.Code,
                            outboxEvent.Type == ManagedSessionOutboxEventType.FailSession ? "error_code" : "code"),
                        Detail = outboxEvent.Detail ?? string.Empty,
                        LatestMetrics = outboxEvent.LatestMetrics,
                    },
                    ct);
                state.SessionIdMappings.Remove(outboxEvent.LocalSessionId);
                break;
            default:
                throw new InvalidDataException($"Unknown session event type: {outboxEvent.Type}.");
        }
    }

    private void StartHeartbeat(ManagedSessionHandle handle, ManagedQualityTarget target)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var registration = new HeartbeatRegistration(cancellation, Task.CompletedTask, new ManagedQualityWindow());
        registration.Task = Task.Run(() => RunHeartbeatLoopAsync(handle, target, registration.QualityWindow, cancellation.Token));
        if (!_heartbeats.TryAdd(handle.LocalSessionId, registration))
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    private async Task RunHeartbeatLoopAsync(
        ManagedSessionHandle handle,
        ManagedQualityTarget target,
        ManagedQualityWindow qualityWindow,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_heartbeatInterval, ct);
                var measurement = qualityWindow.Add(await _qualityProbe.MeasureAsync(target, ct));
                await HeartbeatAsync(handle, measurement, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await LogAsync($"Managed session heartbeat stopped: {ex.GetType().Name}");
        }
    }

    private async Task StopHeartbeatAsync(string localSessionId)
    {
        if (!_heartbeats.TryRemove(localSessionId, out var registration))
        {
            return;
        }

        registration.Cancellation.Cancel();
        try
        {
            await registration.Task;
        }
        catch (OperationCanceledException)
        {
        }
        registration.Cancellation.Dispose();
    }

    private async Task RunReplayLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ReplayAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                await LogAsync($"Managed session replay failed: {ex.GetType().Name}");
            }

            await Task.Delay(_replayInterval, ct);
        }
    }

    private static void AppendEvent(ManagedSessionOutboxState state, ManagedSessionOutboxEvent outboxEvent)
    {
        if (outboxEvent.Type == ManagedSessionOutboxEventType.SessionHeartbeat)
        {
            state.PendingOutbox.RemoveAll(item =>
                item.Type == ManagedSessionOutboxEventType.SessionHeartbeat &&
                item.LocalSessionId == outboxEvent.LocalSessionId);
        }

        if (state.PendingOutbox.Count >= MaxPendingEvents)
        {
            var heartbeatIndex = state.PendingOutbox.FindIndex(item => item.Type == ManagedSessionOutboxEventType.SessionHeartbeat);
            if (heartbeatIndex >= 0)
            {
                state.PendingOutbox.RemoveAt(heartbeatIndex);
            }
            else
            {
                throw new InvalidOperationException("Managed session outbox capacity exceeded.");
            }
        }

        state.PendingOutbox.Add(outboxEvent);
    }

    private void DropSession(ManagedSessionOutboxState state, string localSessionId)
    {
        state.PendingOutbox.RemoveAll(item => item.LocalSessionId == localSessionId);
        state.SessionIdMappings.Remove(localSessionId);
        if (_heartbeats.TryGetValue(localSessionId, out var registration))
        {
            registration.Cancellation.Cancel();
        }
    }

    private static bool IsDeferred(Exception exception, CancellationToken callerToken)
    {
        if (exception is OperationCanceledException)
        {
            return !callerToken.IsCancellationRequested;
        }

        if (exception is ManagedSessionIdentityUnavailableException or ManagedNetworkTimeoutException or HttpRequestException)
        {
            return true;
        }

        return exception is ManagedApiException apiException &&
               (apiException.HttpStatusCode is 401 or 403 or 408 or 429 || apiException.HttpStatusCode >= 500);
    }

    private static string? GetErrorCode(Exception exception)
        => exception is ManagedApiException apiException && !string.IsNullOrWhiteSpace(apiException.ErrorCode)
            ? apiException.ErrorCode
            : exception is ManagedSessionIdentityUnavailableException
                ? "missing_instance_identity"
                : null;

    private static ManagedSessionOutboxEvent NewEvent(ManagedSessionOutboxEventType type, ManagedSessionHandle handle)
        => new()
        {
            Type = type,
            EventId = Guid.NewGuid().ToString("D"),
            LocalSessionId = handle.LocalSessionId,
            SessionId = handle.LocalSessionId,
            InstanceId = handle.InstanceId,
        };

    private static ManagedQualityMetrics UnknownMetrics()
        => ManagedQualityMapper.Map(new ManagedQualityMeasurement { SampledAt = DateTimeOffset.UtcNow }).Metrics;

    private static bool TryParsePlanId(string profileId, out int planId)
    {
        var value = profileId.StartsWith("plan-", StringComparison.OrdinalIgnoreCase)
            ? profileId[5..]
            : profileId;
        return int.TryParse(value, out planId) && planId > 0;
    }

    private static string NormalizeMode(string mode)
        => mode.Equals("tun", StringComparison.OrdinalIgnoreCase) ? "tun" : "http";

    private static string NormalizeIdentifier(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Length <= 64 && normalized.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.')
            ? normalized
            : fallback;
    }

    private static string SanitizeDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return string.Empty;
        }

        var normalized = detail.Trim();
        return normalized.Length <= 128 && normalized.All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.' or ':' or ' ')
            ? normalized
            : string.Empty;
    }

    private static string Required(string? value, string field)
        => !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException($"Missing {field}.");

    private async Task LogAsync(string message)
    {
        if (_logAsync != null)
        {
            await _logAsync(message);
        }
    }

    private sealed class HeartbeatRegistration(
        CancellationTokenSource cancellation,
        Task task,
        ManagedQualityWindow qualityWindow)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Task { get; set; } = task;
        public ManagedQualityWindow QualityWindow { get; } = qualityWindow;
    }
}
