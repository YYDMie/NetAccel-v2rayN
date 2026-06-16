using NetAccel.Managed.Api;
using NetAccel.Managed.Dtos;
using NetAccel.Managed.Services;
using NetAccel.Managed.Storage;

namespace NetAccel.Managed.Tests.Services;

public class AuthServiceTests
{
    [Fact]
    public async Task LoginAsync_StoresTokens_OnSuccess()
    {
        var api = new FakeApiClient();
        var vault = new InMemoryCredentialVault();
        var auth = new AuthService(api, vault);

        api.SetupPost("/api/v1/client/login", new LoginResponse
        {
            AccessToken = "at",
            RefreshToken = "rt",
            ExpiresIn = 3600,
            TokenType = "Bearer"
        });

        var result = await auth.LoginAsync("user", "pass");
        result.Should().BeTrue();
        (await vault.GetAsync("access_token")).Should().Be("at");
        (await vault.GetAsync("refresh_token")).Should().Be("rt");
    }

    [Fact]
    public async Task LoginAsync_ReturnsFalse_OnApiError()
    {
        var api = new FakeApiClient();
        var vault = new InMemoryCredentialVault();
        var auth = new AuthService(api, vault);

        api.SetupPostError("/api/v1/client/login", 401, "invalid_credentials");

        var result = await auth.LoginAsync("user", "wrong");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAsync_UpdatesTokens()
    {
        var api = new FakeApiClient();
        var vault = new InMemoryCredentialVault();
        await vault.SetAsync("refresh_token", "old_rt");
        var auth = new AuthService(api, vault);

        api.SetupPost("/api/v1/client/refresh", new LoginResponse
        {
            AccessToken = "new_at",
            RefreshToken = "new_rt",
            ExpiresIn = 3600,
            TokenType = "Bearer"
        });

        var result = await auth.RefreshAsync();
        result.Should().BeTrue();
        (await vault.GetAsync("access_token")).Should().Be("new_at");
    }

    [Fact]
    public async Task RefreshAsync_ClearsTokens_OnRevoked()
    {
        var api = new FakeApiClient();
        var vault = new InMemoryCredentialVault();
        await vault.SetAsync("refresh_token", "rt");
        await vault.SetAsync("access_token", "at");
        var auth = new AuthService(api, vault);

        api.SetupPostError("/api/v1/client/refresh", 401, "refresh_token_invalid");

        var result = await auth.RefreshAsync();
        result.Should().BeFalse();
        (await vault.GetAsync("access_token")).Should().BeNull();
        (await vault.GetAsync("refresh_token")).Should().BeNull();
    }

    [Fact]
    public async Task LogoutAsync_ClearsTokens()
    {
        var api = new FakeApiClient();
        var vault = new InMemoryCredentialVault();
        await vault.SetAsync("access_token", "at");
        var auth = new AuthService(api, vault);

        await auth.LogoutAsync();
        (await vault.GetAsync("access_token")).Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_ConcurrentCalls_ShareOneRequest()
    {
        var api = new CountingFakeApiClient(delayMs: 100);
        var vault = new InMemoryCredentialVault();
        await vault.SetAsync("refresh_token", "rt");
        var auth = new AuthService(api, vault);

        api.SetupPost("/api/v1/client/refresh", new LoginResponse
        {
            AccessToken = "new_at",
            RefreshToken = "new_rt",
            ExpiresIn = 3600,
            TokenType = "Bearer"
        });

        // Use a Barrier to ensure all callers enter RefreshAsync concurrently
        var barrier = new Barrier(5);
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await auth.RefreshAsync();
            }))
            .ToList();

        var results = await Task.WhenAll(tasks);

        results.All(r => r).Should().BeTrue();
        api.RefreshCallCount.Should().Be(1);
        (await vault.GetAsync("access_token")).Should().Be("new_at");
    }

    [Fact]
    public async Task OnRefreshTokenAsync_WiresToManagedApiClient()
    {
        var api = new FakeApiClient();
        var vault = new InMemoryCredentialVault();
        await vault.SetAsync("refresh_token", "rt");
        var auth = new AuthService(api, vault);

        api.SetupPost("/api/v1/client/refresh", new LoginResponse
        {
            AccessToken = "new_at",
            RefreshToken = "new_rt",
            ExpiresIn = 3600,
            TokenType = "Bearer"
        });

        api.OnRefreshTokenAsync.Should().NotBeNull();
        var result = await api.OnRefreshTokenAsync!();
        result.Should().BeTrue();
        (await vault.GetAsync("access_token")).Should().Be("new_at");
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

    private class CountingFakeApiClient : IManagedApiClient
    {
        private readonly Dictionary<string, object> _responses = new();
        private readonly Dictionary<string, (int, string)> _errors = new();
        private int _refreshCallCount;
        private readonly int _delayMs;

        public string? CurrentAccessToken { get; private set; }
        public string? CurrentInstanceCredential { get; private set; }
        public int RefreshCallCount => _refreshCallCount;

        public CountingFakeApiClient(int delayMs = 0) => _delayMs = delayMs;

        public void SetupPost<T>(string path, T response) => _responses[path] = response!;
        public void SetupPostError(string path, int code, string errorCode) => _errors[path] = (code, errorCode);

        public void SetAccessToken(string? token) => CurrentAccessToken = token;
        public void SetInstanceCredential(string? credential) => CurrentInstanceCredential = credential;

        public async Task<T?> PostAsync<T>(string path, object? body, bool useInstanceCredential = false, bool allowRetry = false, CancellationToken ct = default)
        {
            if (path.Contains("/refresh", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _refreshCallCount);
            }

            if (_delayMs > 0)
            {
                await Task.Delay(_delayMs, ct);
            }

            if (_errors.TryGetValue(path, out var err))
                throw new ManagedApiError(err.Item1, err.Item2, "test");
            if (_responses.TryGetValue(path, out var resp))
                return (T?)resp;
            return default;
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
