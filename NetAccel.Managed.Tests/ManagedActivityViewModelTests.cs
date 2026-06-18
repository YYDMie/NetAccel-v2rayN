using NetAccel.Managed.Presentation;
using NetAccel.Managed.Runtime;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedActivityViewModelTests
{
    [Fact]
    public void ConnectedAndStopped_AddFriendlyEvents()
    {
        var coordinator = new FakeCoordinator();
        using var vm = new ManagedActivityViewModel(
            coordinator,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 18, 10, 24, 0, TimeSpan.Zero)));

        coordinator.Publish(new ManagedConnectionStatus { State = ManagedConnectionState.Starting });
        coordinator.Publish(new ManagedConnectionStatus { State = ManagedConnectionState.Connected });
        coordinator.Publish(new ManagedConnectionStatus { State = ManagedConnectionState.Stopping });
        coordinator.Publish(new ManagedConnectionStatus { State = ManagedConnectionState.Ready });

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("已停止加速", vm.Items[0].Title);
        Assert.Equal("已开始加速", vm.Items[1].Title);
        Assert.True(vm.HasItems);
    }

    [Fact]
    public void FallbackConnected_AddsRecoveryEvent()
    {
        var coordinator = new FakeCoordinator();
        using var vm = new ManagedActivityViewModel(coordinator);

        coordinator.Publish(new ManagedConnectionStatus
        {
            State = ManagedConnectionState.Connected,
            IsFallback = true,
        });

        var item = Assert.Single(vm.Items);
        Assert.Equal(ManagedActivityKind.Recovered, item.Kind);
        Assert.Equal("已自动恢复", item.Title);
        Assert.DoesNotContain("profile", item.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConnectedAfterSwitchTransition_AddsRouteSwitchEvent()
    {
        var coordinator = new FakeCoordinator();
        using var vm = new ManagedActivityViewModel(coordinator);

        coordinator.Publish(new ManagedConnectionStatus { State = ManagedConnectionState.Connected });
        coordinator.Publish(new ManagedConnectionStatus { State = ManagedConnectionState.Starting });
        coordinator.Publish(new ManagedConnectionStatus { State = ManagedConnectionState.Connected });

        Assert.Equal("线路已切换", vm.Items[0].Title);
        Assert.Equal("已开始加速", vm.Items[1].Title);
    }

    [Fact]
    public void ConnectedAfterFailedSwitch_RecordsRestoredPreviousRoute()
    {
        var coordinator = new FakeCoordinator();
        using var vm = new ManagedActivityViewModel(coordinator);

        coordinator.Publish(new ManagedConnectionStatus { State = ManagedConnectionState.Connected });
        coordinator.Publish(new ManagedConnectionStatus { State = ManagedConnectionState.Starting });
        coordinator.Publish(new ManagedConnectionStatus
        {
            State = ManagedConnectionState.Connected,
            FailureKind = ManagedConnectionFailureKind.ProfileUnavailable,
            Message = "host=private.example token=secret",
        });

        Assert.Equal("切换未完成", vm.Items[0].Title);
        Assert.Contains("已恢复原线路", vm.Items[0].Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("private.example", vm.Items[0].Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ManagedConnectionFailureKind.OwnerConflict, "经典模式")]
    [InlineData(ManagedConnectionFailureKind.NoAssignment, "可用线路")]
    [InlineData(ManagedConnectionFailureKind.ProfileUnavailable, "暂不可用")]
    [InlineData(ManagedConnectionFailureKind.RestoreFailed, "打开诊断")]
    public void Faulted_MapsToRedactedFriendlyCopy(
        ManagedConnectionFailureKind failure,
        string expected)
    {
        var coordinator = new FakeCoordinator();
        using var vm = new ManagedActivityViewModel(coordinator);

        coordinator.Publish(new ManagedConnectionStatus
        {
            State = ManagedConnectionState.Faulted,
            FailureKind = failure,
            Message = "token=secret host=private.example port=443",
        });

        var item = Assert.Single(vm.Items);
        Assert.Contains(expected, item.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", item.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private.example", item.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("443", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Activity_IsBoundedToFiftyItems()
    {
        var coordinator = new FakeCoordinator();
        using var vm = new ManagedActivityViewModel(coordinator);

        for (var index = 0; index < 60; index++)
        {
            coordinator.Publish(new ManagedConnectionStatus
            {
                State = ManagedConnectionState.Faulted,
                FailureKind = ManagedConnectionFailureKind.Unknown,
            });
        }

        Assert.Equal(50, vm.Items.Count);
    }

    private sealed class FakeCoordinator : IManagedConnectionCoordinator
    {
        public event Action<ManagedConnectionStatus>? StatusChanged;

        public ManagedConnectionStatus Status { get; private set; } = new();

        public void Publish(ManagedConnectionStatus status)
        {
            Status = status;
            StatusChanged?.Invoke(status);
        }

        public Task<ManagedConnectionResult> StartAsync(
            ManagedConnectionStartRequest request,
            CancellationToken ct = default)
            => Task.FromResult(ManagedConnectionResult.FromStatus(Status));

        public Task<ManagedConnectionResult> StopAsync(CancellationToken ct = default)
            => Task.FromResult(ManagedConnectionResult.FromStatus(Status));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
