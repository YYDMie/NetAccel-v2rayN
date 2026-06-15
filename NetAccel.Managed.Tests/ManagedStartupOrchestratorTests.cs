using System.Net;
using System.Text;
using System.Text.Json;
using NetAccel.Managed.Api;
using NetAccel.Managed.Auth;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Identity;
using NetAccel.Managed.Instance;
using NetAccel.Managed.Selection;
using NetAccel.Managed.Startup;
using NetAccel.Managed.Vault;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedStartupOrchestratorTests
{
    private static (ManagedStartupOrchestrator Orchestrator, InMemoryCredentialVault Vault, HttpClient Http) CreateOrchestrator(
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
        var installation = new InstallationIdentityService(vault);
        var instance = new InstanceService(api, vault, auth);
        var selection = new ManagedSelectionService(api, vault, auth);
        var configService = new ManagedConfigService(api, vault, auth);
        var orch = new ManagedStartupOrchestrator(auth, installation, instance, selection, configService, api, vault, "1.0.0", new(), new());
        return (orch, vault, http);
    }

    [Fact]
    public async Task Startup_NoCredentials_ReturnsNeedsLogin()
    {
        var (orch, _, http) = CreateOrchestrator();
        var result = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.NeedsLogin, result.State);
        http.Dispose();
    }

    [Fact]
    public async Task Startup_RefreshSuccess_InstanceSuccess_ReturnsReady()
    {
        var (orch, vault, http) = CreateOrchestrator((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = "at2", RefreshToken = "rt2" };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = new ClientInstanceRegisterResponse { InstanceId = "i1", InstanceCredential = "ic1" };
                var envelope = new ManagedApiResponse<ClientInstanceRegisterResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/i1/heartbeat")
            {
                var data = new HeartbeatResponse
                {
                    InstanceStatus = "online",
                    Control = new HeartbeatControl { DesiredRevision = 1, AssignmentRevision = 1, SelectionRevision = 0, SelectionMode = "automatic", EffectiveProfileId = null, PolicyRevision = 1, InstanceRevoked = false, AccountDisabled = false, EmergencyStop = false, MandatoryUpdate = false, MinClientVersion = "1.0.0", ServerTime = "2026-06-12T14:00:00Z" },
                };
                var envelope = new ManagedApiResponse<HeartbeatResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/status")
            {
                var data = new ManagedStatus
                {
                    DesiredRevision = 1,
                    AssignmentRevision = 1,
                    SelectionRevision = 0,
                    AppliedRevision = null,
                    PolicyRevision = 1,
                    SelectionMode = "automatic",
                    RecommendedProfileId = "plan-1",
                    SelectedProfileId = null,
                    EffectiveProfileId = "plan-1",
                    FallbackProfileIds = [],
                    Profiles = [new ProfileSummary { Id = "plan-1", DisplayName = "Test", Recommended = true, Available = true, Maintenance = false, CapabilityStatus = "compatible", Quality = new ProfileQuality { LatencyMs = 10, Stability = "good", SampledAt = "2026-06-12T14:00:00Z" } }],
                    InstanceRevoked = false,
                    AccountDisabled = false,
                    EmergencyStop = false,
                    MandatoryUpdate = false,
                    MinClientVersion = "1.0.0",
                    ServerTime = "2026-06-12T14:00:00Z",
                };
                var envelope = new ManagedApiResponse<ManagedStatus> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{}}", Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");

        var result = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.Ready, result.State);
        http.Dispose();
    }

    [Fact]
    public async Task Startup_RefreshAccountDisabled_ReturnsAccountDisabled()
    {
        var (orch, vault, http) = CreateOrchestrator((req, ct) =>
        {
            var envelope = new ManagedApiResponse { Code = 403, Message = "disabled", ErrorCode = ManagedErrorCode.ClientAccountInactive };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");

        var result = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.AccountDisabled, result.State);
        http.Dispose();
    }

    [Fact]
    public async Task Startup_InstanceRevoked_ReturnsInstanceRevoked()
    {
        var (orch, vault, http) = CreateOrchestrator((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = "at2", RefreshToken = "rt2" };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var envelope = new ManagedApiResponse { Code = 403, Message = "revoked", ErrorCode = ManagedErrorCode.ClientInstanceRevoked };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");

        var result = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.InstanceRevoked, result.State);
        http.Dispose();
    }

    [Fact]
    public async Task Startup_NoAssignment_ReturnsNoAssignment()
    {
        var (orch, vault, http) = CreateOrchestrator((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = "at2", RefreshToken = "rt2" };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = new ClientInstanceRegisterResponse { InstanceId = "i1", InstanceCredential = "ic1" };
                var envelope = new ManagedApiResponse<ClientInstanceRegisterResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/i1/heartbeat")
            {
                var data = new HeartbeatResponse
                {
                    InstanceStatus = "online",
                    Control = new HeartbeatControl { DesiredRevision = 0, AssignmentRevision = 0, SelectionRevision = 0, SelectionMode = "automatic", EffectiveProfileId = null, PolicyRevision = 0, InstanceRevoked = false, AccountDisabled = false, EmergencyStop = false, MandatoryUpdate = false, MinClientVersion = "1.0.0", ServerTime = "2026-06-12T14:00:00Z" },
                };
                var envelope = new ManagedApiResponse<HeartbeatResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/status")
            {
                var data = new ManagedStatus
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
                var envelope = new ManagedApiResponse<ManagedStatus> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");

        var result = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.NoAssignment, result.State);
        http.Dispose();
    }

    [Fact]
    public async Task Startup_ConfigFetch_WrappedEnvelope_ReturnsDecryptedPayload()
    {
        var accessToken = "at_test";
        var payload = new ManagedConfigPayload
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 7,
            SelectionRevision = 2,
            RecommendedProfileId = "plan-1",
            FallbackProfileIds = [],
            Profiles = [],
            RoutingPolicy = new { },
            DnsPolicy = new { },
            ClientPolicy = new ClientPolicy { AllowClassicMode = false, AllowTun = true, AllowLocalProxy = false, AllowManualSelection = true, AllowAutomaticFailover = true, OfflineGraceSeconds = 3600 },
        };
        var (nonce, ciphertext) = NetAccel.Managed.Crypto.SpikeV0EnvelopeCrypto.EncryptForTest(
            JsonSerializer.Serialize(payload), accessToken);

        var (orch, vault, http) = CreateOrchestrator((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = accessToken, RefreshToken = "rt2" };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = new ClientInstanceRegisterResponse { InstanceId = "i1", InstanceCredential = "ic1" };
                var envelope = new ManagedApiResponse<ClientInstanceRegisterResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/i1/heartbeat")
            {
                var data = new HeartbeatResponse
                {
                    InstanceStatus = "online",
                    Control = new HeartbeatControl { DesiredRevision = 7, AssignmentRevision = 7, SelectionRevision = 2, SelectionMode = "automatic", EffectiveProfileId = "plan-1", PolicyRevision = 1, InstanceRevoked = false, AccountDisabled = false, EmergencyStop = false, MandatoryUpdate = false, MinClientVersion = "1.0.0", ServerTime = "2026-06-12T14:00:00Z" },
                };
                var envelope = new ManagedApiResponse<HeartbeatResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/status")
            {
                var data = new ManagedStatus
                {
                    DesiredRevision = 7,
                    AssignmentRevision = 7,
                    SelectionRevision = 2,
                    AppliedRevision = null,
                    PolicyRevision = 1,
                    SelectionMode = "automatic",
                    RecommendedProfileId = "plan-1",
                    SelectedProfileId = null,
                    EffectiveProfileId = "plan-1",
                    FallbackProfileIds = [],
                    Profiles = [new ProfileSummary { Id = "plan-1", DisplayName = "Test", Recommended = true, Available = true, Maintenance = false, CapabilityStatus = "compatible", Quality = new ProfileQuality { LatencyMs = 10, Stability = "good", SampledAt = "2026-06-12T14:00:00Z" } }],
                    InstanceRevoked = false,
                    AccountDisabled = false,
                    EmergencyStop = false,
                    MandatoryUpdate = false,
                    MinClientVersion = "1.0.0",
                    ServerTime = "2026-06-12T14:00:00Z",
                };
                var envelope = new ManagedApiResponse<ManagedStatus> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/config")
            {
                var spikeEnvelope = new ManagedEnvelopeSpikeV0
                {
                    Schema = "managed-envelope/spike-v0",
                    PayloadSchema = "managed-config/v1",
                    AssignmentRevision = 7,
                    SelectionRevision = 2,
                    AccountId = 1,
                    InstanceId = "i1",
                    IssuedAt = "2026-06-12T14:00:00Z",
                    ExpiresAt = "2026-06-12T14:15:00Z",
                    Algorithm = "access-token-sha256-aes256gcm",
                    Nonce = nonce,
                    Ciphertext = ciphertext,
                };
                var envelope = new ManagedApiResponse<ManagedEnvelopeSpikeV0> { Code = 200, Message = "ok", ErrorCode = "", Data = spikeEnvelope };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{}}", Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, accessToken);

        var result = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.Ready, result.State);

        var config = await orch.GetCurrentConfigAsync();
        Assert.NotNull(config);
        Assert.Equal(7, config.AssignmentRevision);
        Assert.Equal("managed-config/v1", config.PayloadSchema);
        http.Dispose();
    }

    [Fact]
    public async Task Startup_ConfigFetch_FirstFetch_OmitsIfNoneMatch()
    {
        HttpRequestMessage? captured = null;
        var (orch, vault, http) = CreateOrchestrator((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = "at2", RefreshToken = "rt2" };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = new ClientInstanceRegisterResponse { InstanceId = "i1", InstanceCredential = "ic1" };
                var envelope = new ManagedApiResponse<ClientInstanceRegisterResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/i1/heartbeat")
            {
                var data = new HeartbeatResponse
                {
                    InstanceStatus = "online",
                    Control = new HeartbeatControl { DesiredRevision = 1, AssignmentRevision = 1, SelectionRevision = 0, SelectionMode = "automatic", EffectiveProfileId = null, PolicyRevision = 1, InstanceRevoked = false, AccountDisabled = false, EmergencyStop = false, MandatoryUpdate = false, MinClientVersion = "1.0.0", ServerTime = "2026-06-12T14:00:00Z" },
                };
                var envelope = new ManagedApiResponse<HeartbeatResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/status")
            {
                var data = new ManagedStatus
                {
                    DesiredRevision = 1,
                    AssignmentRevision = 1,
                    SelectionRevision = 0,
                    AppliedRevision = null,
                    PolicyRevision = 1,
                    SelectionMode = "automatic",
                    RecommendedProfileId = "plan-1",
                    SelectedProfileId = null,
                    EffectiveProfileId = "plan-1",
                    FallbackProfileIds = [],
                    Profiles = [new ProfileSummary { Id = "plan-1", DisplayName = "Test", Recommended = true, Available = true, Maintenance = false, CapabilityStatus = "compatible", Quality = new ProfileQuality { LatencyMs = 10, Stability = "good", SampledAt = "2026-06-12T14:00:00Z" } }],
                    InstanceRevoked = false,
                    AccountDisabled = false,
                    EmergencyStop = false,
                    MandatoryUpdate = false,
                    MinClientVersion = "1.0.0",
                    ServerTime = "2026-06-12T14:00:00Z",
                };
                var envelope = new ManagedApiResponse<ManagedStatus> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/config")
            {
                captured = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{}}", Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");

        await orch.StartupAsync();
        Assert.NotNull(captured);
        Assert.Empty(captured.Headers.IfNoneMatch);
        http.Dispose();
    }

    [Fact]
    public async Task Startup_ConfigFetch_SecondFetch_SendsIfNoneMatchWithAssignmentRevision()
    {
        HttpRequestMessage? captured = null;
        var accessToken = "at_test";
        var payload = new ManagedConfigPayload
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 7,
            SelectionRevision = 2,
            RecommendedProfileId = "plan-1",
            FallbackProfileIds = [],
            Profiles = [],
            RoutingPolicy = new { },
            DnsPolicy = new { },
            ClientPolicy = new ClientPolicy { AllowClassicMode = false, AllowTun = true, AllowLocalProxy = false, AllowManualSelection = true, AllowAutomaticFailover = true, OfflineGraceSeconds = 3600 },
        };
        var (nonce, ciphertext) = NetAccel.Managed.Crypto.SpikeV0EnvelopeCrypto.EncryptForTest(
            JsonSerializer.Serialize(payload), accessToken);

        var (orch, vault, http) = CreateOrchestrator((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = accessToken, RefreshToken = "rt2" };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = new ClientInstanceRegisterResponse { InstanceId = "i1", InstanceCredential = "ic1" };
                var envelope = new ManagedApiResponse<ClientInstanceRegisterResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/i1/heartbeat")
            {
                var data = new HeartbeatResponse
                {
                    InstanceStatus = "online",
                    Control = new HeartbeatControl { DesiredRevision = 7, AssignmentRevision = 7, SelectionRevision = 2, SelectionMode = "automatic", EffectiveProfileId = "plan-1", PolicyRevision = 1, InstanceRevoked = false, AccountDisabled = false, EmergencyStop = false, MandatoryUpdate = false, MinClientVersion = "1.0.0", ServerTime = "2026-06-12T14:00:00Z" },
                };
                var envelope = new ManagedApiResponse<HeartbeatResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/status")
            {
                var data = new ManagedStatus
                {
                    DesiredRevision = 7,
                    AssignmentRevision = 7,
                    SelectionRevision = 2,
                    AppliedRevision = null,
                    PolicyRevision = 1,
                    SelectionMode = "automatic",
                    RecommendedProfileId = "plan-1",
                    SelectedProfileId = null,
                    EffectiveProfileId = "plan-1",
                    FallbackProfileIds = [],
                    Profiles = [new ProfileSummary { Id = "plan-1", DisplayName = "Test", Recommended = true, Available = true, Maintenance = false, CapabilityStatus = "compatible", Quality = new ProfileQuality { LatencyMs = 10, Stability = "good", SampledAt = "2026-06-12T14:00:00Z" } }],
                    InstanceRevoked = false,
                    AccountDisabled = false,
                    EmergencyStop = false,
                    MandatoryUpdate = false,
                    MinClientVersion = "1.0.0",
                    ServerTime = "2026-06-12T14:00:00Z",
                };
                var envelope = new ManagedApiResponse<ManagedStatus> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/config")
            {
                captured = req;
                var spikeEnvelope = new ManagedEnvelopeSpikeV0
                {
                    Schema = "managed-envelope/spike-v0",
                    PayloadSchema = "managed-config/v1",
                    AssignmentRevision = 7,
                    SelectionRevision = 2,
                    AccountId = 1,
                    InstanceId = "i1",
                    IssuedAt = "2026-06-12T14:00:00Z",
                    ExpiresAt = "2026-06-12T14:15:00Z",
                    Algorithm = "access-token-sha256-aes256gcm",
                    Nonce = nonce,
                    Ciphertext = ciphertext,
                };
                var envelope = new ManagedApiResponse<ManagedEnvelopeSpikeV0> { Code = 200, Message = "ok", ErrorCode = "", Data = spikeEnvelope };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{}}", Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, accessToken);

        var result1 = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.Ready, result1.State);

        // Second startup with current config should send If-None-Match
        captured = null;
        var result2 = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.Ready, result2.State);
        Assert.NotNull(captured);
        var etag = captured.Headers.IfNoneMatch.FirstOrDefault();
        Assert.NotNull(etag);
        Assert.Equal("\"7\"", etag.Tag);
        http.Dispose();
    }

    [Fact]
    public async Task Startup_ConfigFetch_304_KeepsExistingInMemoryConfig()
    {
        var accessToken = "at_test";
        var payload = new ManagedConfigPayload
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 7,
            SelectionRevision = 2,
            RecommendedProfileId = "plan-1",
            FallbackProfileIds = [],
            Profiles = [],
            RoutingPolicy = new { },
            DnsPolicy = new { },
            ClientPolicy = new ClientPolicy { AllowClassicMode = false, AllowTun = true, AllowLocalProxy = false, AllowManualSelection = true, AllowAutomaticFailover = true, OfflineGraceSeconds = 3600 },
        };
        var (nonce, ciphertext) = NetAccel.Managed.Crypto.SpikeV0EnvelopeCrypto.EncryptForTest(
            JsonSerializer.Serialize(payload), accessToken);

        var configCallCount = 0;
        var (orch, vault, http) = CreateOrchestrator((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = accessToken, RefreshToken = "rt2" };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = new ClientInstanceRegisterResponse { InstanceId = "i1", InstanceCredential = "ic1" };
                var envelope = new ManagedApiResponse<ClientInstanceRegisterResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/i1/heartbeat")
            {
                var data = new HeartbeatResponse
                {
                    InstanceStatus = "online",
                    Control = new HeartbeatControl { DesiredRevision = 7, AssignmentRevision = 7, SelectionRevision = 2, SelectionMode = "automatic", EffectiveProfileId = "plan-1", PolicyRevision = 1, InstanceRevoked = false, AccountDisabled = false, EmergencyStop = false, MandatoryUpdate = false, MinClientVersion = "1.0.0", ServerTime = "2026-06-12T14:00:00Z" },
                };
                var envelope = new ManagedApiResponse<HeartbeatResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/status")
            {
                var data = new ManagedStatus
                {
                    DesiredRevision = 7,
                    AssignmentRevision = 7,
                    SelectionRevision = 2,
                    AppliedRevision = null,
                    PolicyRevision = 1,
                    SelectionMode = "automatic",
                    RecommendedProfileId = "plan-1",
                    SelectedProfileId = null,
                    EffectiveProfileId = "plan-1",
                    FallbackProfileIds = [],
                    Profiles = [new ProfileSummary { Id = "plan-1", DisplayName = "Test", Recommended = true, Available = true, Maintenance = false, CapabilityStatus = "compatible", Quality = new ProfileQuality { LatencyMs = 10, Stability = "good", SampledAt = "2026-06-12T14:00:00Z" } }],
                    InstanceRevoked = false,
                    AccountDisabled = false,
                    EmergencyStop = false,
                    MandatoryUpdate = false,
                    MinClientVersion = "1.0.0",
                    ServerTime = "2026-06-12T14:00:00Z",
                };
                var envelope = new ManagedApiResponse<ManagedStatus> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/config")
            {
                configCallCount++;
                if (configCallCount == 1)
                {
                    var spikeEnvelope = new ManagedEnvelopeSpikeV0
                    {
                        Schema = "managed-envelope/spike-v0",
                        PayloadSchema = "managed-config/v1",
                        AssignmentRevision = 7,
                        SelectionRevision = 2,
                        AccountId = 1,
                        InstanceId = "i1",
                        IssuedAt = "2026-06-12T14:00:00Z",
                        ExpiresAt = "2026-06-12T14:15:00Z",
                        Algorithm = "access-token-sha256-aes256gcm",
                        Nonce = nonce,
                        Ciphertext = ciphertext,
                    };
                    var envelope = new ManagedApiResponse<ManagedEnvelopeSpikeV0> { Code = 200, Message = "ok", ErrorCode = "", Data = spikeEnvelope };
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                    });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{}}", Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, accessToken);

        var result1 = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.Ready, result1.State);
        var config1 = await orch.GetCurrentConfigAsync();
        Assert.NotNull(config1);
        Assert.Equal(7, config1.AssignmentRevision);

        // Second startup on same orchestrator returns 304
        var result2 = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.Ready, result2.State);
        var config2 = await orch.GetCurrentConfigAsync();
        Assert.NotNull(config2);
        Assert.Equal(7, config2.AssignmentRevision);
        Assert.Equal(2, configCallCount);
        http.Dispose();
    }

    [Fact]
    public async Task Startup_ConfigFetch_401RefreshesOnceAndRetriesOnce()
    {
        const string firstAccessToken = "at_first";
        const string refreshedAccessToken = "at_refreshed";
        var payload = new ManagedConfigPayload
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 7,
            SelectionRevision = 2,
            RecommendedProfileId = "plan-1",
            FallbackProfileIds = [],
            Profiles = [],
            RoutingPolicy = new { },
            DnsPolicy = new { },
            ClientPolicy = new ClientPolicy
            {
                AllowClassicMode = false,
                AllowTun = true,
                AllowLocalProxy = false,
                AllowManualSelection = true,
                AllowAutomaticFailover = true,
                OfflineGraceSeconds = 3600,
            },
        };
        var (nonce, ciphertext) = NetAccel.Managed.Crypto.SpikeV0EnvelopeCrypto.EncryptForTest(
            JsonSerializer.Serialize(payload), refreshedAccessToken);

        var refreshCalls = 0;
        var configCalls = 0;
        var (orch, vault, http) = CreateOrchestrator((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                refreshCalls++;
                var token = refreshCalls == 1 ? firstAccessToken : refreshedAccessToken;
                var data = new RefreshResponse { AccessToken = token, RefreshToken = $"rt{refreshCalls + 1}" };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data }),
                        Encoding.UTF8,
                        "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = new ClientInstanceRegisterResponse { InstanceId = "i1", InstanceCredential = "ic1" };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new ManagedApiResponse<ClientInstanceRegisterResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data }),
                        Encoding.UTF8,
                        "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/i1/heartbeat")
            {
                var data = new HeartbeatResponse
                {
                    InstanceStatus = "online",
                    Control = new HeartbeatControl
                    {
                        DesiredRevision = 7,
                        AssignmentRevision = 7,
                        SelectionRevision = 2,
                        SelectionMode = "automatic",
                        EffectiveProfileId = "plan-1",
                        PolicyRevision = 1,
                        MinClientVersion = "1.0.0",
                        ServerTime = "2026-06-16T00:00:00Z",
                    },
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new ManagedApiResponse<HeartbeatResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data }),
                        Encoding.UTF8,
                        "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/status")
            {
                var data = new ManagedStatus
                {
                    DesiredRevision = 7,
                    AssignmentRevision = 7,
                    SelectionRevision = 2,
                    PolicyRevision = 1,
                    SelectionMode = "automatic",
                    RecommendedProfileId = "plan-1",
                    EffectiveProfileId = "plan-1",
                    FallbackProfileIds = [],
                    Profiles =
                    [
                        new ProfileSummary
                        {
                            Id = "plan-1",
                            DisplayName = "Test",
                            Recommended = true,
                            Available = true,
                            CapabilityStatus = "compatible",
                            Quality = new ProfileQuality { Stability = "good" },
                        },
                    ],
                    MinClientVersion = "1.0.0",
                    ServerTime = "2026-06-16T00:00:00Z",
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new ManagedApiResponse<ManagedStatus> { Code = 200, Message = "ok", ErrorCode = "", Data = data }),
                        Encoding.UTF8,
                        "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/config")
            {
                configCalls++;
                if (configCalls == 1)
                {
                    var error = new ManagedApiResponse
                    {
                        Code = 401,
                        Message = "expired",
                        ErrorCode = ManagedErrorCode.InvalidCredentials,
                    };
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(error), Encoding.UTF8, "application/json"),
                    });
                }

                Assert.Equal($"Bearer {refreshedAccessToken}", req.Headers.Authorization?.ToString());
                var spikeEnvelope = new ManagedEnvelopeSpikeV0
                {
                    Schema = "managed-envelope/spike-v0",
                    PayloadSchema = "managed-config/v1",
                    AssignmentRevision = 7,
                    SelectionRevision = 2,
                    AccountId = 1,
                    InstanceId = "i1",
                    IssuedAt = "2026-06-16T00:00:00Z",
                    ExpiresAt = "2026-06-16T00:15:00Z",
                    Algorithm = "access-token-sha256-aes256gcm",
                    Nonce = nonce,
                    Ciphertext = ciphertext,
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new ManagedApiResponse<ManagedEnvelopeSpikeV0> { Code = 200, Message = "ok", ErrorCode = "", Data = spikeEnvelope }),
                        Encoding.UTF8,
                        "application/json"),
                });
            }
            if (path.StartsWith("/api/v1/client/managed/config/") && path.EndsWith("/ack"))
            {
                var ack = new ManagedConfigAck
                {
                    Revision = 7,
                    Stage = "validated",
                    Status = "success",
                    ClientVersion = "1.0.0",
                    CoreVersions = new(),
                    ClientAppliedAt = "2026-06-16T00:00:00Z",
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new ManagedApiResponse<ManagedConfigAck> { Code = 200, Message = "ok", ErrorCode = "", Data = ack }),
                        Encoding.UTF8,
                        "application/json"),
                });
            }

            throw new InvalidOperationException($"Unexpected request: {path}");
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt1");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "stale");

        var result = await orch.StartupAsync();

        Assert.Equal(ManagedStartupState.Ready, result.State);
        Assert.Equal(2, refreshCalls);
        Assert.Equal(2, configCalls);
        Assert.Equal(7, (await orch.GetCurrentConfigAsync())?.AssignmentRevision);
        http.Dispose();
    }

    [Fact]
    public async Task Startup_ConfigAck_SuccessfulStartup_EmitsReceivedThenValidated_NeverApplied()
    {
        var accessToken = "at_test";
        var payload = new ManagedConfigPayload
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 7,
            SelectionRevision = 2,
            RecommendedProfileId = "plan-1",
            FallbackProfileIds = [],
            Profiles = [],
            RoutingPolicy = new { },
            DnsPolicy = new { },
            ClientPolicy = new ClientPolicy { AllowClassicMode = false, AllowTun = true, AllowLocalProxy = false, AllowManualSelection = true, AllowAutomaticFailover = true, OfflineGraceSeconds = 3600 },
        };
        var (nonce, ciphertext) = NetAccel.Managed.Crypto.SpikeV0EnvelopeCrypto.EncryptForTest(
            JsonSerializer.Serialize(payload), accessToken);

        var ackStatuses = new List<string>();
        var (orch, vault, http) = CreateOrchestrator((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = accessToken, RefreshToken = "rt2" };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = new ClientInstanceRegisterResponse { InstanceId = "i1", InstanceCredential = "ic1" };
                var envelope = new ManagedApiResponse<ClientInstanceRegisterResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/i1/heartbeat")
            {
                var data = new HeartbeatResponse
                {
                    InstanceStatus = "online",
                    Control = new HeartbeatControl { DesiredRevision = 7, AssignmentRevision = 7, SelectionRevision = 2, SelectionMode = "automatic", EffectiveProfileId = "plan-1", PolicyRevision = 1, InstanceRevoked = false, AccountDisabled = false, EmergencyStop = false, MandatoryUpdate = false, MinClientVersion = "1.0.0", ServerTime = "2026-06-12T14:00:00Z" },
                };
                var envelope = new ManagedApiResponse<HeartbeatResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/status")
            {
                var data = new ManagedStatus
                {
                    DesiredRevision = 7,
                    AssignmentRevision = 7,
                    SelectionRevision = 2,
                    AppliedRevision = null,
                    PolicyRevision = 1,
                    SelectionMode = "automatic",
                    RecommendedProfileId = "plan-1",
                    SelectedProfileId = null,
                    EffectiveProfileId = "plan-1",
                    FallbackProfileIds = [],
                    Profiles = [new ProfileSummary { Id = "plan-1", DisplayName = "Test", Recommended = true, Available = true, Maintenance = false, CapabilityStatus = "compatible", Quality = new ProfileQuality { LatencyMs = 10, Stability = "good", SampledAt = "2026-06-12T14:00:00Z" } }],
                    InstanceRevoked = false,
                    AccountDisabled = false,
                    EmergencyStop = false,
                    MandatoryUpdate = false,
                    MinClientVersion = "1.0.0",
                    ServerTime = "2026-06-12T14:00:00Z",
                };
                var envelope = new ManagedApiResponse<ManagedStatus> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/config")
            {
                var spikeEnvelope = new ManagedEnvelopeSpikeV0
                {
                    Schema = "managed-envelope/spike-v0",
                    PayloadSchema = "managed-config/v1",
                    AssignmentRevision = 7,
                    SelectionRevision = 2,
                    AccountId = 1,
                    InstanceId = "i1",
                    IssuedAt = "2026-06-12T14:00:00Z",
                    ExpiresAt = "2026-06-12T14:15:00Z",
                    Algorithm = "access-token-sha256-aes256gcm",
                    Nonce = nonce,
                    Ciphertext = ciphertext,
                };
                var envelope = new ManagedApiResponse<ManagedEnvelopeSpikeV0> { Code = 200, Message = "ok", ErrorCode = "", Data = spikeEnvelope };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path.StartsWith("/api/v1/client/managed/config/") && path.EndsWith("/ack"))
            {
                var body = req.Content != null ? req.Content.ReadAsStringAsync().Result : "";
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                ackStatuses.Add(json.GetProperty("status").GetString() ?? "");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{\"acknowledged\":true}}", Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{}}", Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, accessToken);

        var result = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.Ready, result.State);
        Assert.Contains("received", ackStatuses);
        Assert.Contains("validated", ackStatuses);
        Assert.DoesNotContain("applied", ackStatuses);
        http.Dispose();
    }

    [Fact]
    public async Task Startup_ConfigAck_DecryptFailure_EmitsSanitizedFailed()
    {
        var accessToken = "at_test";
        var ackStatuses = new List<(string Status, string? Detail)>();
        var (orch, vault, http) = CreateOrchestrator((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = accessToken, RefreshToken = "rt2" };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = new ClientInstanceRegisterResponse { InstanceId = "i1", InstanceCredential = "ic1" };
                var envelope = new ManagedApiResponse<ClientInstanceRegisterResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/i1/heartbeat")
            {
                var data = new HeartbeatResponse
                {
                    InstanceStatus = "online",
                    Control = new HeartbeatControl { DesiredRevision = 7, AssignmentRevision = 7, SelectionRevision = 2, SelectionMode = "automatic", EffectiveProfileId = "plan-1", PolicyRevision = 1, InstanceRevoked = false, AccountDisabled = false, EmergencyStop = false, MandatoryUpdate = false, MinClientVersion = "1.0.0", ServerTime = "2026-06-12T14:00:00Z" },
                };
                var envelope = new ManagedApiResponse<HeartbeatResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/status")
            {
                var data = new ManagedStatus
                {
                    DesiredRevision = 7,
                    AssignmentRevision = 7,
                    SelectionRevision = 2,
                    AppliedRevision = null,
                    PolicyRevision = 1,
                    SelectionMode = "automatic",
                    RecommendedProfileId = "plan-1",
                    SelectedProfileId = null,
                    EffectiveProfileId = "plan-1",
                    FallbackProfileIds = [],
                    Profiles = [new ProfileSummary { Id = "plan-1", DisplayName = "Test", Recommended = true, Available = true, Maintenance = false, CapabilityStatus = "compatible", Quality = new ProfileQuality { LatencyMs = 10, Stability = "good", SampledAt = "2026-06-12T14:00:00Z" } }],
                    InstanceRevoked = false,
                    AccountDisabled = false,
                    EmergencyStop = false,
                    MandatoryUpdate = false,
                    MinClientVersion = "1.0.0",
                    ServerTime = "2026-06-12T14:00:00Z",
                };
                var envelope = new ManagedApiResponse<ManagedStatus> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/config")
            {
                // Return a valid envelope but with tampered ciphertext so decrypt fails
                var spikeEnvelope = new ManagedEnvelopeSpikeV0
                {
                    Schema = "managed-envelope/spike-v0",
                    PayloadSchema = "managed-config/v1",
                    AssignmentRevision = 7,
                    SelectionRevision = 2,
                    AccountId = 1,
                    InstanceId = "i1",
                    IssuedAt = "2026-06-12T14:00:00Z",
                    ExpiresAt = "2026-06-12T14:15:00Z",
                    Algorithm = "access-token-sha256-aes256gcm",
                    Nonce = "badnonce",
                    Ciphertext = "badciphertext",
                };
                var envelope = new ManagedApiResponse<ManagedEnvelopeSpikeV0> { Code = 200, Message = "ok", ErrorCode = "", Data = spikeEnvelope };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path.StartsWith("/api/v1/client/managed/config/") && path.EndsWith("/ack"))
            {
                var body = req.Content != null ? req.Content.ReadAsStringAsync().Result : "";
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                ackStatuses.Add((json.GetProperty("status").GetString() ?? "", json.TryGetProperty("error_detail", out var d) ? d.GetString() : null));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{\"acknowledged\":true}}", Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{}}", Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, accessToken);

        var result = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.Ready, result.State);
        var config = await orch.GetCurrentConfigAsync();
        Assert.Null(config);

        var received = ackStatuses.FirstOrDefault(a => a.Status == "received");
        Assert.NotNull(received.Status);

        var failed = ackStatuses.FirstOrDefault(a => a.Status == "failed");
        Assert.Equal("failed", failed.Status);
        Assert.NotNull(failed.Detail);
        http.Dispose();
    }

    [Fact]
    public async Task Startup_ConfigAck_Failure_DoesNotPersistPlaintextOrStartRuntime()
    {
        var accessToken = "at_test";
        var payload = new ManagedConfigPayload
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 7,
            SelectionRevision = 2,
            RecommendedProfileId = "plan-1",
            FallbackProfileIds = [],
            Profiles = [],
            RoutingPolicy = new { },
            DnsPolicy = new { },
            ClientPolicy = new ClientPolicy { AllowClassicMode = false, AllowTun = true, AllowLocalProxy = false, AllowManualSelection = true, AllowAutomaticFailover = true, OfflineGraceSeconds = 3600 },
        };
        var (nonce, ciphertext) = NetAccel.Managed.Crypto.SpikeV0EnvelopeCrypto.EncryptForTest(
            JsonSerializer.Serialize(payload), accessToken);

        var (orch, vault, http) = CreateOrchestrator((req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = accessToken, RefreshToken = "rt2" };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = new ClientInstanceRegisterResponse { InstanceId = "i1", InstanceCredential = "ic1" };
                var envelope = new ManagedApiResponse<ClientInstanceRegisterResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/i1/heartbeat")
            {
                var data = new HeartbeatResponse
                {
                    InstanceStatus = "online",
                    Control = new HeartbeatControl { DesiredRevision = 7, AssignmentRevision = 7, SelectionRevision = 2, SelectionMode = "automatic", EffectiveProfileId = "plan-1", PolicyRevision = 1, InstanceRevoked = false, AccountDisabled = false, EmergencyStop = false, MandatoryUpdate = false, MinClientVersion = "1.0.0", ServerTime = "2026-06-12T14:00:00Z" },
                };
                var envelope = new ManagedApiResponse<HeartbeatResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/status")
            {
                var data = new ManagedStatus
                {
                    DesiredRevision = 7,
                    AssignmentRevision = 7,
                    SelectionRevision = 2,
                    AppliedRevision = null,
                    PolicyRevision = 1,
                    SelectionMode = "automatic",
                    RecommendedProfileId = "plan-1",
                    SelectedProfileId = null,
                    EffectiveProfileId = "plan-1",
                    FallbackProfileIds = [],
                    Profiles = [new ProfileSummary { Id = "plan-1", DisplayName = "Test", Recommended = true, Available = true, Maintenance = false, CapabilityStatus = "compatible", Quality = new ProfileQuality { LatencyMs = 10, Stability = "good", SampledAt = "2026-06-12T14:00:00Z" } }],
                    InstanceRevoked = false,
                    AccountDisabled = false,
                    EmergencyStop = false,
                    MandatoryUpdate = false,
                    MinClientVersion = "1.0.0",
                    ServerTime = "2026-06-12T14:00:00Z",
                };
                var envelope = new ManagedApiResponse<ManagedStatus> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/config")
            {
                var spikeEnvelope = new ManagedEnvelopeSpikeV0
                {
                    Schema = "managed-envelope/spike-v0",
                    PayloadSchema = "managed-config/v1",
                    AssignmentRevision = 7,
                    SelectionRevision = 2,
                    AccountId = 1,
                    InstanceId = "i1",
                    IssuedAt = "2026-06-12T14:00:00Z",
                    ExpiresAt = "2026-06-12T14:15:00Z",
                    Algorithm = "access-token-sha256-aes256gcm",
                    Nonce = nonce,
                    Ciphertext = ciphertext,
                };
                var envelope = new ManagedApiResponse<ManagedEnvelopeSpikeV0> { Code = 200, Message = "ok", ErrorCode = "", Data = spikeEnvelope };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path.StartsWith("/api/v1/client/managed/config/") && path.EndsWith("/ack"))
            {
                // ACK fails with 500
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{\"code\":500,\"message\":\"error\",\"error_code\":\"internal\",\"data\":null}", Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{}}", Encoding.UTF8, "application/json"),
            });
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, accessToken);

        var result = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.Ready, result.State);
        var config = await orch.GetCurrentConfigAsync();
        Assert.NotNull(config);
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
