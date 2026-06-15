using System.Net;
using System.Text;
using System.Text.Json;
using NetAccel.Managed.Api;
using NetAccel.Managed.Auth;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Vault;
using Xunit;

namespace NetAccel.Managed.Tests;

public class AuthServiceTests
{
    private static (AuthService Auth, InMemoryCredentialVault Vault, ManagedApiClient Api, HttpClient Http) CreateAuthService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? handler = null)
    {
        var h = handler ?? ((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{\"access_token\":\"a\",\"refresh_token\":\"r\"}}", Encoding.UTF8, "application/json"),
        }));
        var http = new HttpClient(new FakeHandler(h));
        var api = new ManagedApiClient("https://api.example.com", http);
        var vault = new InMemoryCredentialVault();
        var auth = new AuthService(api, vault);
        return (auth, vault, api, http);
    }

    [Fact]
    public async Task Login_Success_StoresTokens()
    {
        var (auth, vault, _, http) = CreateAuthService();
        var result = await auth.LoginAsync("user", "pass");

        Assert.True(result.IsSuccess);
        Assert.Equal("a", await vault.RetrieveAsync(CredentialVaultEntry.AccessToken));
        Assert.Equal("r", await vault.RetrieveAsync(CredentialVaultEntry.RefreshToken));
        http.Dispose();
    }

    [Fact]
    public async Task Login_BadCredentials_ReturnsError()
    {
        var (auth, vault, _, http) = CreateAuthService((req, ct) =>
        {
            var envelope = new ManagedApiResponse { Code = 401, Message = "bad", ErrorCode = ManagedErrorCode.InvalidCredentials };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
            });
        });

        var result = await auth.LoginAsync("user", "pass");
        Assert.False(result.IsSuccess);
        Assert.Equal(AuthResultKind.BadCredentials, result.Kind);
        Assert.Equal(ManagedErrorCode.InvalidCredentials, result.ErrorCode);
        http.Dispose();
    }

    [Fact]
    public async Task Login_AccountDisabled_ReturnsError()
    {
        var (auth, _, _, http) = CreateAuthService((req, ct) =>
        {
            var envelope = new ManagedApiResponse { Code = 403, Message = "disabled", ErrorCode = ManagedErrorCode.ClientAccountInactive };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
            });
        });

        var result = await auth.LoginAsync("user", "pass");
        Assert.Equal(AuthResultKind.AccountDisabled, result.Kind);
        http.Dispose();
    }

    [Fact]
    public async Task Refresh_Success_UpdatesTokens()
    {
        var (auth, vault, _, http) = CreateAuthService();
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "old_rt");

        var result = await auth.RefreshAsync();
        Assert.True(result.IsSuccess);
        Assert.Equal("a", await vault.RetrieveAsync(CredentialVaultEntry.AccessToken));
        Assert.Equal("r", await vault.RetrieveAsync(CredentialVaultEntry.RefreshToken));
        http.Dispose();
    }

    [Fact]
    public async Task Refresh_WithoutRefreshToken_ReturnsBadCredentials()
    {
        var (auth, vault, _, http) = CreateAuthService();
        var result = await auth.RefreshAsync();
        Assert.Equal(AuthResultKind.BadCredentials, result.Kind);
        http.Dispose();
    }

    [Fact]
    public async Task Refresh_InvalidToken_ClearsCredentials()
    {
        var (auth, vault, _, http) = CreateAuthService((req, ct) =>
        {
            var envelope = new ManagedApiResponse { Code = 401, Message = "bad", ErrorCode = ManagedErrorCode.RefreshTokenInvalid };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "bad_rt");

        var result = await auth.RefreshAsync();
        Assert.Equal(AuthResultKind.BadCredentials, result.Kind);
        Assert.Null(await vault.RetrieveAsync(CredentialVaultEntry.AccessToken));
        Assert.Null(await vault.RetrieveAsync(CredentialVaultEntry.RefreshToken));
        http.Dispose();
    }

    [Fact]
    public async Task Refresh_ConcurrentCallers_PerformExactlyOneRefresh()
    {
        var callCount = 0;
        var (auth, vault, _, http) = CreateAuthService(async (req, ct) =>
        {
            Interlocked.Increment(ref callCount);
            await Task.Delay(100, ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{\"access_token\":\"a\",\"refresh_token\":\"r\"}}", Encoding.UTF8, "application/json"),
            };
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");

        var tasks = new[] { auth.RefreshAsync(), auth.RefreshAsync(), auth.RefreshAsync() };
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(1, callCount);
        http.Dispose();
    }

    [Fact]
    public async Task Refresh_NetworkTimeout_ReturnsNetworkError()
    {
        var (auth, vault, _, http) = CreateAuthService(async (req, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");

        var api = new ManagedApiClient("https://api.example.com", http, timeout: TimeSpan.FromMilliseconds(50));
        var auth2 = new AuthService(api, vault);
        var result = await auth2.RefreshAsync();
        Assert.Equal(AuthResultKind.NetworkError, result.Kind);
        http.Dispose();
    }

    [Fact]
    public async Task ExecuteWithRefreshAsync_First401_RefreshSucceeds_RetrySucceeds()
    {
        var attempt = 0;
        var (auth, vault, _, http) = CreateAuthService((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{\"access_token\":\"new_at\",\"refresh_token\":\"new_rt\"}}", Encoding.UTF8, "application/json"),
                });
            }
            // operation endpoint
            attempt++;
            if (attempt == 1)
            {
                var envelope = new ManagedApiResponse { Code = 401, Message = "unauthorized", ErrorCode = ManagedErrorCode.InvalidCredentials };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{\"value\":42}}", Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "old_at");

        var result = await auth.ExecuteWithRefreshAsync(async (token, ct) =>
        {
            // Simulate an API call that uses the token
            if (token == "old_at")
                throw new ManagedApiException("401", 401, ManagedErrorCode.InvalidCredentials, "req1");
            return 42;
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        http.Dispose();
    }

    [Fact]
    public async Task ExecuteWithRefreshAsync_ExactlyOneRefreshAndTwoOperationAttempts()
    {
        var refreshCount = 0;
        var opCount = 0;
        var (auth, vault, _, http) = CreateAuthService((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                refreshCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{\"access_token\":\"new_at\",\"refresh_token\":\"new_rt\"}}", Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "old_at");

        await auth.ExecuteWithRefreshAsync<int>(async (token, ct) =>
        {
            opCount++;
            if (opCount == 1)
                throw new ManagedApiException("401", 401, ManagedErrorCode.InvalidCredentials, "req1");
            return opCount;
        });

        Assert.Equal(1, refreshCount);
        Assert.Equal(2, opCount);
        http.Dispose();
    }

    [Fact]
    public async Task ExecuteWithRefreshAsync_Second401_DoesNotLoop()
    {
        var (auth, vault, _, http) = CreateAuthService((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{\"access_token\":\"new_at\",\"refresh_token\":\"new_rt\"}}", Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "old_at");

        var result = await auth.ExecuteWithRefreshAsync<object>(async (token, ct) =>
        {
            throw new ManagedApiException("401", 401, ManagedErrorCode.InvalidCredentials, "req1");
        });

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.OperationException);
        Assert.Equal(401, result.OperationException.HttpStatusCode);
        http.Dispose();
    }

    [Fact]
    public async Task ExecuteWithRefreshAsync_InvalidRefresh_ClearsCredentialsAndDoesNotRetry()
    {
        var opCount = 0;
        var (auth, vault, _, http) = CreateAuthService((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var envelope = new ManagedApiResponse { Code = 401, Message = "bad", ErrorCode = ManagedErrorCode.RefreshTokenInvalid };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            opCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "old_at");

        var result = await auth.ExecuteWithRefreshAsync<int>(async (token, ct) =>
        {
            opCount++;
            throw new ManagedApiException("401", 401, ManagedErrorCode.InvalidCredentials, "req1");
        });

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.RefreshResult);
        Assert.Equal(AuthResultKind.BadCredentials, result.RefreshResult.Kind);
        Assert.Null(await vault.RetrieveAsync(CredentialVaultEntry.AccessToken));
        Assert.Null(await vault.RetrieveAsync(CredentialVaultEntry.RefreshToken));
        Assert.Equal(1, opCount); // one attempt, no retry after failed refresh
        http.Dispose();
    }

    [Fact]
    public async Task ExecuteWithRefreshAsync_StableInstanceScopeError_DoesNotRefresh()
    {
        var refreshCount = 0;
        var (auth, vault, _, http) = CreateAuthService((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                refreshCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{\"access_token\":\"new_at\",\"refresh_token\":\"new_rt\"}}", Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "old_at");

        var result = await auth.ExecuteWithRefreshAsync<object>(async (token, ct) =>
        {
            throw new ManagedApiException("401", 401, ManagedErrorCode.InstanceCredentialScopeDenied, "req1");
        });

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.OperationException);
        Assert.Equal(0, refreshCount);
        http.Dispose();
    }

    [Fact]
    public async Task Logout_CallsServerAndClearsVault()
    {
        var (auth, vault, _, http) = CreateAuthService();
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");

        await auth.LogoutAsync();
        Assert.Null(await vault.RetrieveAsync(CredentialVaultEntry.AccessToken));
        Assert.Null(await vault.RetrieveAsync(CredentialVaultEntry.RefreshToken));
        http.Dispose();
    }

    [Fact]
    public async Task Logout_WithoutToken_DoesNotThrow()
    {
        var (auth, vault, _, http) = CreateAuthService();
        await auth.LogoutAsync();
        Assert.Null(await vault.RetrieveAsync(CredentialVaultEntry.AccessToken));
        http.Dispose();
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
