using NetAccel.Managed;
using NetAccel.Managed.Api;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Runtime;
using NetAccel.Managed.Selection;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedConnectionCoordinatorTests
{
    [Fact]
    public async Task StartAsync_SystemProxy_AcquiresOwnerAndConnectsRecommendedProfile()
    {
        var runner = new FakeCoreRunner();
        await using var owner = NewOwner();
        var coordinator = new ManagedConnectionCoordinator(owner, runner);
        var payload = CreatePayload();

        var result = await coordinator.StartAsync(new ManagedConnectionStartRequest
        {
            Payload = payload,
            NetworkMode = ManagedConnectionMode.SystemProxy,
        });

        Assert.True(result.Success);
        Assert.Equal(ManagedConnectionState.Connected, coordinator.Status.State);
        Assert.Equal("plan-a", coordinator.Status.EffectiveProfileId);
        Assert.Equal(ConnectionOwner.Managed, owner.CurrentOwner);
        Assert.Equal(ManagedConnectionMode.SystemProxy, runner.StartedModes.Single());
        Assert.False(runner.StartedRuntime.Single().EnableTun);
    }

    [Fact]
    public async Task StartAsync_AcksAppliedAfterSuccessfulRuntimeApplication()
    {
        var runner = new FakeCoreRunner();
        await using var owner = NewOwner();
        var config = new FakeConfigService();
        var coordinator = new ManagedConnectionCoordinator(
            owner,
            runner,
            configService: config,
            clientVersion: "1.0.0",
            coreVersions: new Dictionary<string, string> { ["xray"] = "test" });
        var payload = CreatePayload();

        var result = await coordinator.StartAsync(new ManagedConnectionStartRequest { Payload = payload });

        Assert.True(result.Success);
        var ack = Assert.Single(config.Acks);
        Assert.Equal(payload.AssignmentRevision, ack.Revision);
        Assert.Equal("applied", ack.Status);
        Assert.Equal("1.0.0", ack.ClientVersion);
        Assert.Equal("test", ack.CoreVersions["xray"]);
    }

    [Fact]
    public async Task StopAsync_StopsCoreAndReleasesOwner()
    {
        var runner = new FakeCoreRunner();
        await using var owner = NewOwner();
        var coordinator = new ManagedConnectionCoordinator(owner, runner);
        await coordinator.StartAsync(new ManagedConnectionStartRequest { Payload = CreatePayload() });

        var result = await coordinator.StopAsync();

        Assert.True(result.Success);
        Assert.Equal(ManagedConnectionState.Ready, coordinator.Status.State);
        Assert.Equal(ConnectionOwner.None, owner.CurrentOwner);
        Assert.Equal(1, runner.StopCount);
    }

    [Fact]
    public async Task StopAsync_CleansUpCoreWithNonCancellableStopToken()
    {
        var runner = new FakeCoreRunner { FailStopWhenTokenCanBeCanceled = true };
        await using var owner = NewOwner();
        var coordinator = new ManagedConnectionCoordinator(owner, runner);
        await coordinator.StartAsync(new ManagedConnectionStartRequest { Payload = CreatePayload() });

        using var cts = new CancellationTokenSource();
        var result = await coordinator.StopAsync(cts.Token);

        Assert.True(result.Success);
        Assert.Equal(ManagedConnectionState.Ready, coordinator.Status.State);
        Assert.Equal(ConnectionOwner.None, owner.CurrentOwner);
        Assert.Equal(1, runner.StopCount);
    }

    [Fact]
    public async Task StateMachine_PublishesStartConnectStopReadySequence()
    {
        var runner = new FakeCoreRunner();
        await using var owner = NewOwner();
        var coordinator = new ManagedConnectionCoordinator(owner, runner);
        var states = new List<ManagedConnectionState>();
        coordinator.StatusChanged += status => states.Add(status.State);

        await coordinator.StartAsync(new ManagedConnectionStartRequest { Payload = CreatePayload() });
        await coordinator.StopAsync();

        Assert.Equal(
            new[]
            {
                ManagedConnectionState.Starting,
                ManagedConnectionState.Connected,
                ManagedConnectionState.Stopping,
                ManagedConnectionState.Ready,
            },
            states);
    }

    [Fact]
    public async Task StartAsync_Tun_RequiresClientAndProfilePolicy()
    {
        var runner = new FakeCoreRunner();
        await using var owner = NewOwner();
        var coordinator = new ManagedConnectionCoordinator(owner, runner);
        var payload = CreatePayload();
        payload.ClientPolicy!.AllowTun = false;

        var result = await coordinator.StartAsync(new ManagedConnectionStartRequest
        {
            Payload = payload,
            NetworkMode = ManagedConnectionMode.Tun,
        });

        Assert.False(result.Success);
        Assert.Equal(ManagedConnectionFailureKind.NoAssignment, result.FailureKind);
        Assert.Equal(ConnectionOwner.None, owner.CurrentOwner);
        Assert.Empty(runner.StartedProfileIds);
    }

    [Fact]
    public async Task StartAsync_Tun_StartsWithTunEnabledWhenAllowed()
    {
        var runner = new FakeCoreRunner();
        await using var owner = NewOwner();
        var coordinator = new ManagedConnectionCoordinator(owner, runner);

        var result = await coordinator.StartAsync(new ManagedConnectionStartRequest
        {
            Payload = CreatePayload(),
            NetworkMode = ManagedConnectionMode.Tun,
        });

        Assert.True(result.Success);
        Assert.Equal(ManagedConnectionMode.Tun, runner.StartedModes.Single());
        Assert.True(runner.StartedRuntime.Single().EnableTun);
    }

    [Fact]
    public async Task StartAsync_ManualUnassignedProfileIsRejectedWithoutStartingCore()
    {
        var runner = new FakeCoreRunner();
        await using var owner = NewOwner();
        var coordinator = new ManagedConnectionCoordinator(owner, runner);

        var result = await coordinator.StartAsync(new ManagedConnectionStartRequest
        {
            Payload = CreatePayload(),
            SelectionMode = ManagedProfileSelectionMode.Manual,
            ProfileId = "plan-missing",
        });

        Assert.False(result.Success);
        Assert.Equal(ManagedConnectionFailureKind.ProfileNotAssigned, result.FailureKind);
        Assert.Equal(ConnectionOwner.None, owner.CurrentOwner);
        Assert.Empty(runner.StartedProfileIds);
    }

    [Fact]
    public async Task StartAsync_ManualUnavailableProfileIsRejectedWithoutStartingCore()
    {
        var runner = new FakeCoreRunner();
        await using var owner = NewOwner();
        var coordinator = new ManagedConnectionCoordinator(owner, runner);
        var payload = CreatePayload();
        payload.Profiles.Single(p => p.Id == "plan-b").Available = false;

        var result = await coordinator.StartAsync(new ManagedConnectionStartRequest
        {
            Payload = payload,
            SelectionMode = ManagedProfileSelectionMode.Manual,
            ProfileId = "plan-b",
        });

        Assert.False(result.Success);
        Assert.Equal(ManagedConnectionFailureKind.ProfileUnavailable, result.FailureKind);
        Assert.Equal(ConnectionOwner.None, owner.CurrentOwner);
        Assert.Empty(runner.StartedProfileIds);
    }

    [Fact]
    public async Task SwitchAsync_FailedManualSwitchRestoresPreviousProfileAndDoesNotPersist()
    {
        var runner = new FakeCoreRunner();
        await using var owner = NewOwner();
        var selection = new FakeSelectionService();
        var coordinator = new ManagedConnectionCoordinator(owner, runner, selectionService: selection);
        var payload = CreatePayload();

        await coordinator.StartAsync(new ManagedConnectionStartRequest { Payload = payload });
        runner.FailProfileIds.Add("plan-b");

        var result = await coordinator.SwitchAsync(payload, ManagedProfileSelectionMode.Manual, "plan-b");

        Assert.False(result.Success);
        Assert.Equal("plan-a", coordinator.Status.EffectiveProfileId);
        Assert.Equal(ManagedConnectionState.Connected, coordinator.Status.State);
        Assert.Equal(new[] { "plan-a", "plan-b", "plan-a" }, runner.StartedProfileIds);
        Assert.Empty(selection.Calls);
    }

    [Fact]
    public async Task SwitchAsync_CancelledManualSwitchRestoresPreviousProfile()
    {
        var runner = new FakeCoreRunner();
        await using var owner = NewOwner();
        var selection = new FakeSelectionService();
        var coordinator = new ManagedConnectionCoordinator(owner, runner, selectionService: selection);
        var payload = CreatePayload();

        await coordinator.StartAsync(new ManagedConnectionStartRequest { Payload = payload });
        runner.CancelProfileIds.Add("plan-b");

        var result = await coordinator.SwitchAsync(payload, ManagedProfileSelectionMode.Manual, "plan-b");

        Assert.False(result.Success);
        Assert.Equal(ManagedConnectionFailureKind.Cancelled, result.FailureKind);
        Assert.Equal(ManagedConnectionState.Connected, coordinator.Status.State);
        Assert.Equal("plan-a", coordinator.Status.EffectiveProfileId);
        Assert.Equal(new[] { "plan-a", "plan-b", "plan-a" }, runner.StartedProfileIds);
        Assert.Equal(2, runner.StopCount);
        Assert.Empty(selection.Calls);
    }

    [Fact]
    public async Task SwitchAsync_SuccessfulManualSwitchPersistsSelection()
    {
        var runner = new FakeCoreRunner();
        await using var owner = NewOwner();
        var selection = new FakeSelectionService();
        var coordinator = new ManagedConnectionCoordinator(owner, runner, selectionService: selection);
        var payload = CreatePayload();

        await coordinator.StartAsync(new ManagedConnectionStartRequest { Payload = payload });
        var result = await coordinator.SwitchAsync(payload, ManagedProfileSelectionMode.Manual, "plan-b");

        Assert.True(result.Success);
        Assert.Equal("plan-b", coordinator.Status.EffectiveProfileId);
        var call = Assert.Single(selection.Calls);
        Assert.Equal("manual", call.Mode);
        Assert.Equal("plan-b", call.ProfileId);
        Assert.Equal(payload.SelectionRevision, call.ExpectedRevision);
    }

    [Fact]
    public async Task RecoverFromCoreFault_UsesAuthorizedFallbackWithoutOverwritingManualPreference()
    {
        var runner = new FakeCoreRunner();
        await using var owner = NewOwner();
        var selection = new FakeSelectionService();
        var coordinator = new ManagedConnectionCoordinator(owner, runner, selectionService: selection);
        var payload = CreatePayload();

        await coordinator.StartAsync(new ManagedConnectionStartRequest
        {
            Payload = payload,
            SelectionMode = ManagedProfileSelectionMode.Manual,
            ProfileId = "plan-a",
            PersistSelection = true,
        });
        selection.Calls.Clear();
        runner.FailProfileIds.Add("plan-a");
        runner.IsRunningOverride = false;

        var result = await coordinator.RecoverFromCoreFaultAsync("core_exited");

        Assert.True(result.Success);
        Assert.True(coordinator.Status.IsFallback);
        Assert.Equal("plan-a", coordinator.Status.PreferredProfileId);
        Assert.Equal("plan-b", coordinator.Status.EffectiveProfileId);
        Assert.Empty(selection.Calls);
    }

    [Fact]
    public async Task StartAsync_ReleasesOwnerWhenCoreStartFails()
    {
        var runner = new FakeCoreRunner();
        runner.FailProfileIds.Add("plan-a");
        runner.FailProfileIds.Add("plan-b");
        await using var owner = NewOwner();
        var coordinator = new ManagedConnectionCoordinator(owner, runner);

        var result = await coordinator.StartAsync(new ManagedConnectionStartRequest { Payload = CreatePayload() });

        Assert.False(result.Success);
        Assert.Equal(ManagedConnectionFailureKind.CoreStartFailed, result.FailureKind);
        Assert.Equal(ConnectionOwner.None, owner.CurrentOwner);
        Assert.True(runner.StopCount >= 1);
    }

    [Fact]
    public async Task StartAsync_FallbackSkipsProfileIdsOutsideCurrentPayload()
    {
        var runner = new FakeCoreRunner();
        runner.FailProfileIds.Add("plan-a");
        await using var owner = NewOwner();
        var coordinator = new ManagedConnectionCoordinator(owner, runner);
        var payload = CreatePayload();
        payload.FallbackProfileIds = ["plan-missing", "plan-b"];

        var result = await coordinator.StartAsync(new ManagedConnectionStartRequest { Payload = payload });

        Assert.True(result.Success);
        Assert.Equal("plan-b", coordinator.Status.EffectiveProfileId);
        Assert.Equal(new[] { "plan-a", "plan-b" }, runner.StartedProfileIds);
    }

    private static ConnectionOwnershipCoordinator NewOwner()
        => new(new InMemoryConnectionOwnershipStore(), useGlobalMutex: false);

    private static ManagedConfigPayload CreatePayload()
    {
        return new ManagedConfigPayload
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 7,
            SelectionRevision = 3,
            RecommendedProfileId = "plan-a",
            FallbackProfileIds = ["plan-b"],
            Profiles =
            [
                CreateVlessProfile("plan-a", priority: 100, recommended: true),
                CreateVlessProfile("plan-b", priority: 80, recommended: false),
            ],
            RoutingPolicy = new ManagedRoutingPolicy { Mode = "managed_default" },
            DnsPolicy = new ManagedDnsPolicy
            {
                NormalDns = "https://cloudflare-dns.com/dns-query",
                TunDns = "https://cloudflare-dns.com/dns-query",
            },
            ClientPolicy = new ClientPolicy
            {
                AllowTun = true,
                AllowManualSelection = true,
                AllowAutomaticFailover = true,
                OfflineGraceSeconds = 3600,
            },
        };
    }

    private static ManagedProfile CreateVlessProfile(string id, int priority, bool recommended)
    {
        return new ManagedProfile
        {
            Id = id,
            Revision = 1,
            DisplayName = id,
            Priority = priority,
            Recommended = recommended,
            Available = true,
            Protocol = "vless",
            CorePreference = "xray",
            Endpoint = new EndpointInfo { Host = $"{id}.example.com", Port = 443 },
            Transport = new TransportInfo { Network = "raw" },
            VlessCredentials = new VlessCredentials { Uuid = id == "plan-a" ? "00000000-0000-0000-0000-000000000001" : "00000000-0000-0000-0000-000000000002" },
            VlessRealitySecurity = new VlessRealitySecurity
            {
                Type = "reality",
                ServerName = $"{id}.example.com",
                PublicKey = "test-public-key",
                ShortId = "01",
                Fingerprint = "chrome",
            },
            Policy = new ProfilePolicy
            {
                AllowSystemProxy = true,
                AllowTun = true,
                AllowLocalProxy = false,
            },
        };
    }

    private sealed class FakeCoreRunner : IManagedCoreRunner
    {
        public HashSet<string> FailProfileIds { get; } = [];
        public HashSet<string> CancelProfileIds { get; } = [];
        public List<string> StartedProfileIds { get; } = [];
        public List<ManagedConnectionMode> StartedModes { get; } = [];
        public List<ManagedRuntimeConfig> StartedRuntime { get; } = [];
        public int StopCount { get; private set; }
        public bool? IsRunningOverride { get; set; }
        public bool FailStopWhenTokenCanBeCanceled { get; set; }
        public bool IsRunning => IsRunningOverride ?? StartedProfileIds.Count > StopCount;

        public Task StartAsync(ManagedRuntimeConfig runtimeConfig, ManagedConnectionMode mode, CancellationToken ct = default)
        {
            var profileId = runtimeConfig.Node.IndexId.Replace("managed:", "", StringComparison.Ordinal);
            StartedProfileIds.Add(profileId);
            StartedModes.Add(mode);
            StartedRuntime.Add(runtimeConfig);
            IsRunningOverride = null;
            if (FailProfileIds.Contains(profileId))
            {
                throw new InvalidOperationException($"start failed: {profileId}");
            }

            if (CancelProfileIds.Contains(profileId))
            {
                throw new OperationCanceledException(ct);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            if (FailStopWhenTokenCanBeCanceled && ct.CanBeCanceled)
            {
                throw new OperationCanceledException(ct);
            }

            StopCount++;
            IsRunningOverride = false;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSelectionService : IManagedSelectionService
    {
        public List<(string Mode, string? ProfileId, int ExpectedRevision)> Calls { get; } = [];
        public SelectionResult Result { get; set; } = new()
        {
            Kind = SelectionResultKind.Success,
            State = new ManagedSelectionResponse
            {
                AssignmentRevision = 7,
                SelectionRevision = 4,
                SelectionMode = "manual",
                SelectedProfileId = "plan-b",
                EffectiveProfileId = "plan-b",
            },
        };

        public Task<PolicyStatusResult<ManagedPolicy>> GetPolicyAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<PolicyStatusResult<ManagedStatus>> GetStatusAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SelectionResult> UpdateSelectionAsync(string mode, string? profileId, int expectedSelectionRevision, CancellationToken ct = default)
        {
            Calls.Add((mode, profileId, expectedSelectionRevision));
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeConfigService : IManagedConfigService
    {
        public List<(int Revision, string Status, string ClientVersion, Dictionary<string, string> CoreVersions)> Acks { get; } = [];

        public Task<ConfigAckResult> AckConfigAsync(
            int revision,
            string status,
            string clientVersion,
            Dictionary<string, string> coreVersions,
            string? errorCode = null,
            string? errorDetail = null,
            CancellationToken ct = default)
        {
            Acks.Add((revision, status, clientVersion, coreVersions));
            return Task.FromResult(new ConfigAckResult { Kind = ConfigAckResultKind.Success });
        }
    }
}
