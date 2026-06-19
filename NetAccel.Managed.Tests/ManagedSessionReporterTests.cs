using System.Net;
using System.Text.Json;
using NetAccel.Managed.Api;
using NetAccel.Managed.Session;
using Xunit;

namespace NetAccel.Managed.Tests;

public sealed class ManagedSessionReporterTests
{
    [Fact]
    public async Task OnlineLifecycleIsDeliveredInOrderAndMappingIsRemovedOnClose()
    {
        var transport = new FakeTransport();
        var store = new MemoryOutboxStore();
        await using var reporter = NewReporter(transport, store);
        var ct = TestContext.Current.CancellationToken;

        var created = await reporter.CreateAsync(
            "plan-42",
            "http",
            DateTimeOffset.Parse("2026-06-19T01:00:00Z"),
            ct,
            selectionRevision: 9,
            preferredProfileId: "plan-43",
            isFallback: true);
        Assert.True(created.Accepted);
        Assert.False(created.Deferred);
        var handle = Assert.IsType<ManagedSessionHandle>(created.Handle);

        Assert.True((await reporter.ActivateAsync(handle, null, ct)).Accepted);
        Assert.True((await reporter.HeartbeatAsync(handle, new ManagedQualityMeasurement
        {
            LatencyMs = 42.5,
            JitterMs = 6,
            LossRate = 0.01,
            DownloadMbps = 80,
            Source = "client_probe",
            Accuracy = "measured",
            SampledAt = DateTimeOffset.Parse("2026-06-19T01:00:30Z"),
        }, ct)).Accepted);
        Assert.True((await reporter.CloseAsync(
            handle,
            ManagedSessionReason.UserDisconnect,
            ManagedSessionEventCode.Closed,
            ct: ct)).Accepted);

        Assert.Equal(new[] { "create", "activate", "heartbeat", "close" }, transport.Calls);
        var createRequest = Assert.Single(transport.CreateRequests);
        Assert.Equal(42, createRequest.PlanId);
        Assert.Equal(9, createRequest.SelectionRevision);
        Assert.Equal(43, createRequest.PreferredPlanId);
        Assert.Equal("automatic_failover", createRequest.FallbackReason);
        var heartbeat = Assert.Single(transport.HeartbeatRequests);
        Assert.Equal("good", heartbeat.QualityState);
        Assert.Equal(42.5, heartbeat.LatestMetrics.LatencyMs);
        Assert.Equal(80, heartbeat.LatestMetrics.DownloadMbps);
        Assert.Empty(store.State.PendingOutbox);
        Assert.Empty(store.State.SessionIdMappings);
    }

    [Fact]
    public async Task OfflineEventsArePersistedThenReplayedInOriginalOrder()
    {
        var transport = new FakeTransport { Offline = true };
        var store = new MemoryOutboxStore();
        await using var reporter = NewReporter(transport, store);
        var ct = TestContext.Current.CancellationToken;

        var created = await reporter.CreateAsync("plan-7", "tun", DateTimeOffset.UtcNow, ct);
        var handle = Assert.IsType<ManagedSessionHandle>(created.Handle);
        Assert.True(created.Accepted);
        Assert.True(created.Deferred);

        Assert.True((await reporter.ActivateAsync(handle, null, ct)).Deferred);
        Assert.True((await reporter.HeartbeatAsync(handle, UnknownMeasurement(), ct)).Deferred);
        Assert.Equal(
            new[]
            {
                ManagedSessionOutboxEventType.CreateSession,
                ManagedSessionOutboxEventType.ActivateSession,
                ManagedSessionOutboxEventType.SessionHeartbeat,
            },
            store.State.PendingOutbox.Select(item => item.Type));

        transport.Offline = false;
        var replayed = await reporter.ReplayAsync(ct);

        Assert.True(replayed.Accepted);
        Assert.False(replayed.Deferred);
        Assert.Equal(new[] { "create", "activate", "heartbeat" }, transport.Calls);
        Assert.Empty(store.State.PendingOutbox);
        Assert.Single(store.State.SessionIdMappings);
    }

