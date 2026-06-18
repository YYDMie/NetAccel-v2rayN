using NetAccel.Managed.Dto;
using NetAccel.Managed.Presentation;
using NetAccel.Managed.Runtime;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedHomeViewModelTests
{
    [Fact]
    public async Task Refresh_WithAssignment_BecomesIdle()
    {
        var coordinator = new FakeCoordinator();
        using var vm = new ManagedHomeViewModel(coordinator, () => Task.FromResult<ManagedConfigPayload?>(CreatePayload()));

        await vm.RefreshAsync();

        Assert.Equal(ManagedHomeStage.Idle, vm.Stage);
        Assert.Equal("香港智能通道", vm.RouteName);
        Assert.True(vm.IsActionEnabled);
    }

    [Fact]
    public async Task Refresh_WithoutAssignment_ShowsNoAssignment()
    {
        using var vm = new ManagedHomeViewModel(
            new FakeCoordinator(),
            () => Task.FromResult<ManagedConfigPayload?>(new ManagedConfigPayload()));

        await vm.RefreshAsync();

        Assert.Equal(ManagedHomeStage.NoAssignment, vm.Stage);
        Assert.Equal("重新检查", vm.ActionText);
    }

    [Fact]
    public async Task Toggle_StartsAutomaticSystemProxyConnection()
    {
        var coordinator = new FakeCoordinator();
        using var vm = new ManagedHomeViewModel(coordinator, () => Task.FromResult<ManagedConfigPayload?>(CreatePayload()));
        await vm.RefreshAsync();

        await vm.ToggleConnectionAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(coordinator.LastStartRequest);
        Assert.Equal(ManagedConnectionMode.SystemProxy, coordinator.LastStartRequest!.NetworkMode);
        Assert.Equal(ManagedProfileSelectionMode.Automatic, coordinator.LastStartRequest.SelectionMode);
        Assert.Equal(ManagedHomeStage.Connected, vm.Stage);
    }

    [Fact]
    public async Task Toggle_WhenConnected_StopsConnection()
    {
        var coordinator = new FakeCoordinator();
        using var vm = new ManagedHomeViewModel(coordinator, () => Task.FromResult<ManagedConfigPayload?>(CreatePayload()));
        await vm.RefreshAsync();
        await vm.ToggleConnectionAsync(TestContext.Current.CancellationToken);

        await vm.ToggleConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, coordinator.StopCalls);
        Assert.Equal(ManagedHomeStage.Idle, vm.Stage);
    }

    [Fact]
    public async Task TunPolicyFailure_MapsToNeedsPermission()
    {
        var coordinator = new FakeCoordinator
        {
            StartFailure = ManagedConnectionFailureKind.PolicyDenied,
        };
        using var vm = new ManagedHomeViewModel(coordinator, () => Task.FromResult<ManagedConfigPayload?>(CreatePayload()));
        await vm.RefreshAsync();
        vm.NetworkMode = ManagedConnectionMode.Tun;

        await vm.ToggleConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedHomeStage.NeedsPermission, vm.Stage);
        Assert.Contains("授权", vm.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FallbackConnection_DoesNotHideTemporaryRouteReason()
    {
        var coordinator = new FakeCoordinator { UseFallback = true };
        var payload = CreatePayload();
        using var vm = new ManagedHomeViewModel(coordinator, () => Task.FromResult<ManagedConfigPayload?>(payload));
        await vm.RefreshAsync();

        await vm.ToggleConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal("香港备用通道", vm.RouteName);
        Assert.Contains("临时备用", vm.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdatedManualSelection_IsUsedByNextConnection()
    {
        var coordinator = new FakeCoordinator();
        using var vm = new ManagedHomeViewModel(coordinator, () => Task.FromResult<ManagedConfigPayload?>(CreatePayload()));
        await vm.RefreshAsync();
        vm.UpdateSelectionPreference(ManagedProfileSelectionMode.Manual, "p2", 4);

        await vm.ToggleConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedProfileSelectionMode.Manual, coordinator.LastStartRequest!.SelectionMode);
        Assert.Equal("p2", coordinator.LastStartRequest.ProfileId);
        Assert.False(coordinator.LastStartRequest.AllowAutomaticFallback);
        Assert.Equal(4, coordinator.LastStartRequest.Payload.SelectionRevision);
    }

    [Fact]
    public async Task OwnerConflict_MapsToClassicRunning()
    {
        var coordinator = new FakeCoordinator
        {
            StartFailure = ManagedConnectionFailureKind.OwnerConflict,
        };
        using var vm = new ManagedHomeViewModel(coordinator, () => Task.FromResult<ManagedConfigPayload?>(CreatePayload()));
        await vm.RefreshAsync();

        await vm.ToggleConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedHomeStage.ClassicRunning, vm.Stage);
        Assert.False(vm.IsActionEnabled);
        Assert.Contains("经典模式", vm.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Toggle_WhileOperationIsRunning_IgnoresDuplicateClick()
    {
        var coordinator = new FakeCoordinator { HoldStart = true };
        using var vm = new ManagedHomeViewModel(coordinator, () => Task.FromResult<ManagedConfigPayload?>(CreatePayload()));
        await vm.RefreshAsync();

        var first = vm.ToggleConnectionAsync(TestContext.Current.CancellationToken);
        await coordinator.StartEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await vm.ToggleConnectionAsync(TestContext.Current.CancellationToken);
        coordinator.ReleaseStart.SetResult();
        await first;

        Assert.Equal(1, coordinator.StartCalls);
    }

    private static ManagedConfigPayload CreatePayload()
    {
        return new ManagedConfigPayload
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 1,
            SelectionRevision = 1,
            RecommendedProfileId = "p1",
            FallbackProfileIds = ["p2"],
            Profiles =
            [
                new ManagedProfile
                {
                    Id = "p1",
                    DisplayName = "香港智能通道",
                    Available = true,
                    Policy = new ProfilePolicy { AllowSystemProxy = true, AllowTun = true },
                },
                new ManagedProfile
                {
                    Id = "p2",
                    DisplayName = "香港备用通道",
                    Available = true,
                    Policy = new ProfilePolicy { AllowSystemProxy = true, AllowTun = true },
                },
            ],
            ClientPolicy = new ClientPolicy { AllowTun = true, AllowAutomaticFailover = true },
        };
    }

    private sealed class FakeCoordinator : IManagedConnectionCoordinator
    {
        public event Action<ManagedConnectionStatus>? StatusChanged;

        public ManagedConnectionStatus Status { get; private set; } = new();
        public ManagedConnectionStartRequest? LastStartRequest { get; private set; }
        public int StopCalls { get; private set; }
        public int StartCalls { get; private set; }
        public ManagedConnectionFailureKind StartFailure { get; init; }
        public bool UseFallback { get; init; }
        public bool HoldStart { get; init; }
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ManagedConnectionResult> StartAsync(
            ManagedConnectionStartRequest request,
            CancellationToken ct = default)
        {
            StartCalls++;
            LastStartRequest = request;
            Publish(new ManagedConnectionStatus
            {
                State = ManagedConnectionState.Starting,
                NetworkMode = request.NetworkMode,
                SelectionMode = request.SelectionMode,
            });

            if (HoldStart)
            {
                StartEntered.SetResult();
                await ReleaseStart.Task.WaitAsync(ct);
            }

            if (StartFailure != ManagedConnectionFailureKind.None)
            {
                Publish(new ManagedConnectionStatus
                {
                    State = ManagedConnectionState.Faulted,
                    NetworkMode = request.NetworkMode,
                    FailureKind = StartFailure,
                });
                return ManagedConnectionResult.Failed(Status, StartFailure, "failed");
            }

            Publish(new ManagedConnectionStatus
            {
                State = ManagedConnectionState.Connected,
                NetworkMode = request.NetworkMode,
                SelectionMode = request.SelectionMode,
                PreferredProfileId = "p1",
                EffectiveProfileId = UseFallback ? "p2" : "p1",
                IsFallback = UseFallback,
            });
            return ManagedConnectionResult.FromStatus(Status);
        }

        public Task<ManagedConnectionResult> StopAsync(CancellationToken ct = default)
        {
            StopCalls++;
            Publish(new ManagedConnectionStatus { State = ManagedConnectionState.Stopping });
            Publish(new ManagedConnectionStatus { State = ManagedConnectionState.Ready });
            return Task.FromResult(ManagedConnectionResult.FromStatus(Status));
        }

        private void Publish(ManagedConnectionStatus status)
        {
            Status = status;
            StatusChanged?.Invoke(status);
        }
    }
}
