using NetAccel.Managed.Auth;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Presentation;
using NetAccel.Managed.Startup;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedLoginViewModelTests
{
    [Fact]
    public async Task Initialize_NoCredentials_ShowsLogin()
    {
        var startup = new FakeStartup(ManagedStartupState.NeedsLogin);
        using var vm = new ManagedLoginViewModel(new FakeAuth(), startup);

        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLoginStage.Default, vm.Stage);
        Assert.True(vm.IsLoginFormVisible);
    }

    [Fact]
    public async Task Login_Success_WithConfig_BecomesReady()
    {
        var startup = new FakeStartup(ManagedStartupState.Ready, new ManagedConfigPayload());
        var auth = new FakeAuth(AuthResultKind.Success);
        using var vm = new ManagedLoginViewModel(auth, startup)
        {
            Username = "user",
        };

        await vm.LoginAsync("secret", TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLoginStage.Ready, vm.Stage);
        Assert.True(vm.IsReady);
        Assert.Equal("secret", auth.LastPassword);
    }

    [Fact]
    public async Task Login_BadCredentials_ShowsFriendlyError()
    {
        using var vm = new ManagedLoginViewModel(
            new FakeAuth(AuthResultKind.BadCredentials),
            new FakeStartup(ManagedStartupState.NeedsLogin))
        {
            Username = "user",
        };

        await vm.LoginAsync("bad", TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLoginStage.InvalidCredentials, vm.Stage);
        Assert.True(vm.IsLoginFormVisible);
        Assert.DoesNotContain("bad", vm.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_ReadyWithoutConfig_DoesNotExposeFalseReady()
    {
        using var vm = new ManagedLoginViewModel(
            new FakeAuth(),
            new FakeStartup(ManagedStartupState.Ready));

        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLoginStage.SyncUnavailable, vm.Stage);
        Assert.False(vm.IsReady);
    }

    [Fact]
    public async Task Startup_NoAssignment_ShowsNoAssignment()
    {
        using var vm = new ManagedLoginViewModel(
            new FakeAuth(),
            new FakeStartup(ManagedStartupState.NoAssignment));

        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLoginStage.NoAssignment, vm.Stage);
        Assert.Equal("重新检查", vm.PrimaryActionText);
    }

    [Fact]
    public async Task Startup_Progress_MapsToPreparingAndSyncing()
    {
        var observed = new List<ManagedLoginStage>();
        var startup = new FakeStartup(
            ManagedStartupState.Ready,
            new ManagedConfigPayload(),
            [ManagedStartupPhase.BindingInstance, ManagedStartupPhase.SyncingConfiguration]);
        using var vm = new ManagedLoginViewModel(new FakeAuth(), startup);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Stage))
            {
                observed.Add(vm.Stage);
            }
        };

        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Contains(ManagedLoginStage.PreparingDevice, observed);
        Assert.Contains(ManagedLoginStage.SyncingConfiguration, observed);
        Assert.Equal(ManagedLoginStage.Ready, vm.Stage);
    }

    [Fact]
    public async Task ParallelLogin_IsIgnoredWhileFirstOperationRuns()
    {
        var auth = new FakeAuth(delay: TimeSpan.FromMilliseconds(80));
        using var vm = new ManagedLoginViewModel(auth, new FakeStartup(ManagedStartupState.Ready, new ManagedConfigPayload()))
        {
            Username = "user",
        };

        var first = vm.LoginAsync("one", TestContext.Current.CancellationToken);
        var second = vm.LoginAsync("two", TestContext.Current.CancellationToken);
        await Task.WhenAll(first, second);

        Assert.Equal(1, auth.LoginCalls);
    }

    [Fact]
    public async Task ReturnToLogin_LogsOutAndRestoresForm()
    {
        var auth = new FakeAuth(AuthResultKind.AccountDisabled);
        using var vm = new ManagedLoginViewModel(auth, new FakeStartup(ManagedStartupState.NeedsLogin))
        {
            Username = "user",
        };

        await vm.LoginAsync("secret", TestContext.Current.CancellationToken);
        await vm.ReturnToLoginAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, auth.LogoutCalls);
        Assert.Equal(ManagedLoginStage.Default, vm.Stage);
        Assert.True(vm.IsLoginFormVisible);
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
}
