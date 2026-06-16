using NetAccel.Managed.Api;
using NetAccel.Managed.Dtos;
using NetAccel.Managed.Services;
using NetAccel.Managed.Storage;

namespace NetAccel.Managed.Tests.Services;

public class StartupCoordinatorTests
{
    [Fact]
    public async Task RunAsync_ReturnsNeedsLogin_WhenNotAuthenticated()
    {
        var auth = new FakeAuthService { Authenticated = false };
        var coord = new StartupCoordinator(auth, new FakeInstanceService(), new FakeSelectionService(), new FakeConfigSync(), new FakeApiClient());
        var result = await coord.RunAsync();
        result.State.Should().Be(StartupState.NeedsLogin);
    }

    [Fact]
    public async Task RunAsync_ReturnsInstanceRevoked_WhenRevoked()
    {
        var auth = new FakeAuthService { Authenticated = true };
        var instance = new FakeInstanceService();
        instance.SetupHeartbeat(new HeartbeatResponse
        {
            Control = new ManagedHeartbeatControl { InstanceRevoked = true, AccountDisabled = false, EmergencyStop = false, MandatoryUpdate = false }
        });
        var coord = new StartupCoordinator(auth, instance, new FakeSelectionService(), new FakeConfigSync(), new FakeApiClient());
        var result = await coord.RunAsync();
        result.State.Should().Be(StartupState.InstanceRevoked);
    }

    [Fact]
    public async Task RunAsync_ReturnsNoAssignment_WhenEmptyProfiles()
    {
        var auth = new FakeAuthService { Authenticated = true };
        var instance = new FakeInstanceService();
        instance.SetupHeartbeat(new HeartbeatResponse
        {
            Control = new ManagedHeartbeatControl { InstanceRevoked = false, AccountDisabled = false, EmergencyStop = false, MandatoryUpdate = false }
        });
        var selection = new FakeSelectionService();
        selection.SetupStatus(new ManagedStatus { Profiles = new() });
        var coord = new StartupCoordinator(auth, instance, selection, new FakeConfigSync(), new FakeApiClient());
        var result = await coord.RunAsync();
        result.State.Should().Be(StartupState.NoAssignment);
    }

    [Fact]
    public async Task RunAsync_ReturnsReady_WhenHealthy()
    {
        var auth = new FakeAuthService { Authenticated = true };
        var instance = new FakeInstanceService();
        instance.SetupHeartbeat(new HeartbeatResponse
        {
            Control = new ManagedHeartbeatControl { InstanceRevoked = false, AccountDisabled = false, EmergencyStop = false, MandatoryUpdate = false }
        });
        var selection = new FakeSelectionService();
        selection.SetupStatus(new ManagedStatus { Profiles = new() { new() { Id = "p1", DisplayName = "Test" } } });
        var coord = new StartupCoordinator(auth, instance, selection, new FakeConfigSync(), new FakeApiClient());
        var result = await coord.RunAsync();
        result.State.Should().Be(StartupState.Ready);
    }

    private class FakeAuthService : IAuthService
    {
        public bool Authenticated { get; set; }
        public event EventHandler<AuthStateChangedEventArgs>? StateChanged;
        public Task<bool> LoginAsync(string username, string password, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> RefreshAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task LogoutAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsAuthenticatedAsync(CancellationToken ct = default) => Task.FromResult(Authenticated);
    }

    private class FakeInstanceService : IInstanceService
    {
        private HeartbeatResponse? _hb;
        public void SetupHeartbeat(HeartbeatResponse hb) => _hb = hb;
        public string? InstanceId => "inst-1";
        public bool IsRegistered => true;
        public Task<bool> EnsureRegisteredAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<InstanceRegisterResponse?> RegisterAsync(CancellationToken ct = default) => Task.FromResult<InstanceRegisterResponse?>(null);
        public Task<bool> RotateCredentialsAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<HeartbeatResponse?> HeartbeatAsync(CancellationToken ct = default) => Task.FromResult(_hb);
    }

    private class FakeSelectionService : IManagedSelectionService
    {
        private ManagedStatus? _status;
        public void SetupStatus(ManagedStatus status) => _status = status;
        public Task<ManagedPolicy?> GetPolicyAsync(CancellationToken ct = default) => Task.FromResult<ManagedPolicy?>(null);
        public Task<ManagedStatus?> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(_status);
        public Task<ManagedSelectionResponse?> SetSelectionAsync(string mode, string? profileId, int expectedRevision, CancellationToken ct = default)
            => Task.FromResult<ManagedSelectionResponse?>(null);
    }

    private class FakeConfigSync : IManagedConfigSyncService
    {
        public Task<ConfigSyncResult?> FetchConfigAsync(int? currentRevision = null, CancellationToken ct = default)
            => Task.FromResult<ConfigSyncResult?>(null);
        public Task<bool> AckConfigAsync(int revision, string stage, string status, string? errorCode = null, string? errorDetail = null, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private class FakeApiClient : IManagedApiClient
    {
        public string? CurrentAccessToken { get; set; }
        public string? CurrentInstanceCredential { get; set; }
        public Func<Task<bool>>? OnRefreshTokenAsync { get; set; }
        public void SetAccessToken(string? token) { }
        public void SetInstanceCredential(string? credential) { }
        public Task<T?> PostAsync<T>(string path, object? body, bool useInstanceCredential = false, bool allowRetry = false, CancellationToken ct = default) => Task.FromResult<T?>(default);
        public Task<T?> GetAsync<T>(string path, bool useInstanceCredential = false, bool allowRetry = true, CancellationToken ct = default) => Task.FromResult<T?>(default);
        public Task<T?> PutAsync<T>(string path, object? body, bool useInstanceCredential = false, bool allowRetry = false, CancellationToken ct = default) => Task.FromResult<T?>(default);
        public Task<T?> PatchAsync<T>(string path, object? body, bool useInstanceCredential = false, bool allowRetry = false, CancellationToken ct = default) => Task.FromResult<T?>(default);
        public Task<T?> DeleteAsync<T>(string path, bool useInstanceCredential = false, bool allowRetry = true, CancellationToken ct = default) => Task.FromResult<T?>(default);
        public Task<ETagResponse<T>> GetWithETagAsync<T>(string path, string? etag, bool useInstanceCredential = false, CancellationToken ct = default) => Task.FromResult(new ETagResponse<T> { Data = default, IsNotModified = false });
    }
}
