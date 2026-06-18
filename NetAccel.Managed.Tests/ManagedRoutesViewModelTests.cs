using NetAccel.Managed.Dto;
using NetAccel.Managed.Presentation;
using NetAccel.Managed.Runtime;
using NetAccel.Managed.Selection;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedRoutesViewModelTests
{
    [Fact]
    public async Task Refresh_UsesOnlyAssignedStatusProfiles()
    {
        var selection = new FakeSelectionService { Status = CreateStatus() };
        using var vm = CreateViewModel(selection, new FakeCoordinator());

        await vm.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, vm.Routes.Count);
        Assert.Equal(["plan-42", "plan-43"], vm.Routes.Select(route => route.Id));
        Assert.DoesNotContain(vm.Routes, route => route.DisplayName.Contains("example.net", StringComparison.Ordinal));
        Assert.True(vm.CanChangeSelection);
    }

    [Fact]
    public async Task Refresh_DisablesMaintenanceAndIncompatibleRoutes()
    {
        var status = CreateStatus();
        status.Profiles[0].Maintenance = true;
        status.Profiles[1].CapabilityStatus = "incompatible";
        var selection = new FakeSelectionService { Status = status };
        using var vm = CreateViewModel(selection, new FakeCoordinator());

        await vm.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.All(vm.Routes, route => Assert.False(route.IsSelectable));
        Assert.Contains(vm.Routes, route => route.StatusText == "维护中");
        Assert.Contains(vm.Routes, route => route.StatusText == "暂不可用");
    }

    [Fact]
    public async Task Refresh_WhenAssignmentChanged_DisablesSelectionUntilPayloadIsCurrent()
    {
        var status = CreateStatus();
        status.AssignmentRevision++;
        var selection = new FakeSelectionService { Status = status };
        using var vm = CreateViewModel(selection, new FakeCoordinator());

        await vm.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.False(vm.CanChangeSelection);
        Assert.All(vm.Routes, route => Assert.False(route.IsSelectable));
        Assert.Contains("正在更新", vm.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_WhenManualSelectionDenied_DisablesRoutesButKeepsAutomaticSelection()
    {
        var status = CreateStatus();
        var selection = new FakeSelectionService { Status = status };
        var payload = CreatePayload();
        payload.ClientPolicy.AllowManualSelection = false;
        using var vm = new ManagedRoutesViewModel(
            selection,
            new FakeCoordinator(),
            () => Task.FromResult<ManagedConfigPayload?>(payload));

        await vm.RefreshAsync(TestContext.Current.CancellationToken);
        await vm.SelectRouteAsync("plan-43", TestContext.Current.CancellationToken);

        Assert.True(vm.IsSelectionEnabled);
        Assert.All(vm.Routes, route => Assert.False(route.IsSelectable));
        Assert.Null(selection.LastMode);
        Assert.Contains("未开放手动选线", vm.Message, StringComparison.Ordinal);

        await vm.SelectAutomaticAsync(TestContext.Current.CancellationToken);

        Assert.Equal("automatic", selection.LastMode);
    }

    [Fact]
    public async Task SelectRoute_WhenDisconnected_PersistsInstanceSelection()
    {
        var selection = new FakeSelectionService { Status = CreateStatus() };
        using var vm = CreateViewModel(selection, new FakeCoordinator());
        await vm.RefreshAsync(TestContext.Current.CancellationToken);

        await vm.SelectRouteAsync("plan-43", TestContext.Current.CancellationToken);

        Assert.Equal("manual", selection.LastMode);
        Assert.Equal("plan-43", selection.LastProfileId);
        Assert.Equal(10, selection.LastExpectedRevision);
        Assert.False(vm.AutomaticSelected);
        Assert.True(vm.Routes.Single(route => route.Id == "plan-43").IsSelected);
    }

    [Fact]
    public async Task SelectAutomatic_WhenDisconnected_ClearsManualPreference()
    {
        var status = CreateStatus();
        status.SelectionMode = "manual";
        status.SelectedProfileId = "plan-42";
        var selection = new FakeSelectionService { Status = status };
        using var vm = CreateViewModel(selection, new FakeCoordinator());
        await vm.RefreshAsync(TestContext.Current.CancellationToken);

        await vm.SelectAutomaticAsync(TestContext.Current.CancellationToken);

        Assert.Equal("automatic", selection.LastMode);
        Assert.Null(selection.LastProfileId);
        Assert.True(vm.AutomaticSelected);
    }

    [Fact]
    public async Task SelectRoute_WhenConnected_UsesSafeSwitchAndRefreshesStatus()
    {
        var selection = new FakeSelectionService { Status = CreateStatus() };
        var coordinator = new FakeCoordinator(ManagedConnectionState.Connected);
        string? switchedProfile = null;
        var payload = CreatePayload();
        using var vm = new ManagedRoutesViewModel(
            selection,
            coordinator,
            () => Task.FromResult<ManagedConfigPayload?>(payload),
            (requestPayload, mode, profileId, persist, ct) =>
            {
                switchedProfile = profileId;
                Assert.Equal(ManagedProfileSelectionMode.Manual, mode);
                Assert.True(persist);
                Assert.Equal(10, requestPayload.SelectionRevision);
                selection.Status!.SelectionMode = "manual";
                selection.Status.SelectedProfileId = profileId;
                selection.Status.SelectionRevision = 11;
                return Task.FromResult(ManagedConnectionResult.FromStatus(coordinator.Status));
            });
        await vm.RefreshAsync(TestContext.Current.CancellationToken);

        await vm.SelectRouteAsync("plan-43", TestContext.Current.CancellationToken);

        Assert.Equal("plan-43", switchedProfile);
        Assert.True(vm.Routes.Single(route => route.Id == "plan-43").IsSelected);
        Assert.Null(selection.LastMode);
    }

    [Fact]
    public async Task SelectRoute_RejectsUnavailableWithoutCallingServer()
    {
        var status = CreateStatus();
        status.Profiles[1].Available = false;
        var selection = new FakeSelectionService { Status = status };
        using var vm = CreateViewModel(selection, new FakeCoordinator());
        await vm.RefreshAsync(TestContext.Current.CancellationToken);

        await vm.SelectRouteAsync("plan-43", TestContext.Current.CancellationToken);

        Assert.Null(selection.LastMode);
        Assert.Contains("暂时不可用", vm.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_WhenOffline_ShowsCachedRoutesButDisablesSelection()
    {
        var selection = new FakeSelectionService
        {
            StatusKind = PolicyStatusResultKind.NetworkError,
        };
        using var vm = CreateViewModel(selection, new FakeCoordinator());

        await vm.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, vm.Routes.Count);
        Assert.False(vm.CanChangeSelection);
        Assert.All(vm.Routes, route => Assert.False(route.IsSelectable));
        Assert.Contains("离线", vm.Message, StringComparison.Ordinal);
    }

    private static ManagedRoutesViewModel CreateViewModel(
        FakeSelectionService selection,
        FakeCoordinator coordinator)
        => new(selection, coordinator, () => Task.FromResult<ManagedConfigPayload?>(CreatePayload()));

    private static ManagedStatus CreateStatus()
        => new()
        {
            AssignmentRevision = 42,
            SelectionRevision = 10,
            SelectionMode = "automatic",
            RecommendedProfileId = "plan-42",
            EffectiveProfileId = "plan-42",
            Profiles =
            [
                new ProfileSummary
                {
                    Id = "plan-42",
                    DisplayName = "香港智能通道",
                    Region = "HK",
                    Recommended = true,
                    Available = true,
                    CapabilityStatus = "compatible",
                    Quality = new ProfileQuality { LatencyMs = 42 },
                },
                new ProfileSummary
                {
                    Id = "plan-43",
                    DisplayName = "日本低延迟通道",
                    Region = "JP",
                    Available = true,
                    CapabilityStatus = "compatible",
                    Quality = new ProfileQuality { LatencyMs = 68 },
                },
            ],
        };

    private static ManagedConfigPayload CreatePayload()
        => new()
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 42,
            SelectionRevision = 10,
            RecommendedProfileId = "plan-42",
            Profiles =
            [
                new ManagedProfile
                {
                    Id = "plan-42",
                    DisplayName = "香港智能通道",
                    Available = true,
                    Recommended = true,
                    Policy = new ProfilePolicy { AllowSystemProxy = true, AllowTun = true },
                },
                new ManagedProfile
                {
                    Id = "plan-43",
                    DisplayName = "日本低延迟通道",
                    Available = true,
                    Policy = new ProfilePolicy { AllowSystemProxy = true, AllowTun = true },
                },
            ],
            ClientPolicy = new ClientPolicy { AllowTun = true, AllowManualSelection = true },
        };

    private sealed class FakeSelectionService : IManagedSelectionService
    {
        public ManagedStatus? Status { get; set; }
        public PolicyStatusResultKind StatusKind { get; init; } = PolicyStatusResultKind.Success;
        public string? LastMode { get; private set; }
        public string? LastProfileId { get; private set; }
        public int LastExpectedRevision { get; private set; }

        public Task<PolicyStatusResult<ManagedPolicy>> GetPolicyAsync(CancellationToken ct = default)
            => Task.FromResult(new PolicyStatusResult<ManagedPolicy> { Kind = PolicyStatusResultKind.Success, Data = new ManagedPolicy() });

        public Task<PolicyStatusResult<ManagedStatus>> GetStatusAsync(CancellationToken ct = default)
            => Task.FromResult(new PolicyStatusResult<ManagedStatus> { Kind = StatusKind, Data = Status });

        public Task<SelectionResult> UpdateSelectionAsync(
            string mode,
            string? profileId,
            int expectedSelectionRevision,
            CancellationToken ct = default)
        {
            LastMode = mode;
            LastProfileId = profileId;
            LastExpectedRevision = expectedSelectionRevision;
            return Task.FromResult(new SelectionResult
            {
                Kind = SelectionResultKind.Success,
                State = new ManagedSelectionResponse
                {
                    SelectionMode = mode,
                    SelectedProfileId = profileId,
                    SelectionRevision = expectedSelectionRevision + 1,
                },
            });
        }
    }

    private sealed class FakeCoordinator : IManagedConnectionCoordinator
    {
        public FakeCoordinator(ManagedConnectionState state = ManagedConnectionState.Ready)
        {
            Status = new ManagedConnectionStatus { State = state };
        }

        public event Action<ManagedConnectionStatus>? StatusChanged
        {
            add { }
            remove { }
        }
        public ManagedConnectionStatus Status { get; }

        public Task<ManagedConnectionResult> StartAsync(ManagedConnectionStartRequest request, CancellationToken ct = default)
            => Task.FromResult(ManagedConnectionResult.FromStatus(Status));

        public Task<ManagedConnectionResult> StopAsync(CancellationToken ct = default)
            => Task.FromResult(ManagedConnectionResult.FromStatus(Status));
    }
}