    [Fact]
    public async Task RepeatedOfflineHeartbeatsCoalesceWithoutLosingLifecycleEvents()
    {
        var transport = new FakeTransport { Offline = true };
        var store = new MemoryOutboxStore();
        await using var reporter = NewReporter(transport, store);
        var ct = TestContext.Current.CancellationToken;

        var created = await reporter.CreateAsync("plan-8", "http", DateTimeOffset.UtcNow, ct);
        var handle = Assert.IsType<ManagedSessionHandle>(created.Handle);
        await reporter.ActivateAsync(handle, null, ct);
        await reporter.HeartbeatAsync(handle, UnknownMeasurement(), ct);
        await reporter.HeartbeatAsync(handle, new ManagedQualityMeasurement
        {
            LatencyMs = 88,
            Source = "tcp_connect",
            Accuracy = "measured",
            SampledAt = DateTimeOffset.UtcNow,
        }, ct);

        Assert.Equal(3, store.State.PendingOutbox.Count);
        var heartbeat = Assert.Single(store.State.PendingOutbox.Where(item =>
            item.Type == ManagedSessionOutboxEventType.SessionHeartbeat));
        Assert.Equal(88, heartbeat.LatestMetrics?.LatencyMs);
    }

    [Fact]
    public async Task PermanentCreateRejectionDropsDependentEventsAndDoesNotStartSession()
    {
        var transport = new FakeTransport { PermanentErrorCode = "managed_session_plan_not_effective" };
        var store = new MemoryOutboxStore();
        await using var reporter = NewReporter(transport, store);

        var result = await reporter.CreateAsync(
            "plan-9",
            "http",
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.False(result.Accepted);
        Assert.Null(result.Handle);
        Assert.Equal("managed_session_plan_not_effective", result.ErrorCode);
        Assert.Empty(store.State.PendingOutbox);
        Assert.Empty(store.State.SessionIdMappings);
    }

    [Fact]
    public async Task FileOutboxContainsNoCredentialConfigEndpointOrUnsafeDetail()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "session-outbox.json");
        var transport = new FakeTransport { Offline = true };
        await using var reporter = new ManagedSessionReporter(
            transport,
            new FileManagedSessionOutboxStore(path),
            replayInterval: TimeSpan.FromHours(1));
        var ct = TestContext.Current.CancellationToken;

        var created = await reporter.CreateAsync("plan-10", "http", DateTimeOffset.UtcNow, ct);
        var handle = Assert.IsType<ManagedSessionHandle>(created.Handle);
        await reporter.ActivateAsync(handle, null, ct);
        await reporter.HeartbeatAsync(handle, UnknownMeasurement(), ct);
        await reporter.FailAsync(
            handle,
            ManagedSessionReason.StartFailed,
            ManagedSessionEventCode.EngineStartFailed,
            "Bearer secret-token https://line.example.com:443 password=hidden",
            ct: ct);

