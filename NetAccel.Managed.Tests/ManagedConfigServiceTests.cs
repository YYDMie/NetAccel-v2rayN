using System.Net;
using System.Text;
using System.Text.Json;
using NetAccel.Managed.Api;
using NetAccel.Managed.Auth;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Vault;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedConfigServiceTests
{
    private static string SuccessAckEnvelope(int revision, string stage)
    {
        var ack = new ManagedConfigAck
        {
            Revision = revision,
            Stage = stage,
            Status = "success",
            AppliedRevision = null,
            ClientVersion = "1.0.0",
            CoreVersions = new(),
            ErrorCode = null,
            ErrorMessage = null,
            ErrorDetail = null,
            ClientAppliedAt = "2026-06-16T00:00:00Z",
        };
        return JsonSerializer.Serialize(new ManagedApiResponse<ManagedConfigAck>
        {
            Code = 200,
            Message = "ok",
            ErrorCode = "",
            Data = ack,
        });
    }

    private static (ManagedConfigService Svc, InMemoryCredentialVault Vault, HttpClient Http) CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? handler = null)
    {
        var h = handler ?? ((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SuccessAckEnvelope(7, "received"), Encoding.UTF8, "application/json"),
        }));
        var http = new HttpClient(new FakeHandler(h));
        var api = new ManagedApiClient("https://api.example.com", http);
        var vault = new InMemoryCredentialVault();
        var auth = new AuthService(api, vault);
        var svc = new ManagedConfigService(api, vault, auth);
        return (svc, vault, http);
    }

    [Fact]
    public async Task AckConfig_SendsCorrectUrlHeadersAndBody()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var (svc, vault, http) = CreateService((req, ct) =>
        {
            captured = req;
            capturedBody = req.Content != null ? req.Content.ReadAsStringAsync().Result : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessAckEnvelope(7, "received"), Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");

        var result = await svc.AckConfigAsync(7, "received", "1.0.0", new Dictionary<string, string> { ["xray"] = "25.1.1" });

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal("POST", captured.Method.Method);
        Assert.EndsWith("/api/v1/client/managed/config/7/ack", captured.RequestUri?.ToString());
        Assert.Equal("Bearer at", captured.Headers.Authorization?.ToString());
        Assert.Contains("ic", captured.Headers.GetValues("X-NetAccel-Instance-Credential"));

        Assert.NotNull(capturedBody);
        var json = JsonSerializer.Deserialize<JsonElement>(capturedBody);
        Assert.Equal("received", json.GetProperty("status").GetString());
        Assert.Equal("1.0.0", json.GetProperty("client_version").GetString());
        Assert.Equal("25.1.1", json.GetProperty("core_versions").GetProperty("xray").GetString());
        http.Dispose();
    }

    [Fact]
    public async Task AckConfig_401ThenRefreshThenSuccess()
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
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessAckEnvelope(7, "received"), Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "old_at");
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");

        var result = await svc.AckConfigAsync(7, "received", "1.0.0", new());
        Assert.True(result.IsSuccess);
        Assert.Equal(2, attempt);
        http.Dispose();
    }

    [Fact]
    public async Task AckConfig_SanitizesErrorDetail()
    {
        string? capturedBody = null;
        var (svc, vault, http) = CreateService((req, ct) =>
        {
            capturedBody = req.Content != null ? req.Content.ReadAsStringAsync().Result : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessAckEnvelope(7, "failed"), Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");

        await svc.AckConfigAsync(7, "failed", "1.0.0", new(), errorCode: "decrypt_failed", errorDetail: "plaintext contains endpoint host 192.168.1.1:443 with token and key");

        Assert.NotNull(capturedBody);
        var json = JsonSerializer.Deserialize<JsonElement>(capturedBody);
        var detail = json.GetProperty("error_detail").GetString() ?? "";
        Assert.DoesNotContain("192.168.1.1", detail);
        Assert.DoesNotContain("443", detail);
        Assert.DoesNotContain("plaintext", detail);
        Assert.DoesNotContain("endpoint", detail);
        http.Dispose();
    }

    [Fact]
    public async Task AckConfig_SanitizesStructuredConfiguration()
    {
        string? capturedBody = null;
        var (svc, vault, http) = CreateService((req, ct) =>
        {
            capturedBody = req.Content != null ? req.Content.ReadAsStringAsync().Result : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessAckEnvelope(7, "failed"), Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");

        await svc.AckConfigAsync(
            7,
            "failed",
            "1.0.0",
            new(),
            errorCode: "validation_failed",
            errorDetail: "{\"outbounds\":[{\"server\":\"secret.example.com\",\"port\":443}]}");

        var json = JsonSerializer.Deserialize<JsonElement>(capturedBody!);
        Assert.Equal("[redacted]", json.GetProperty("error_detail").GetString());
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
