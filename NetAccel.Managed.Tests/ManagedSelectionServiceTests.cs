using System.Net;
using System.Text;
using System.Text.Json;
using NetAccel.Managed.Api;
using NetAccel.Managed.Auth;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Selection;
using NetAccel.Managed.Vault;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedSelectionServiceTests
{
    private static (ManagedSelectionService Svc, InMemoryCredentialVault Vault, HttpClient Http) CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? handler = null)
    {
        var h = handler ?? ((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{}}", Encoding.UTF8, "application/json"),
        }));
        var http = new HttpClient(new FakeHandler(h));
        var api = new ManagedApiClient("https://api.example.com", http);
        var vault = new InMemoryCredentialVault();
        var auth = new AuthService(api, vault);
        var svc = new ManagedSelectionService(api, vault, auth);
        return (svc, vault, http);
    }

    [Fact]
    public async Task GetStatus_Success_ReturnsStatus()
    {
        var status = new ManagedStatus
        {
            DesiredRevision = 42,
            AssignmentRevision = 42,
            SelectionRevision = 10,
            AppliedRevision = 41,
            PolicyRevision = 7,
            SelectionMode = "manual",
            RecommendedProfileId = "plan-42",
            SelectedProfileId = "plan-43",
            EffectiveProfileId = "plan-43",
            FallbackProfileIds = ["plan-42"],
            Profiles =
            [
                new ProfileSummary { Id = "plan-42", DisplayName = "Smart", Recommended = true, Available = true, Maintenance = false, CapabilityStatus = "compatible", Quality = new ProfileQuality { LatencyMs = 42, Stability = "good", SampledAt = "2026-06-12T13:59:30Z" } },
            ],
            InstanceRevoked = false,
            AccountDisabled = false,
            EmergencyStop = false,
            MandatoryUpdate = false,
            MinClientVersion = "1.0.0",
            ServerTime = "2026-06-12T14:00:00Z",
        };
        var envelope = new ManagedApiResponse<ManagedStatus> { Code = 200, Message = "ok", ErrorCode = "", Data = status };
        var (svc, vault, http) = CreateService((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
        }));
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");

        var result = await svc.GetStatusAsync();
        Assert.Equal(PolicyStatusResultKind.Success, result.Kind);
        Assert.Equal("plan-43", result.Data?.EffectiveProfileId);
        http.Dispose();
    }

    [Fact]
    public async Task GetStatus_NoProfiles_ReturnsSuccessButEmpty()
    {
        var status = new ManagedStatus
        {
            DesiredRevision = 0,
            AssignmentRevision = 0,
            SelectionRevision = 0,
            AppliedRevision = null,
            PolicyRevision = 0,
            SelectionMode = "automatic",
            RecommendedProfileId = null,
            SelectedProfileId = null,
            EffectiveProfileId = null,
            FallbackProfileIds = [],
            Profiles = [],
            InstanceRevoked = false,
            AccountDisabled = false,
            EmergencyStop = false,
            MandatoryUpdate = false,
            MinClientVersion = "1.0.0",
            ServerTime = "2026-06-12T14:00:00Z",
        };
        var envelope = new ManagedApiResponse<ManagedStatus> { Code = 200, Message = "ok", ErrorCode = "", Data = status };
        var (svc, vault, http) = CreateService((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
        }));
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");

        var result = await svc.GetStatusAsync();
        Assert.Equal(PolicyStatusResultKind.Success, result.Kind);
        Assert.Empty(result.Data?.Profiles ?? []);
        http.Dispose();
    }

    [Fact]
    public async Task UpdateSelection_Automatic_SendsNullProfileId()
    {
        var response = new ManagedSelectionResponse { AssignmentRevision = 42, SelectionRevision = 11, SelectionMode = "automatic", SelectedProfileId = null, EffectiveProfileId = "plan-42" };
        var envelope = new ManagedApiResponse<ManagedSelectionResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = response };
        HttpRequestMessage? captured = null;
        var (svc, vault, http) = CreateService((req, ct) =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");

        var result = await svc.UpdateSelectionAsync("automatic", null, 10);
        Assert.Equal(SelectionResultKind.Success, result.Kind);
        Assert.Equal("automatic", result.State?.SelectionMode);
        Assert.NotNull(captured);
        http.Dispose();
    }

    [Fact]
    public async Task UpdateSelection_Manual_SendsProfileId()
    {
        var response = new ManagedSelectionResponse { AssignmentRevision = 42, SelectionRevision = 11, SelectionMode = "manual", SelectedProfileId = "plan-43", EffectiveProfileId = "plan-43" };
        var envelope = new ManagedApiResponse<ManagedSelectionResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = response };
        var (svc, vault, http) = CreateService((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
        }));
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");

        var result = await svc.UpdateSelectionAsync("manual", "plan-43", 10);
        Assert.Equal(SelectionResultKind.Success, result.Kind);
        Assert.Equal("plan-43", result.State?.SelectedProfileId);
        http.Dispose();
    }

    [Fact]
    public async Task UpdateSelection_RevisionConflict_ReturnsError()
    {
        var envelope = new ManagedApiResponse { Code = 409, Message = "conflict", ErrorCode = ManagedErrorCode.ManagedSelectionRevisionConflict };
        var (svc, vault, http) = CreateService((req, ct) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)409)
        {
            Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
        }));
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");

        var result = await svc.UpdateSelectionAsync("manual", "plan-43", 9);
        Assert.Equal(SelectionResultKind.RevisionConflict, result.Kind);
        Assert.Equal(ManagedErrorCode.ManagedSelectionRevisionConflict, result.ErrorCode);
        http.Dispose();
    }

    [Fact]
    public async Task UpdateSelection_PlanNotAssigned_ReturnsError()
    {
        var envelope = new ManagedApiResponse { Code = 422, Message = "not assigned", ErrorCode = ManagedErrorCode.ManagedSelectionPlanNotAssigned };
        var (svc, vault, http) = CreateService((req, ct) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)422)
        {
            Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
        }));
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");

        var result = await svc.UpdateSelectionAsync("manual", "plan-99", 10);
        Assert.Equal(SelectionResultKind.PlanNotAssigned, result.Kind);
        http.Dispose();
    }

    [Fact]
    public async Task UpdateSelection_Revoked_ReturnsError()
    {
        var envelope = new ManagedApiResponse { Code = 403, Message = "revoked", ErrorCode = ManagedErrorCode.ClientInstanceRevoked };
        var (svc, vault, http) = CreateService((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
        }));
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");

        var result = await svc.UpdateSelectionAsync("automatic", null, 10);
        Assert.Equal(SelectionResultKind.Revoked, result.Kind);
        http.Dispose();
    }

    [Fact]
    public async Task GetStatus_401ThenRefreshThenSuccess()
    {
        var status = new ManagedStatus
        {
            DesiredRevision = 42,
            AssignmentRevision = 42,
            SelectionRevision = 10,
            AppliedRevision = 41,
            PolicyRevision = 7,
            SelectionMode = "manual",
            Profiles = [],
            InstanceRevoked = false,
            AccountDisabled = false,
            EmergencyStop = false,
            MandatoryUpdate = false,
            MinClientVersion = "1.0.0",
            ServerTime = "2026-06-12T14:00:00Z",
        };
        var statusEnvelope = new ManagedApiResponse<ManagedStatus> { Code = 200, Message = "ok", ErrorCode = "", Data = status };
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
                Content = new StringContent(JsonSerializer.Serialize(statusEnvelope), Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "old_at");
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");

        var result = await svc.GetStatusAsync();
        Assert.Equal(PolicyStatusResultKind.Success, result.Kind);
        Assert.Equal(2, attempt);
        http.Dispose();
    }

    [Fact]
    public async Task UpdateSelection_401ThenRefreshThenSuccess()
    {
        var response = new ManagedSelectionResponse { AssignmentRevision = 42, SelectionRevision = 11, SelectionMode = "automatic", SelectedProfileId = null, EffectiveProfileId = "plan-42" };
        var responseEnvelope = new ManagedApiResponse<ManagedSelectionResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = response };
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
                Content = new StringContent(JsonSerializer.Serialize(responseEnvelope), Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "old_at");
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");

        var result = await svc.UpdateSelectionAsync("automatic", null, 10);
        Assert.Equal(SelectionResultKind.Success, result.Kind);
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