        var json = await File.ReadAllTextAsync(path, ct);
        Assert.DoesNotContain("secret-token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("line.example.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("host", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("engine_start_failed", json, StringComparison.Ordinal);

        var persisted = await new FileManagedSessionOutboxStore(path).LoadAsync(ct);
        var createEvent = Assert.Single(persisted.PendingOutbox.Where(item =>
            item.Type == ManagedSessionOutboxEventType.CreateSession));
        Assert.Equal(createEvent.LocalSessionId, createEvent.FallbackSessionId);
        Assert.All(
            persisted.PendingOutbox.Where(item => item.Type != ManagedSessionOutboxEventType.CreateSession),
            item => Assert.Equal(item.LocalSessionId, item.SessionId));
        var failEvent = Assert.Single(persisted.PendingOutbox.Where(item =>
            item.Type == ManagedSessionOutboxEventType.FailSession));
        Assert.Equal(ManagedSessionEventCode.EngineStartFailed, failEvent.ErrorCode);
        Assert.Null(failEvent.Code);
    }

    [Fact]
    public async Task ActivatedSessionProducesMeasuredHeartbeatAndCloseStopsLoop()
    {
        var transport = new FakeTransport();
        var probe = new FakeQualityProbe();
        var store = new MemoryOutboxStore();
        await using var reporter = NewReporter(
            transport,
            store,
            probe,
            heartbeatInterval: TimeSpan.FromMilliseconds(10));
        var ct = TestContext.Current.CancellationToken;

        var created = await reporter.CreateAsync("plan-11", "http", DateTimeOffset.UtcNow, ct);
        var handle = Assert.IsType<ManagedSessionHandle>(created.Handle);
        await reporter.ActivateAsync(handle, new ManagedQualityTarget("assigned.example", 443), ct);
        await transport.HeartbeatObserved.Task.WaitAsync(TimeSpan.FromSeconds(2), ct);
        await reporter.CloseAsync(handle, ManagedSessionReason.UserDisconnect, ManagedSessionEventCode.Closed, ct: ct);
        var countAfterClose = transport.HeartbeatRequests.Count;
        await Task.Delay(40, ct);

        Assert.Equal(1, probe.CallCount);
        Assert.Equal(countAfterClose, transport.HeartbeatRequests.Count);
        Assert.Equal("assigned.example", probe.LastTarget?.Host);
        Assert.Equal(27, transport.HeartbeatRequests[0].LatestMetrics.LatencyMs);
    }

    [Fact]
    public void QualityMapperUsesUnknownInsteadOfFakeZeroAndPreservesRealSpeed()
    {
        var unknown = ManagedQualityMapper.Map(UnknownMeasurement());
        Assert.Equal("unknown", unknown.QualityState);
        Assert.Null(unknown.Metrics.LatencyMs);
        Assert.Null(unknown.Metrics.LossRate);
        Assert.Null(unknown.Metrics.DownloadMbps);

        var degraded = ManagedQualityMapper.Map(new ManagedQualityMeasurement
        {
            LatencyMs = 320,
            JitterMs = 20,
            LossRate = 0.12,
            DownloadMbps = 12.5,
            UploadMbps = 3.25,
            Source = "engine_stats",
            Accuracy = "measured",
            SampledAt = DateTimeOffset.Parse("2026-06-19T02:00:00Z"),
        });
        Assert.Equal("degraded", degraded.QualityState);
        Assert.Equal(12.5, degraded.Metrics.DownloadMbps);
        Assert.Equal(3.25, degraded.Metrics.UploadMbps);
    }

    [Fact]
    public void QualityWindowMapsMeasuredStabilityWithoutInventingSpeed()
    {
        var window = new ManagedQualityWindow(3);
        window.Add(new ManagedQualityMeasurement
        {
            LatencyMs = 40,
            LossRate = 0,
            Source = "tcp_connect",
            Accuracy = "measured",
            SampledAt = DateTimeOffset.Parse("2026-06-19T02:00:00Z"),
        });
        var second = window.Add(new ManagedQualityMeasurement
        {
            LatencyMs = 55,
            LossRate = 0,
            Source = "tcp_connect",
            Accuracy = "measured",
            SampledAt = DateTimeOffset.Parse("2026-06-19T02:00:30Z"),
        });

        Assert.Equal(15, second.JitterMs);
        Assert.Equal(0, second.LossRate);
        Assert.Null(second.DownloadMbps);
        Assert.Null(second.UploadMbps);
    }

    [Fact]
    public async Task CorruptedOutboxIsResetOnNextAtomicSave()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "session-outbox.json");
        await File.WriteAllTextAsync(path, "{broken", TestContext.Current.CancellationToken);
        var store = new FileManagedSessionOutboxStore(path);

        var state = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Empty(state.PendingOutbox);
        await store.SaveAsync(state, TestContext.Current.CancellationToken);

