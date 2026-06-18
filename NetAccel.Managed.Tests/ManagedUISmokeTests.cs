using System.ComponentModel;
using NetAccel.Managed.Auth;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Presentation;
using NetAccel.Managed.Runtime;
using NetAccel.Managed.Selection;
using NetAccel.Managed.Settings;
using NetAccel.Managed.Startup;
using Xunit;

namespace NetAccel.Managed.Tests;

/// <summary>
/// ViewModel-level integration tests that verify the UI state machine behaves correctly.
/// These serve as automation smoke tests for the WPF desktop app without requiring UI automation frameworks.
/// </summary>
public class ManagedUISmokeTests
{
    #region Login Flow Smoke Tests

    [Fact]
    public void LoginFlow_InitialState_ShowsLoginForm()
    {
        var auth = new FakeAuth();
        var startup = new FakeStartup(ManagedStartupState.NeedsLogin);
        using var vm = new ManagedLoginViewModel(auth, startup);

        Assert.Equal(ManagedLoginStage.Default, vm.Stage);
        Assert.True(vm.IsLoginFormVisible);
        Assert.False(vm.IsBusy);
        Assert.Contains("登录", vm.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginFlow_SuccessfulAuth_TransitionsToReady()
    {
        var auth = new FakeAuth(AuthResultKind.Success);
        var startup = new FakeStartup(ManagedStartupState.Ready, new ManagedConfigPayload());
        using var vm = new ManagedLoginViewModel(auth, startup) { Username = "testuser" };

        await vm.LoginAsync("password", TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLoginStage.Ready, vm.Stage);
        Assert.True(vm.IsReady);
        Assert.False(vm.IsLoginFormVisible);
        Assert.Contains("就绪", vm.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginFlow_FailedAuth_ShowsErrorAndAllowsRetry()
    {
        var auth = new FakeAuth(AuthResultKind.BadCredentials);
        var startup = new FakeStartup(ManagedStartupState.NeedsLogin);
        using var vm = new ManagedLoginViewModel(auth, startup) { Username = "testuser" };

        await vm.LoginAsync("wrongpassword", TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLoginStage.InvalidCredentials, vm.Stage);
        Assert.True(vm.IsLoginFormVisible);
        Assert.Contains("不正确", vm.Title, StringComparison.Ordinal);
        Assert.Contains("密码", vm.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginFlow_CancelDuringLogin_ReturnsToLoginForm()
    {
        var auth = new FakeAuth(delay: TimeSpan.FromMilliseconds(500));
        var startup = new FakeStartup(ManagedStartupState.Ready, new ManagedConfigPayload());
        using var vm = new ManagedLoginViewModel(auth, startup) { Username = "testuser" };

        var loginTask = vm.LoginAsync("password", TestContext.Current.CancellationToken);
        vm.Cancel();

        await loginTask;

        Assert.Equal(ManagedLoginStage.Default, vm.Stage);
        Assert.True(vm.IsLoginFormVisible);
    }

    [Fact]
    public async Task LoginFlow_NetworkError_ShowsNetworkErrorState()
    {
        var auth = new FakeAuth(AuthResultKind.NetworkError);
        var startup = new FakeStartup(ManagedStartupState.NeedsLogin);
        using var vm = new ManagedLoginViewModel(auth, startup) { Username = "testuser" };

        await vm.LoginAsync("password", TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLoginStage.NetworkError, vm.Stage);
        Assert.Contains("连接", vm.Title, StringComparison.Ordinal);
        Assert.Contains("网络", vm.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginFlow_AccountDisabled_ShowsDisabledState()
    {
        var auth = new FakeAuth(AuthResultKind.AccountDisabled);
        var startup = new FakeStartup(ManagedStartupState.NeedsLogin);
        using var vm = new ManagedLoginViewModel(auth, startup) { Username = "testuser" };

        await vm.LoginAsync("password", TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLoginStage.AccountDisabled, vm.Stage);
        Assert.Contains("停用", vm.Title, StringComparison.Ordinal);
        Assert.True(vm.IsReturnToLoginVisible);
    }

    #endregion

    #region Navigation Smoke Tests

    [Fact]
    public void Navigation_HomeSection_HasValidViewModel()
    {
        var coordinator = new FakeConnectionCoordinator();
        var configProvider = () => Task.FromResult<ManagedConfigPayload?>(CreatePayload());
        using var vm = new ManagedHomeViewModel(coordinator, configProvider);

        Assert.NotNull(vm.StatusText);
        Assert.NotEmpty(vm.StatusText);
        Assert.NotNull(vm.ActionText);
        Assert.NotEmpty(vm.ActionText);
        Assert.NotNull(vm.Detail);
        Assert.NotEmpty(vm.Detail);
    }

    [Fact]
    public void Navigation_RoutesSection_HasValidViewModel()
    {
        var selection = new FakeSelectionService();
        var coordinator = new FakeConnectionCoordinator();
        var configProvider = () => Task.FromResult<ManagedConfigPayload?>(CreatePayload());
        using var vm = new ManagedRoutesViewModel(selection, coordinator, configProvider);

        Assert.NotNull(vm.Message);
        // IsSelectionEnabled defaults to false before LoadAsync populates CanChangeSelection
        Assert.False(vm.IsSelectionEnabled);
    }

    [Fact]
    public void Navigation_ActivitySection_HasValidViewModel()
    {
        var coordinator = new FakeConnectionCoordinator();
        using var vm = new ManagedActivityViewModel(coordinator);

        Assert.NotNull(vm.Items);
        Assert.False(vm.HasItems);
    }

    [Fact]
    public void Navigation_SettingsSection_HasValidViewModel()
    {
        var preferencesStore = new FakePreferencesStore();
        var vm = new ManagedSettingsViewModel(
            preferencesStore,
            () => false,
            (_, _) => Task.FromResult(true),
            _ => Task.FromResult<int?>(1),
            _ => Task.CompletedTask,
            _ => Task.FromResult(new ClassicModeHandoffResult { Success = true }),
            _ => Task.CompletedTask);

        Assert.NotNull(vm.Summary);
        Assert.NotEmpty(vm.Summary);
        Assert.True(vm.CanAct);
    }

    [Fact]
    public void Navigation_DiagnosticsSection_HasValidViewModel()
    {
        var diagnostics = new FakeDiagnosticsService();
        using var vm = new ManagedDiagnosticsViewModel(diagnostics);

        Assert.NotNull(vm.Summary);
        Assert.NotEmpty(vm.Summary);
        Assert.False(vm.IsBusy);
        Assert.True(vm.CanRepair);
        Assert.True(vm.CanExport);
    }

    #endregion

    #region Theme Smoke Tests

    [Fact]
    public void SettingsViewModel_ThemeNormalization_WorksCorrectly()
    {
        var preferencesStore = new FakePreferencesStore();
        var vm = new ManagedSettingsViewModel(
            preferencesStore,
            () => false,
            (_, _) => Task.FromResult(true),
            _ => Task.FromResult<int?>(1),
            _ => Task.CompletedTask,
            _ => Task.FromResult(new ClassicModeHandoffResult { Success = true }),
            _ => Task.CompletedTask);

        vm.Theme = "LIGHT";
        Assert.Equal("light", vm.Theme);

        vm.Theme = "Dark";
        Assert.Equal("dark", vm.Theme);

        vm.Theme = "invalid";
        Assert.Equal("system", vm.Theme);
    }

    #endregion

    #region Connection State Smoke Tests

    [Fact]
    public void ConnectionState_InitiallyLoading()
    {
        var coordinator = new FakeConnectionCoordinator();
        var configProvider = () => Task.FromResult<ManagedConfigPayload?>(CreatePayload());
        using var vm = new ManagedHomeViewModel(coordinator, configProvider);

        Assert.Equal(ManagedHomeStage.Loading, vm.Stage);
        Assert.False(vm.IsConnected);
        // Loading stage means IsBusy is true until RefreshAsync completes
        Assert.True(vm.IsBusy);
        Assert.Contains("准备", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectionState_ToggleConnection_TransitionsThroughStartingToConnected()
    {
        var coordinator = new FakeConnectionCoordinator();
        var configProvider = () => Task.FromResult<ManagedConfigPayload?>(CreatePayload());
        using var vm = new ManagedHomeViewModel(coordinator, configProvider);

        await vm.RefreshAsync();
        Assert.Equal(ManagedHomeStage.Idle, vm.Stage);

        await vm.ToggleConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedHomeStage.Connected, vm.Stage);
        Assert.True(vm.IsConnected);
        Assert.Contains("加速", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("停止", vm.ActionText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectionState_ErrorState_ShowsDiagnosticInformation()
    {
        var coordinator = new FakeConnectionCoordinator
        {
            StartFailure = ManagedConnectionFailureKind.CoreStartFailed
        };
        var configProvider = () => Task.FromResult<ManagedConfigPayload?>(CreatePayload());
        using var vm = new ManagedHomeViewModel(coordinator, configProvider);

        await vm.RefreshAsync();
        await vm.ToggleConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedHomeStage.Faulted, vm.Stage);
        Assert.Contains("失败", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("诊断", vm.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectionState_OwnerConflict_ShowsClassicRunning()
    {
        var coordinator = new FakeConnectionCoordinator
        {
            StartFailure = ManagedConnectionFailureKind.OwnerConflict
        };
        var configProvider = () => Task.FromResult<ManagedConfigPayload?>(CreatePayload());
        using var vm = new ManagedHomeViewModel(coordinator, configProvider);

        await vm.RefreshAsync();
        await vm.ToggleConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedHomeStage.ClassicRunning, vm.Stage);
        Assert.Contains("经典", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("经典", vm.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectionState_TunPolicyDenied_ShowsNeedsPermission()
    {
        var coordinator = new FakeConnectionCoordinator
        {
            StartFailure = ManagedConnectionFailureKind.PolicyDenied
        };
        var configProvider = () => Task.FromResult<ManagedConfigPayload?>(CreatePayload());
        using var vm = new ManagedHomeViewModel(coordinator, configProvider);

        await vm.RefreshAsync();
        vm.NetworkMode = ManagedConnectionMode.Tun;
        await vm.ToggleConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedHomeStage.NeedsPermission, vm.Stage);
        Assert.Contains("授权", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("授权", vm.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectionState_NoAssignment_ShowsNoAssignmentState()
    {
        var coordinator = new FakeConnectionCoordinator();
        var configProvider = () => Task.FromResult<ManagedConfigPayload?>(new ManagedConfigPayload());
        using var vm = new ManagedHomeViewModel(coordinator, configProvider);

        await vm.RefreshAsync();

        Assert.Equal(ManagedHomeStage.NoAssignment, vm.Stage);
        Assert.Contains("无线路", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("重新检查", vm.ActionText, StringComparison.Ordinal);
    }

    #endregion

    #region Accessibility Property Verification

    [Fact]
    public void Accessibility_HomeViewModel_StatusTextNotEmptyInAllStates()
    {
        var coordinator = new FakeConnectionCoordinator();
        var configProvider = () => Task.FromResult<ManagedConfigPayload?>(CreatePayload());
        using var vm = new ManagedHomeViewModel(coordinator, configProvider);

        // Test all possible stages have non-empty StatusText
        var stages = Enum.GetValues<ManagedHomeStage>();
        foreach (var stage in stages)
        {
            // Trigger state change through coordinator
            coordinator.Publish(new ManagedConnectionStatus
            {
                State = stage switch
                {
                    ManagedHomeStage.Idle => ManagedConnectionState.Ready,
                    ManagedHomeStage.Starting => ManagedConnectionState.Starting,
                    ManagedHomeStage.Connected => ManagedConnectionState.Connected,
                    ManagedHomeStage.Stopping => ManagedConnectionState.Stopping,
                    ManagedHomeStage.Faulted => ManagedConnectionState.Faulted,
                    _ => ManagedConnectionState.Ready
                }
            });

            Assert.NotNull(vm.StatusText);
            Assert.NotEmpty(vm.StatusText);
            Assert.DoesNotContain("null", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Accessibility_LoginViewModel_TitleAndMessageNotEmpty()
    {
        var auth = new FakeAuth(AuthResultKind.BadCredentials);
        var startup = new FakeStartup(ManagedStartupState.NeedsLogin);
        using var vm = new ManagedLoginViewModel(auth, startup) { Username = "testuser" };

        // Test default state
        Assert.NotNull(vm.Title);
        Assert.NotEmpty(vm.Title);
        Assert.NotNull(vm.Message);
        Assert.NotEmpty(vm.Message);

        // Trigger invalid credentials state
        await vm.LoginAsync("wrongpassword", TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLoginStage.InvalidCredentials, vm.Stage);
        Assert.NotNull(vm.Title);
        Assert.NotEmpty(vm.Title);
        Assert.NotNull(vm.Message);
        Assert.NotEmpty(vm.Message);
    }

    [Fact]
    public void Accessibility_SettingsViewModel_SummaryAndAccountTextNotEmpty()
    {
        var preferencesStore = new FakePreferencesStore();
        var vm = new ManagedSettingsViewModel(
            preferencesStore,
            () => false,
            (_, _) => Task.FromResult(true),
            _ => Task.FromResult<int?>(1),
            _ => Task.CompletedTask,
            _ => Task.FromResult(new ClassicModeHandoffResult { Success = true }),
            _ => Task.CompletedTask);

        Assert.NotNull(vm.Summary);
        Assert.NotEmpty(vm.Summary);
        Assert.NotNull(vm.AccountText);
        Assert.NotEmpty(vm.AccountText);
        Assert.NotNull(vm.DeviceText);
        Assert.NotEmpty(vm.DeviceText);
        Assert.NotNull(vm.LastSyncText);
        Assert.NotEmpty(vm.LastSyncText);
    }

    [Fact]
    public void Accessibility_DiagnosticsViewModel_CheckPropertiesNotNull()
    {
        var diagnostics = new FakeDiagnosticsService();
        using var vm = new ManagedDiagnosticsViewModel(diagnostics);

        Assert.NotNull(vm.ServiceConnection);
        Assert.NotNull(vm.ServiceConnection.Title);
        Assert.NotEmpty(vm.ServiceConnection.Title);
        Assert.NotNull(vm.ServiceConnection.Status);
        Assert.NotEmpty(vm.ServiceConnection.Status);
        Assert.NotNull(vm.ServiceConnection.Detail);
        Assert.NotEmpty(vm.ServiceConnection.Detail);

        Assert.NotNull(vm.CurrentRoute);
        Assert.NotNull(vm.LocalNetwork);
        Assert.NotNull(vm.AccelerationEngine);
        Assert.NotNull(vm.Summary);
        Assert.NotEmpty(vm.Summary);
    }

    [Fact]
    public void Accessibility_ActivityViewModel_ItemsPropertyNotNull()
    {
        var coordinator = new FakeConnectionCoordinator();
        using var vm = new ManagedActivityViewModel(coordinator);

        Assert.NotNull(vm.Items);

        // After an event, items should have valid titles
        coordinator.Publish(new ManagedConnectionStatus { State = ManagedConnectionState.Connected });

        Assert.True(vm.HasItems);
        Assert.All(vm.Items, item =>
        {
            Assert.NotNull(item.Title);
            Assert.NotEmpty(item.Title);
        });
    }

    #endregion

    #region Helper Classes and Factories

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

    private sealed class FakeAuth : IAuthService
    {
        private readonly AuthResultKind _loginKind;
        private readonly TimeSpan _delay;

        public FakeAuth(AuthResultKind loginKind = AuthResultKind.Success, TimeSpan? delay = null)
        {
            _loginKind = loginKind;
            _delay = delay ?? TimeSpan.Zero;
        }

        public int LoginCalls { get; private set; }
        public int LogoutCalls { get; private set; }
        public string? LastPassword { get; private set; }

        public async Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default)
        {
            LoginCalls++;
            LastPassword = password;
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, ct);
            }

            return new AuthResult { Kind = _loginKind };
        }

        public Task<AuthResult> RefreshAsync(CancellationToken ct = default)
            => Task.FromResult(new AuthResult { Kind = AuthResultKind.Success });

        public Task LogoutAsync(CancellationToken ct = default)
        {
            LogoutCalls++;
            return Task.CompletedTask;
        }

        public Task<bool> HasCredentialsAsync() => Task.FromResult(true);
        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("token");
        public Task<int?> GetAccountIdAsync() => Task.FromResult<int?>(1);

        public Task<AuthOperationResult<T>> ExecuteWithRefreshAsync<T>(
            Func<string, CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeStartup : IManagedStartupOrchestrator
    {
        private readonly ManagedStartupState _state;
        private readonly ManagedConfigPayload? _config;
        private readonly IReadOnlyList<ManagedStartupPhase> _phases;

        public FakeStartup(
            ManagedStartupState state,
            ManagedConfigPayload? config = null,
            IReadOnlyList<ManagedStartupPhase>? phases = null)
        {
            _state = state;
            _config = config;
            _phases = phases ?? [];
        }

        public event Action<ManagedStartupPhase>? ProgressChanged;

        public Task<ManagedStartupResult> StartupAsync(CancellationToken ct = default)
        {
            foreach (var phase in _phases)
            {
                ProgressChanged?.Invoke(phase);
            }

            return Task.FromResult(new ManagedStartupResult { State = _state });
        }

        public Task<ManagedConfigPayload?> GetCurrentConfigAsync()
            => Task.FromResult(_config);
    }

    private sealed class FakeConnectionCoordinator : IManagedConnectionCoordinator
    {
        public event Action<ManagedConnectionStatus>? StatusChanged;

        public ManagedConnectionStatus Status { get; private set; } = new();
        public ManagedConnectionStartRequest? LastStartRequest { get; private set; }
        public int StopCalls { get; private set; }
        public ManagedConnectionFailureKind StartFailure { get; init; }
        public bool UseFallback { get; init; }

        public async Task<ManagedConnectionResult> StartAsync(
            ManagedConnectionStartRequest request,
            CancellationToken ct = default)
        {
            LastStartRequest = request;
            Publish(new ManagedConnectionStatus
            {
                State = ManagedConnectionState.Starting,
                NetworkMode = request.NetworkMode,
                SelectionMode = request.SelectionMode,
            });

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

        public void Publish(ManagedConnectionStatus status)
        {
            Status = status;
            StatusChanged?.Invoke(status);
        }
    }

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

    private sealed class FakePreferencesStore : IManagedPreferencesStore
    {
        private ManagedPreferences _preferences = new();

        public Task<ManagedPreferences> ReadAsync(CancellationToken ct = default)
            => Task.FromResult(_preferences);

        public Task WriteAsync(ManagedPreferences preferences, CancellationToken ct = default)
        {
            _preferences = preferences;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDiagnosticsService : IManagedDiagnosticsService
    {
        public Task<ManagedDiagnosticsSnapshot> CheckAsync(CancellationToken ct = default)
        {
            var snapshot = new ManagedDiagnosticsSnapshot
            {
                ServiceConnection = new ManagedDiagnosticCheck
                {
                    Title = "服务连接",
                    Status = "正常",
                    Detail = "已连接到管理服务",
                    Condition = ManagedDiagnosticCondition.Normal,
                },
                CurrentRoute = new ManagedDiagnosticCheck
                {
                    Title = "当前线路",
                    Status = "正常",
                    Detail = "香港智能通道",
                    Condition = ManagedDiagnosticCondition.Normal,
                },
                LocalNetwork = new ManagedDiagnosticCheck
                {
                    Title = "本机网络",
                    Status = "正常",
                    Detail = "网络连接正常",
                    Condition = ManagedDiagnosticCondition.Normal,
                },
                AccelerationEngine = new ManagedDiagnosticCheck
                {
                    Title = "加速引擎",
                    Status = "正常",
                    Detail = "引擎运行中",
                    Condition = ManagedDiagnosticCondition.Normal,
                },
            };
            return Task.FromResult(snapshot);
        }

        public Task<ManagedRepairResult> RepairAsync(CancellationToken ct = default)
        {
            var result = new ManagedRepairResult
            {
                Steps = [],
                Snapshot = new ManagedDiagnosticsSnapshot
                {
                    ServiceConnection = new ManagedDiagnosticCheck
                    {
                        Title = "服务连接",
                        Status = "正常",
                        Detail = "已连接到管理服务",
                        Condition = ManagedDiagnosticCondition.Normal,
                    },
                    CurrentRoute = new ManagedDiagnosticCheck
                    {
                        Title = "当前线路",
                        Status = "正常",
                        Detail = "香港智能通道",
                        Condition = ManagedDiagnosticCondition.Normal,
                    },
                    LocalNetwork = new ManagedDiagnosticCheck
                    {
                        Title = "本机网络",
                        Status = "正常",
                        Detail = "网络连接正常",
                        Condition = ManagedDiagnosticCondition.Normal,
                    },
                    AccelerationEngine = new ManagedDiagnosticCheck
                    {
                        Title = "加速引擎",
                        Status = "正常",
                        Detail = "引擎运行中",
                        Condition = ManagedDiagnosticCondition.Normal,
                    },
                },
            };
            return Task.FromResult(result);
        }

        public Task<ManagedDiagnosticsExportResult> ExportAsync(string destinationPath, CancellationToken ct = default)
        {
            var result = new ManagedDiagnosticsExportResult
            {
                Success = true,
                Message = "诊断包已导出",
            };
            return Task.FromResult(result);
        }
    }

    #endregion
}
