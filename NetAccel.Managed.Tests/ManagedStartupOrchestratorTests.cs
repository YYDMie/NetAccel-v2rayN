using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetAccel.Managed.Api;
using NetAccel.Managed.Auth;
using NetAccel.Managed.Cache;
using NetAccel.Managed.Crypto;
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
    private static (ManagedStartupOrchestrator Orchestrator, InMemoryCredentialVault Vault, HttpClient Http, string CacheDir, string ServerPublicKey, string ServerPrivateKey) CreateOrchestrator(
        Func<InMemoryCredentialVault, string, string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>? handlerFactory = null,
        Func<string, Task>? logAsync = null)
    {
        var vault = new InMemoryCredentialVault();
        var cacheDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var envelopeCache = new EnvelopeCacheManager(cacheDir);
        var offlineRules = new OfflineConfigRules();

        using var serverEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var serverPublicKey = serverEcdsa.ExportSubjectPublicKeyInfoPem();
        var serverPrivateKey = serverEcdsa.ExportPkcs8PrivateKeyPem();

        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> defaultHandler = (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{}}", Encoding.UTF8, "application/json"),
        });

        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> innerHandler = handlerFactory?.Invoke(vault, serverPublicKey, serverPrivateKey) ?? defaultHandler;
        var http = new HttpClient(new FakeHandler(innerHandler));
        var api = new ManagedApiClient("https://api.example.com", http);
        var auth = new AuthService(api, vault);
        var installation = new InstallationIdentityService(vault);
        var instance = new InstanceService(api, vault, auth);
        var selection = new ManagedSelectionService(api, vault, auth);
        var configService = new ManagedConfigService(api, vault, auth);
        var deviceKeyManager = new DeviceKeyManager(vault);

        var serverKeyProvider = new StaticServerKeyProvider(serverPublicKey);

        var orch = new ManagedStartupOrchestrator(
            auth, installation, instance, selection, configService,
            deviceKeyManager, envelopeCache, offlineRules, serverKeyProvider,
            api, vault, "1.0.0", new(), new(), logAsync);

        return (orch, vault, http, cacheDir, serverPublicKey, serverPrivateKey);
    }

    private static ManagedEnvelopeV1 MakeV1Envelope(
        string payloadJson,
        string devicePublicKey,
        string serverPrivateKey,
        InMemoryCredentialVault? vault = null,
        string serverKeyId = "server-key-1",
        int accountId = 1,
        string instanceId = "i1",
        string? keyId = null,
        int assignmentRevision = 7,
        int selectionRevision = 2,
        DateTimeOffset? issuedAt = null,
        DateTimeOffset? expiresAt = null)
    {
        var actualKeyId = keyId ?? vault?.RetrieveAsync(CredentialVaultEntry.DeviceKeyId).Result ?? "device-key-1";
        return ManagedEnvelopeV1Crypto.EncryptForTest(
            payloadJson, devicePublicKey, serverPrivateKey, serverKeyId,
            accountId, instanceId, actualKeyId, assignmentRevision, selectionRevision,
            issuedAt, expiresAt);
    }

    private static string MakePayloadJson(ManagedConfigPayload payload)
        => JsonSerializer.Serialize(payload);

    private static ManagedConfigPayload CreateValidPayload(int assignmentRevision = 7, int selectionRevision = 2)
    {
        return new ManagedConfigPayload
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = assignmentRevision,
            SelectionRevision = selectionRevision,
            RecommendedProfileId = "plan-1",
            FallbackProfileIds = [],
            Profiles =
            [
                new ManagedProfile
                {
                    Id = "plan-1",
                    DisplayName = "Test",
                    Available = true,
                    Protocol = "vless",
                    CorePreference = "xray",
                    Endpoint = new EndpointInfo { Host = "test.example.com", Port = 443 },
                    Transport = new TransportInfo { Network = "raw" },
                    VlessCredentials = new VlessCredentials { Uuid = "00000000-0000-0000-0000-000000000001" },
                    VlessRealitySecurity = new VlessRealitySecurity
                    {
                        Type = "reality",
                        ServerName = "test.example.com",
                        PublicKey = "test-pub-key",
                        ShortId = "01",
                        Fingerprint = "chrome",
                    },
                    Policy = new ProfilePolicy { AllowSystemProxy = true, AllowTun = true, AllowLocalProxy = false },
                },
            ],
            RoutingPolicy = new ManagedRoutingPolicy { Mode = "managed_default" },
            DnsPolicy = new ManagedDnsPolicy { NormalDns = "https://cloudflare-dns.com/dns-query", TunDns = "https://cloudflare-dns.com/dns-query" },
            ClientPolicy = new ClientPolicy { AllowClassicMode = false, AllowTun = true, AllowLocalProxy = false, AllowManualSelection = true, AllowAutomaticFailover = true, OfflineGraceSeconds = 3600 },
        };
    }

    [Fact]
    public async Task Startup_NoCredentials_ReturnsNeedsLogin()
    {
        var (orch, _, http, cacheDir, _, _) = CreateOrchestrator();
        var result = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.NeedsLogin, result.State);
        http.Dispose();
        Directory.Delete(cacheDir, true);
    }

    [Fact]
    public async Task Startup_NoCredentials_ReportsCredentialCheckPhase()
    {
        var (orch, _, http, cacheDir, _, _) = CreateOrchestrator();
        var phases = new List<ManagedStartupPhase>();
        orch.ProgressChanged += phases.Add;

        await orch.StartupAsync(TestContext.Current.CancellationToken);

        Assert.Equal([ManagedStartupPhase.CheckingCredentials], phases);
        http.Dispose();
        Directory.Delete(cacheDir, true);
    }

    [Fact]
    public async Task Startup_UnexpectedException_DoesNotExposeSensitiveMessage()
    {
        var logs = new List<string>();
        var (orch, vault, http, cacheDir, _, _) = CreateOrchestrator(
            (_, _, _) => (_, _) => throw new InvalidOperationException(
                "Authorization=Bearer secret-token private.example:443"),
            message =>
            {
                logs.Add(message);
                return Task.CompletedTask;
            });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "refresh-secret");

        try
        {
            var result = await orch.StartupAsync(TestContext.Current.CancellationToken);

            Assert.Equal(ManagedStartupState.Faulted, result.State);
            Assert.Equal("startup_failed", result.Message);
            var log = Assert.Single(logs);
            Assert.Contains(nameof(InvalidOperationException), log, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-token", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private.example", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Authorization", log, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            http.Dispose();
            Directory.Delete(cacheDir, true);
        }
    }

    [Fact]
    public async Task Startup_RefreshSuccess_InstanceSuccess_ReturnsReady()
    {
        var (orch, vault, http, cacheDir, _, _) = CreateOrchestrator((vault, serverPublicKey, serverPrivateKey) => (req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = "at2", RefreshToken = "rt2", AccountId = 1 };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = RegistrationResponse();
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
        await vault.StoreAsync(CredentialVaultEntry.AccountId, "1");

        var result = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.Ready, result.State);
        http.Dispose();
        Directory.Delete(cacheDir, true);
    }

    [Fact]
    public async Task Startup_RefreshAccountDisabled_ReturnsAccountDisabled()
    {
        var (orch, vault, http, cacheDir, _, _) = CreateOrchestrator((vault, serverPublicKey, serverPrivateKey) => (req, ct) =>
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
        Directory.Delete(cacheDir, true);
    }

    [Fact]
    public async Task Startup_InstanceRevoked_ReturnsInstanceRevoked()
    {
        var (orch, vault, http, cacheDir, _, _) = CreateOrchestrator((vault, serverPublicKey, serverPrivateKey) => (req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = "at2", RefreshToken = "rt2", AccountId = 1 };
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
        Directory.Delete(cacheDir, true);
    }

    [Fact]
    public async Task Startup_NoAssignment_ReturnsNoAssignment()
    {
        var (orch, vault, http, cacheDir, _, _) = CreateOrchestrator((vault, serverPublicKey, serverPrivateKey) => (req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = "at2", RefreshToken = "rt2", AccountId = 1 };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = RegistrationResponse();
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
            if (path == "/api/v1/client/managed/keys")
            {
                var data = new DeviceKeyRegisterResponse { Id = "dk1", KeyId = "device-key-1", InstanceId = "i1", CreatedAt = "2026-06-12T14:00:00Z" };
                var envelope = new ManagedApiResponse<DeviceKeyRegisterResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
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
        Directory.Delete(cacheDir, true);
    }

    [Fact]
    public async Task Startup_ConfigFetch_V1Envelope_ReturnsDecryptedPayload()
    {
        var payload = CreateValidPayload();

        var (orch, vault, http, cacheDir, serverPublicKey, serverPrivateKey) = CreateOrchestrator((vault, serverPublicKey, serverPrivateKey) => (req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = "at2", RefreshToken = "rt2", AccountId = 1 };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = RegistrationResponse();
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
            if (path == "/api/v1/client/managed/keys")
            {
                var data = new DeviceKeyRegisterResponse { Id = "dk1", KeyId = "device-key-1", InstanceId = "i1", CreatedAt = "2026-06-12T14:00:00Z" };
                var envelope = new ManagedApiResponse<DeviceKeyRegisterResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/managed/envelope")
            {
                var deviceKey = vault.RetrieveAsync(CredentialVaultEntry.DevicePrivateKey).Result;
                using var deviceEcdh = ECDiffieHellman.Create();
                deviceEcdh.ImportFromPem(deviceKey!);
                var devicePublicKey = deviceEcdh.ExportSubjectPublicKeyInfoPem();

                var v1Envelope = MakeV1Envelope(
                    MakePayloadJson(payload), devicePublicKey, serverPrivateKey,
                    vault, accountId: 1, instanceId: "i1");

                var envelope = new ManagedApiResponse<ManagedEnvelopeV1> { Code = 200, Message = "ok", ErrorCode = "", Data = v1Envelope };
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
        await vault.StoreAsync(CredentialVaultEntry.AccountId, "1");

        var result = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.Ready, result.State);

        var config = await orch.GetCurrentConfigAsync();
        Assert.NotNull(config);
        Assert.Equal(7, config.AssignmentRevision);
        Assert.Equal("managed-config/v1", config.PayloadSchema);
        http.Dispose();
        Directory.Delete(cacheDir, true);
    }

    [Fact]
    public async Task Startup_ConfigFetch_FirstFetch_OmitsIfNoneMatch()
    {
        HttpRequestMessage? captured = null;
        var (orch, vault, http, cacheDir, serverPublicKey, serverPrivateKey) = CreateOrchestrator((vault, serverPublicKey, serverPrivateKey) => (req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = "at2", RefreshToken = "rt2", AccountId = 1 };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = RegistrationResponse();
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
            if (path == "/api/v1/client/managed/envelope")
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
        await vault.StoreAsync(CredentialVaultEntry.AccountId, "1");

        await orch.StartupAsync();
        Assert.NotNull(captured);
        Assert.Empty(captured.Headers.IfNoneMatch);
        http.Dispose();
        Directory.Delete(cacheDir, true);
    }

    [Fact]
    public async Task Startup_ConfigFetch_SecondFetch_SendsIfNoneMatchWithAssignmentRevision()
    {
        HttpRequestMessage? captured = null;
        var payload = CreateValidPayload();

        var (orch, vault, http, cacheDir, serverPublicKey, serverPrivateKey) = CreateOrchestrator((vault, serverPublicKey, serverPrivateKey) => (req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = "at2", RefreshToken = "rt2", AccountId = 1 };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = RegistrationResponse();
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
            if (path == "/api/v1/client/managed/envelope")
            {
                captured = req;
                var deviceKey = vault.RetrieveAsync(CredentialVaultEntry.DevicePrivateKey).Result;
                using var deviceEcdh = ECDiffieHellman.Create();
                deviceEcdh.ImportFromPem(deviceKey!);
                var devicePublicKey = deviceEcdh.ExportSubjectPublicKeyInfoPem();

                var v1Envelope = MakeV1Envelope(
                    MakePayloadJson(payload), devicePublicKey, serverPrivateKey,
                    vault, accountId: 1, instanceId: "i1");

                var envelope = new ManagedApiResponse<ManagedEnvelopeV1> { Code = 200, Message = "ok", ErrorCode = "", Data = v1Envelope };
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
        await vault.StoreAsync(CredentialVaultEntry.AccountId, "1");

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
        Directory.Delete(cacheDir, true);
    }

    [Fact]
    public async Task Startup_ConfigFetch_304_KeepsExistingInMemoryConfig()
    {
        var payload = CreateValidPayload();

        var configCallCount = 0;
        var (orch, vault, http, cacheDir, serverPublicKey, serverPrivateKey) = CreateOrchestrator((vault, serverPublicKey, serverPrivateKey) => (req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = "at2", RefreshToken = "rt2", AccountId = 1 };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = RegistrationResponse();
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
            if (path == "/api/v1/client/managed/envelope")
            {
                configCallCount++;
                if (configCallCount == 1)
                {
                    var deviceKey = vault.RetrieveAsync(CredentialVaultEntry.DevicePrivateKey).Result;
                    using var deviceEcdh = ECDiffieHellman.Create();
                    deviceEcdh.ImportFromPem(deviceKey!);
                    var devicePublicKey = deviceEcdh.ExportSubjectPublicKeyInfoPem();

                    var v1Envelope = MakeV1Envelope(
                        MakePayloadJson(payload), devicePublicKey, serverPrivateKey,
                        vault, accountId: 1, instanceId: "i1");

                    var envelope = new ManagedApiResponse<ManagedEnvelopeV1> { Code = 200, Message = "ok", ErrorCode = "", Data = v1Envelope };
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
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.AccountId, "1");

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
        Directory.Delete(cacheDir, true);
    }

    [Fact]
    public async Task Startup_ConfigFetch_401RefreshesOnceAndRetriesOnce()
    {
        const string firstAccessToken = "at_first";
        const string refreshedAccessToken = "at_refreshed";
        var payload = CreateValidPayload();

        var refreshCalls = 0;
        var configCalls = 0;
        var (orch, vault, http, cacheDir, serverPublicKey, serverPrivateKey) = CreateOrchestrator((vault, serverPublicKey, serverPrivateKey) => (req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                refreshCalls++;
                var token = refreshCalls == 1 ? firstAccessToken : refreshedAccessToken;
                var data = new RefreshResponse { AccessToken = token, RefreshToken = $"rt{refreshCalls + 1}", AccountId = 1 };
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
                var data = RegistrationResponse();
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
            if (path == "/api/v1/client/managed/envelope")
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
                var deviceKey = vault.RetrieveAsync(CredentialVaultEntry.DevicePrivateKey).Result;
                using var deviceEcdh = ECDiffieHellman.Create();
                deviceEcdh.ImportFromPem(deviceKey!);
                var devicePublicKey = deviceEcdh.ExportSubjectPublicKeyInfoPem();

                var v1Envelope = MakeV1Envelope(
                    MakePayloadJson(payload), devicePublicKey, serverPrivateKey,
                    vault, accountId: 1, instanceId: "i1");

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new ManagedApiResponse<ManagedEnvelopeV1> { Code = 200, Message = "ok", ErrorCode = "", Data = v1Envelope }),
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
            if (path == "/api/v1/client/managed/keys")
            {
                var data = new DeviceKeyRegisterResponse { Id = "dk1", KeyId = "device-key-1", InstanceId = "i1", CreatedAt = "2026-06-16T00:00:00Z" };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new ManagedApiResponse<DeviceKeyRegisterResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data }),
                        Encoding.UTF8,
                        "application/json"),
                });
            }

            throw new InvalidOperationException($"Unexpected request: {path}");
        });
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt1");
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "stale");
        await vault.StoreAsync(CredentialVaultEntry.AccountId, "1");

        var result = await orch.StartupAsync();

        Assert.Equal(ManagedStartupState.Ready, result.State);
        Assert.Equal(2, refreshCalls);
        Assert.Equal(2, configCalls);
        Assert.Equal(7, (await orch.GetCurrentConfigAsync())?.AssignmentRevision);
        http.Dispose();
        Directory.Delete(cacheDir, true);
    }

    [Fact]
    public async Task Startup_ConfigAck_SuccessfulStartup_EmitsReceivedThenValidated_NeverApplied()
    {
        var payload = CreateValidPayload();

        var ackStatuses = new List<string>();
        var (orch, vault, http, cacheDir, serverPublicKey, serverPrivateKey) = CreateOrchestrator((vault, serverPublicKey, serverPrivateKey) => (req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = "at2", RefreshToken = "rt2", AccountId = 1 };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = RegistrationResponse();
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
            if (path == "/api/v1/client/managed/envelope")
            {
                var deviceKey = vault.RetrieveAsync(CredentialVaultEntry.DevicePrivateKey).Result;
                using var deviceEcdh = ECDiffieHellman.Create();
                deviceEcdh.ImportFromPem(deviceKey!);
                var devicePublicKey = deviceEcdh.ExportSubjectPublicKeyInfoPem();

                var v1Envelope = MakeV1Envelope(
                    MakePayloadJson(payload), devicePublicKey, serverPrivateKey,
                    vault, accountId: 1, instanceId: "i1");

                var envelope = new ManagedApiResponse<ManagedEnvelopeV1> { Code = 200, Message = "ok", ErrorCode = "", Data = v1Envelope };
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
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.AccountId, "1");

        var result = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.Ready, result.State);
        Assert.Contains("received", ackStatuses);
        Assert.Contains("validated", ackStatuses);
        Assert.DoesNotContain("applied", ackStatuses);
        http.Dispose();
        Directory.Delete(cacheDir, true);
    }

    [Fact]
    public async Task Startup_ConfigAck_DecryptFailure_EmitsSanitizedFailed()
    {
        var ackStatuses = new List<(string Status, string? Detail)>();
        var (orch, vault, http, cacheDir, serverPublicKey, serverPrivateKey) = CreateOrchestrator((vault, serverPublicKey, serverPrivateKey) => (req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = "at2", RefreshToken = "rt2", AccountId = 1 };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = RegistrationResponse();
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
            if (path == "/api/v1/client/managed/envelope")
            {
                // Return a valid v1 envelope but with tampered ciphertext so decrypt fails
                var deviceKey = vault.RetrieveAsync(CredentialVaultEntry.DevicePrivateKey).Result;
                using var deviceEcdh = ECDiffieHellman.Create();
                deviceEcdh.ImportFromPem(deviceKey!);
                var devicePublicKey = deviceEcdh.ExportSubjectPublicKeyInfoPem();

                var v1Envelope = MakeV1Envelope(
                    MakePayloadJson(CreateValidPayload()),
                    devicePublicKey, serverPrivateKey,
                    vault, accountId: 1, instanceId: "i1");

                var bytes = Convert.FromBase64String(v1Envelope.Ciphertext);
                bytes[^1] ^= 0xFF;
                v1Envelope.Ciphertext = Convert.ToBase64String(bytes);

                var envelope = new ManagedApiResponse<ManagedEnvelopeV1> { Code = 200, Message = "ok", ErrorCode = "", Data = v1Envelope };
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
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.AccountId, "1");

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
        Directory.Delete(cacheDir, true);
    }

    [Fact]
    public async Task Startup_ConfigAck_Failure_DoesNotPersistPlaintextOrStartRuntime()
    {
        var payload = CreateValidPayload();

        var (orch, vault, http, cacheDir, serverPublicKey, serverPrivateKey) = CreateOrchestrator((vault, serverPublicKey, serverPrivateKey) => (req, ct) =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v1/client/refresh")
            {
                var data = new RefreshResponse { AccessToken = "at2", RefreshToken = "rt2", AccountId = 1 };
                var envelope = new ManagedApiResponse<RefreshResponse> { Code = 200, Message = "ok", ErrorCode = "", Data = data };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
                });
            }
            if (path == "/api/v1/client/instances/register")
            {
                var data = RegistrationResponse();
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
            if (path == "/api/v1/client/managed/envelope")
            {
                var deviceKey = vault.RetrieveAsync(CredentialVaultEntry.DevicePrivateKey).Result;
                using var deviceEcdh = ECDiffieHellman.Create();
                deviceEcdh.ImportFromPem(deviceKey!);
                var devicePublicKey = deviceEcdh.ExportSubjectPublicKeyInfoPem();

                var v1Envelope = MakeV1Envelope(
                    MakePayloadJson(payload), devicePublicKey, serverPrivateKey,
                    vault, accountId: 1, instanceId: "i1");

                var envelope = new ManagedApiResponse<ManagedEnvelopeV1> { Code = 200, Message = "ok", ErrorCode = "", Data = v1Envelope };
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
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.AccountId, "1");

        var result = await orch.StartupAsync();
        Assert.Equal(ManagedStartupState.Ready, result.State);
        var config = await orch.GetCurrentConfigAsync();
        Assert.NotNull(config);
        http.Dispose();
        Directory.Delete(cacheDir, true);
    }

    private static ClientInstanceRegisterResponse RegistrationResponse() => new()
    {
        Instance = new RegisteredClientInstance { Id = "i1" },
        InstanceCredential = new InstanceCredential
        {
            Credential = "ic1",
            Scope = ["instance:heartbeat", "session:report"],
            ExpiresAt = 1781308800,
        },
    };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
