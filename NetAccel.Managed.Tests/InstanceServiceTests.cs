using System.Net;
using System.Text;
using System.Text.Json;
using NetAccel.Managed.Api;
using NetAccel.Managed.Auth;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Identity;
using NetAccel.Managed.Instance;
using NetAccel.Managed.Vault;
using Xunit;

namespace NetAccel.Managed.Tests;

public class InstanceServiceTests
{
    private static (InstanceService Svc, InMemoryCredentialVault Vault, HttpClient Http) CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? handler = null,
        Action<InMemoryCredentialVault>? seedVault = null)
    {
        var h = handler ?? ((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{\"instance\":{\"id\":\"i1\"},\"instance_credential\":{\"credential\":\"ic1\",\"scope\":[\"instance:heartbeat\"],\"expires_at\":1781308800}}}", Encoding.UTF8, "application/json"),
        }));
        var http = new HttpClient(new FakeHandler(h));
        var api = new ManagedApiClient("https://api.example.com", http);
        var vault = new InMemoryCredentialVault();
        seedVault?.Invoke(vault);
        var auth = new AuthService(api, vault);
        var svc = new InstanceService(api, vault, auth);
        return (svc, vault, http);
    }

    [Fact]
    public async Task Register_Success_StoresCredential()
    {
        var (svc, vault, http) = CreateService();
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");

        var result = await svc.RegisterOrRecoverAsync("ik", "windows", "10", "x64", "1.0", new(), new(), null);
        Assert.True(result.IsSuccess);
        Assert.Equal("ic1", await vault.RetrieveAsync(CredentialVaultEntry.InstanceCredential));
        Assert.Equal("i1", await vault.RetrieveAsync(CredentialVaultEntry.InstanceMetadata));
        http.Dispose();
    }

    [Fact]
    public async Task Register_RebindRequired_ReturnsError()
    {
        var (svc, vault, http) = CreateService((req, ct) =>
        {
            var envelope = new ManagedApiResponse { Code = 403, Message = "rebind", ErrorCode = ManagedErrorCode.ClientInstanceRebindRequired };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");

        var result = await svc.RegisterOrRecoverAsync("ik", "windows", "10", "x64", "1.0", new(), new(), null);
        Assert.Equal(InstanceResultKind.RebindRequired, result.Kind);
        Assert.Equal(ManagedErrorCode.ClientInstanceRebindRequired, result.ErrorCode);
        http.Dispose();
    }

    [Fact]
    public async Task Register_Revoked_ReturnsError()
    {
        var (svc, vault, http) = CreateService((req, ct) =>
        {
            var envelope = new ManagedApiResponse { Code = 403, Message = "revoked", ErrorCode = ManagedErrorCode.ClientInstanceRevoked };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");

        var result = await svc.RegisterOrRecoverAsync("ik", "windows", "10", "x64", "1.0", new(), new(), null);
        Assert.Equal(InstanceResultKind.Revoked, result.Kind);
        http.Dispose();
    }

    [Fact]
    public async Task Heartbeat_Success_ReturnsControl()
    {
        var (svc, vault, http) = CreateService(async (req, ct) =>
        {
            Assert.Equal("/api/v1/client/runtime/instances/i1/heartbeat", req.RequestUri?.AbsolutePath);
            using var body = JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));
            Assert.Equal("1.13.12", body.RootElement.GetProperty("engine_version").GetString());
            Assert.False(body.RootElement.TryGetProperty("core_versions", out _));
            Assert.False(body.RootElement.TryGetProperty("effective_profile_id", out _));
            Assert.False(body.RootElement.TryGetProperty("session_active", out _));
            var data = new HeartbeatResponse
            {
                InstanceStatus = "online",
                Control = new HeartbeatControl
                {
                    DesiredRevision = 42,
                    SelectionRevision = 10,
                    SelectionMode = "automatic",
                    PolicyRevision = 7,
                    InstanceRevoked = false,
                    AccountDisabled = false,
                    EmergencyStop = false,
                    MandatoryUpdate = false,
                    MinClientVersion = "1.0.0",
                    ServerTime = "2026-06-12T14:00:00Z",
                },
            };
            var envelope = new ManagedApiResponse<HeartbeatResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
            };
        });
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");
        await vault.StoreAsync(CredentialVaultEntry.InstanceMetadata, "i1");

        var result = await svc.HeartbeatAsync("1.0", new() { ["sing-box"] = "1.13.12" }, new(), null, false);
        Assert.Equal(HeartbeatResultKind.Success, result.Kind);
        Assert.NotNull(result.Control);
        Assert.Equal(42, result.Control.DesiredRevision);
        http.Dispose();
    }

    [Fact]
    public async Task Heartbeat_Revoked_ClearsInstance()
    {
        var (svc, vault, http) = CreateService((req, ct) =>
        {
            var data = new HeartbeatResponse
            {
                InstanceStatus = "online",
                Control = new HeartbeatControl { InstanceRevoked = true, AccountDisabled = false, EmergencyStop = false, MandatoryUpdate = false, ServerTime = "2026-06-12T14:00:00Z" },
            };
            var envelope = new ManagedApiResponse<HeartbeatResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");
        await vault.StoreAsync(CredentialVaultEntry.InstanceMetadata, "i1");

        var result = await svc.HeartbeatAsync("1.0", new(), new(), null, false);
        Assert.Equal(HeartbeatResultKind.Revoked, result.Kind);
        Assert.Null(await vault.RetrieveAsync(CredentialVaultEntry.InstanceCredential));
        http.Dispose();
    }

    [Fact]
    public async Task Heartbeat_MissingInstance_ReturnsError()
    {
        var (svc, vault, http) = CreateService();
        var result = await svc.HeartbeatAsync("1.0", new(), new(), null, false);
        Assert.Equal(HeartbeatResultKind.UnknownError, result.Kind);
        http.Dispose();
    }

    [Fact]
    public async Task RotateCredential_Success_UpdatesVault()
    {
        var (svc, vault, http) = CreateService((req, ct) =>
        {
            var data = new CredentialRotateResponse
            {
                InstanceCredential = new InstanceCredential { Credential = "ic2", Scope = ["instance:heartbeat"], ExpiresAt = 1781308800 },
            };
            var envelope = new ManagedApiResponse<CredentialRotateResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");
        await vault.StoreAsync(CredentialVaultEntry.InstanceMetadata, "i1");

        var result = await svc.RotateCredentialAsync();
        Assert.True(result.IsSuccess);
        Assert.Equal("ic2", await vault.RetrieveAsync(CredentialVaultEntry.InstanceCredential));
        http.Dispose();
    }

    [Fact]
    public async Task Register_401ThenRefreshThenSuccess()
    {
        var attempt = 0;
        var (svc, vault, http) = CreateService((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{\"access_token\":\"new_at\",\"refresh_token\":\"new_rt\"}}", Encoding.UTF8, "application/json"),
                });
            }
            attempt++;
            if (attempt == 1)
            {
                var envelope = new ManagedApiResponse { Code = 401, Message = "unauthorized", ErrorCode = ManagedErrorCode.InvalidCredentials };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            var data = new ClientInstanceRegisterResponse
            {
                Instance = new RegisteredClientInstance { Id = "i1" },
                InstanceCredential = new InstanceCredential { Credential = "ic1", Scope = ["instance:heartbeat"], ExpiresAt = 1781308800 },
            };
            var envelope2 = new ManagedApiResponse<ClientInstanceRegisterResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope2), Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "old_at");
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");

        var result = await svc.RegisterOrRecoverAsync("ik", "windows", "10", "x64", "1.0", new(), new(), null);
        Assert.Equal(InstanceResultKind.Success, result.Kind);
        Assert.Equal(2, attempt);
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