        var reloaded = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, reloaded.SchemaVersion);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task FailureCodesComeFromTheMasterContract()
    {
        var path = Path.Combine(FindContractsRoot(), "error-codes.json");
        await using var stream = File.OpenRead(path);
        var codes = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(
            stream,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(codes);
        foreach (var code in new[]
                 {
                     ManagedSessionEventCode.SessionCreateFailed,
                     ManagedSessionEventCode.EngineStartFailed,
                     ManagedSessionEventCode.EngineExited,
                     ManagedSessionEventCode.ProxyCleanupFailed,
                     ManagedSessionEventCode.ClientCrashed,
                     ManagedSessionEventCode.ConnectCancelled,
                 })
        {
            Assert.Contains(code, codes.Keys);
        }
    }

    private static ManagedSessionReporter NewReporter(
        FakeTransport transport,
        MemoryOutboxStore store,
        IManagedQualityProbe? qualityProbe = null,
        TimeSpan? heartbeatInterval = null)
        => new(
            transport,
            store,
            qualityProbe,
            heartbeatInterval,
            replayInterval: TimeSpan.FromHours(1));

    private static ManagedQualityMeasurement UnknownMeasurement()
        => new() { SampledAt = DateTimeOffset.Parse("2026-06-19T00:00:00Z") };

    private static string FindContractsRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("NETACCEL_MASTER_REPO");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var configuredContracts = Path.Combine(configuredRoot, "contracts");
            if (File.Exists(Path.Combine(configuredContracts, "error-codes.json")))
            {
                return configuredContracts;
            }
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "contracts");
            if (File.Exists(Path.Combine(candidate, "error-codes.json")))
            {
                return candidate;
            }

            var siblingCandidate = Path.Combine(directory.FullName, "NetAccel", "contracts");
            if (File.Exists(Path.Combine(siblingCandidate, "error-codes.json")))
            {
                return siblingCandidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the NetAccel contracts directory.");
    }

    private sealed class MemoryOutboxStore : IManagedSessionOutboxStore
    {
        public ManagedSessionOutboxState State { get; private set; } = new();

        public Task<ManagedSessionOutboxState> LoadAsync(CancellationToken ct = default)
            => Task.FromResult(State);

        public Task SaveAsync(ManagedSessionOutboxState state, CancellationToken ct = default)
        {
            State = state;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTransport : IManagedSessionTransport
    {
        public bool Offline { get; set; }
        public string? PermanentErrorCode { get; set; }
        public List<string> Calls { get; } = [];
        public List<ManagedSessionCreateRequest> CreateRequests { get; } = [];
        public List<ManagedSessionHeartbeatRequest> HeartbeatRequests { get; } = [];
        public TaskCompletionSource HeartbeatObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> GetInstanceIdAsync() => Task.FromResult("550e8400-e29b-41d4-a716-446655440002");

        public Task<ManagedConnectionSession> CreateAsync(ManagedSessionCreateRequest request, CancellationToken ct = default)
        {
            ThrowIfUnavailable();
            Calls.Add("create");
            CreateRequests.Add(request);
            return Task.FromResult(new ManagedConnectionSession
            {
                Id = $"server-{request.IdempotencyKey}",
                IdempotencyKey = request.IdempotencyKey,
                State = "starting",
            });
        }

        public Task ActivateAsync(string sessionId, CancellationToken ct = default)
        {
            ThrowIfUnavailable();
            Calls.Add("activate");
            return Task.CompletedTask;
        }

        public Task HeartbeatAsync(string sessionId, ManagedSessionHeartbeatRequest request, CancellationToken ct = default)
        {
            ThrowIfUnavailable();
            Calls.Add("heartbeat");
            HeartbeatRequests.Add(request);
            HeartbeatObserved.TrySetResult();
            return Task.CompletedTask;
        }

        public Task CloseAsync(string sessionId, ManagedSessionCloseRequest request, CancellationToken ct = default)
        {
            ThrowIfUnavailable();
            Calls.Add("close");
            return Task.CompletedTask;
        }

        private void ThrowIfUnavailable()
        {
            if (Offline)
            {
                throw new HttpRequestException("offline");
            }

            if (PermanentErrorCode != null)
            {
                throw new ManagedApiException("rejected", (int)HttpStatusCode.BadRequest, PermanentErrorCode, "request-id");
            }
        }
    }

    private sealed class FakeQualityProbe : IManagedQualityProbe
    {
        public int CallCount { get; private set; }
        public ManagedQualityTarget? LastTarget { get; private set; }

        public Task<ManagedQualityMeasurement> MeasureAsync(ManagedQualityTarget target, CancellationToken ct = default)
        {
            CallCount++;
            LastTarget = target;
            return Task.FromResult(new ManagedQualityMeasurement
            {
                LatencyMs = 27,
                LossRate = 0,
                Source = "tcp_connect",
                Accuracy = "measured",
                SampledAt = DateTimeOffset.UtcNow,
            });
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"netaccel-session-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
