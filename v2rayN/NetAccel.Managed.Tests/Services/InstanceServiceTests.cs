using NetAccel.Managed.Api;
using NetAccel.Managed.Dtos;
using NetAccel.Managed.Identity;
using NetAccel.Managed.Services;
using NetAccel.Managed.Storage;

namespace NetAccel.Managed.Tests.Services;

public class InstanceServiceTests
{
    [Fact]
    public async Task RegisterAsync_StoresCredential_AndSetsClient()
    {
        var api = new FakeApiClient();
        var vault = new InMemoryCredentialVault();
        var install = new InstallationIdentityService(vault);
        var svc = new InstanceService(api, vault, install, "1.0.0", new(), new() { "vless-reality" });

        api.SetupPost("/api/v1/client/instances/register", new InstanceRegisterResponse
        {
            Id = "inst-1",
            Credential = "cred-1",
            ExpiresAt = "2026-01-01T00:00:00Z"
        });

        var result = await svc.RegisterAsync();
        result.Should().NotBeNull();
        result!.Id.Should().Be("inst-1");
        (await vault.GetAsync("instance_id")).Should().Be("inst-1");
        (await vault.GetAsync("instance_credential")).Should().Be("cred-1");
        api.CurrentInstanceCredential.Should().Be("cred-1");
    }

    [Fact]
    public async Task HeartbeatAsync_ReturnsControl()
    {
        var api = new FakeApiClient();
        var vault = new InMemoryCredentialVault();
        await vault.SetAsync("instance_id", "inst-1");
        await vault.SetAsync("instance_credential", "cred-1");
        api.SetInstanceCredential("cred-1");

        var install = new InstallationIdentityService(vault);
        var svc = new InstanceService(api, vault, install, "1.0.0", new(), new() { "vless-reality" });

        api.SetupPost("/api/v1/client/instances/inst-1/heartbeat", new HeartbeatResponse
        {
            InstanceStatus = "online",
            Control = new ManagedHeartbeatControl
            {
                DesiredRevision = 42,
                InstanceRevoked = false,
                AccountDisabled = false,
                EmergencyStop = false,
                MandatoryUpdate = false
            }
        });

        var result = await svc.HeartbeatAsync();
        result.Should().NotBeNull();
        result!.Control.DesiredRevision.Should().Be(42);
    }

    private class FakeApiClient : IManagedApiClient
    {
        private readonly Dictionary<string, object> _responses = new();
        private readonly Dictionary<string, (int, string)> _errors = new();
        public string? CurrentAccessToken { get; private set; }
        public string? CurrentInstanceCredential { get; private set; }

        public void SetupPost<T>(string path, T response) => _responses[path] = response!;
        public void SetupPostError(string path, int code, string errorCode) => _errors[path] = (code, errorCode);

        public void SetAccessToken(string? token) => CurrentAccessToken = token;
        public void SetInstanceCredential(string? credential) => CurrentInstanceCredential = credential;

        public Task<T?> PostAsync<T>(string path, object? body, bool useInstanceCredential = false, bool allowRetry = false, CancellationToken ct = default)
        {
            if (_errors.TryGetValue(path, out var err))
                throw new ManagedApiError(err.Item1, err.Item2, "test");
            if (_responses.TryGetValue(path, out var resp))
                return Task.FromResult<T?>((T)resp);
            return Task.FromResult<T?>(default);
        }

        public Task<T?> GetAsync<T>(string path, bool useInstanceCredential = false, bool allowRetry = true, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
        public Task<T?> PutAsync<T>(string path, object? body, bool useInstanceCredential = false, bool allowRetry = false, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
        public Task<T?> PatchAsync<T>(string path, object? body, bool useInstanceCredential = false, bool allowRetry = false, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
        public Task<T?> DeleteAsync<T>(string path, bool useInstanceCredential = false, bool allowRetry = true, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
        public Task<ETagResponse<T>> GetWithETagAsync<T>(string path, string? etag, bool useInstanceCredential = false, CancellationToken ct = default)
            => Task.FromResult(new ETagResponse<T> { Data = default, IsNotModified = false });
        public Func<Task<bool>>? OnRefreshTokenAsync { get; set; }
    }
}
