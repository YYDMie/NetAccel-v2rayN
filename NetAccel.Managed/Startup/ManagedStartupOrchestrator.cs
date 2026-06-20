using NetAccel.Managed.Api;
using NetAccel.Managed.Auth;
using NetAccel.Managed.Cache;
using NetAccel.Managed.Crypto;
using NetAccel.Managed.Domain;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Identity;
using NetAccel.Managed.Instance;
using NetAccel.Managed.Selection;
using NetAccel.Managed.Vault;

namespace NetAccel.Managed.Startup;

/// <summary>
/// Coordinates managed startup: vault -> refresh -> instance -> device key -> heartbeat -> policy/status -> config fetch/decrypt/ack.
/// Does not start core, modify system proxy, enable TUN, or write to classic SQLite.
/// </summary>
public interface IManagedStartupOrchestrator
{
    event Action<ManagedStartupPhase>? ProgressChanged;
    Task<ManagedStartupResult> StartupAsync(CancellationToken ct = default);
    Task<ManagedConfigPayload?> GetCurrentConfigAsync();
}

public sealed class ManagedStartupOrchestrator : IManagedStartupOrchestrator
{
    private readonly IAuthService _auth;
    private readonly IInstallationIdentityService _installation;
    private readonly IInstanceService _instance;
    private readonly IManagedSelectionService _selection;
    private readonly IManagedConfigService _configService;
    private readonly IDeviceKeyManager _deviceKeyManager;
    private readonly IEnvelopeCacheManager _envelopeCache;
    private readonly IOfflineConfigRules _offlineRules;
    private readonly IServerKeyProvider _serverKeyProvider;
    private readonly ManagedApiClient _api;
    private readonly ICredentialVault _vault;
    private readonly string _clientVersion;
    private readonly Dictionary<string, string> _coreVersions;
    private readonly ClientCapabilities _capabilities;
    private readonly Func<string, Task>? _logAsync;

    private ManagedConfigPayload? _currentConfig;

    public event Action<ManagedStartupPhase>? ProgressChanged;

    public ManagedStartupOrchestrator(
        IAuthService auth,
        IInstallationIdentityService installation,
        IInstanceService instance,
        IManagedSelectionService selection,
        IManagedConfigService configService,
        IDeviceKeyManager deviceKeyManager,
        IEnvelopeCacheManager envelopeCache,
        IOfflineConfigRules offlineRules,
        IServerKeyProvider serverKeyProvider,
        ManagedApiClient api,
        ICredentialVault vault,
        string clientVersion,
        Dictionary<string, string> coreVersions,
        ClientCapabilities capabilities,
        Func<string, Task>? logAsync = null)
    {
        _auth = auth;
        _installation = installation;
        _instance = instance;
        _selection = selection;
        _configService = configService;
        _deviceKeyManager = deviceKeyManager;
        _envelopeCache = envelopeCache;
        _offlineRules = offlineRules;
        _serverKeyProvider = serverKeyProvider;
        _api = api;
        _vault = vault;
        _clientVersion = clientVersion;
        _coreVersions = coreVersions;
        _capabilities = capabilities;
        _logAsync = logAsync;
    }

    public async Task<ManagedStartupResult> StartupAsync(CancellationToken ct = default)
    {
        try
        {
            // 1. Check vault for credentials
            ReportProgress(ManagedStartupPhase.CheckingCredentials);
            if (!await _auth.HasCredentialsAsync())
            {
                return new ManagedStartupResult { State = ManagedStartupState.NeedsLogin };
            }

            // 2. Refresh if needed
            ReportProgress(ManagedStartupPhase.RefreshingSession);
            var refreshResult = await _auth.RefreshAsync(ct);
            if (!refreshResult.IsSuccess)
            {
                return refreshResult.Kind switch
                {
                    AuthResultKind.AccountDisabled => new ManagedStartupResult { State = ManagedStartupState.AccountDisabled, Message = refreshResult.ErrorCode, RequestId = refreshResult.RequestId },
                    AuthResultKind.SessionRevoked => new ManagedStartupResult { State = ManagedStartupState.NeedsLogin, Message = refreshResult.ErrorCode, RequestId = refreshResult.RequestId },
                    AuthResultKind.BadCredentials => new ManagedStartupResult { State = ManagedStartupState.NeedsLogin, Message = refreshResult.ErrorCode, RequestId = refreshResult.RequestId },
                    AuthResultKind.NetworkError => new ManagedStartupResult { State = ManagedStartupState.Offline, Message = "refresh_timeout" },
                    _ => new ManagedStartupResult { State = ManagedStartupState.Faulted, Message = refreshResult.ErrorCode, RequestId = refreshResult.RequestId },
                };
            }

            // 3. Ensure instance
            ReportProgress(ManagedStartupPhase.BindingInstance);
            var installationKey = await _installation.GetOrCreateInstallationKeyAsync();
            var instanceId = await _instance.GetInstanceIdAsync();
            var oldCredential = await _instance.GetInstanceCredentialAsync();

            if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(oldCredential))
            {
                var regResult = await _instance.RegisterOrRecoverAsync(
                    installationKey,
                    "windows",
                    Environment.OSVersion.Version.ToString(),
                    System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                    _clientVersion,
                    _coreVersions,
                    _capabilities,
                    oldInstanceCredential: oldCredential,
                    ct: ct);

                if (!regResult.IsSuccess)
                {
                    return regResult.Kind switch
                    {
                        InstanceResultKind.Revoked => new ManagedStartupResult { State = ManagedStartupState.InstanceRevoked, Message = regResult.ErrorCode, RequestId = regResult.RequestId },
                        InstanceResultKind.AccountDisabled => new ManagedStartupResult { State = ManagedStartupState.AccountDisabled, Message = regResult.ErrorCode, RequestId = regResult.RequestId },
                        InstanceResultKind.MandatoryUpdate => new ManagedStartupResult { State = ManagedStartupState.MandatoryUpdate, Message = regResult.ErrorCode, RequestId = regResult.RequestId },
                        InstanceResultKind.NetworkError => new ManagedStartupResult { State = ManagedStartupState.Offline, Message = "register_timeout" },
                        _ => new ManagedStartupResult { State = ManagedStartupState.Faulted, Message = regResult.ErrorCode, RequestId = regResult.RequestId },
                    };
                }
            }

            // 4. Ensure device key and register with instance
            ReportProgress(ManagedStartupPhase.RegisteringDeviceKey);
            var (deviceKeyResult, deviceKeyStartupResult) = await EnsureDeviceKeyAsync(ct);
            if (!deviceKeyResult.IsSuccess)
            {
                return deviceKeyStartupResult!;
            }

            // 5. Heartbeat / control
            ReportProgress(ManagedStartupPhase.CheckingControlState);
            var hbResult = await _instance.HeartbeatAsync(
                _clientVersion,
                _coreVersions,
                _capabilities,
                effectiveProfileId: null,
                sessionActive: false,
                ct: ct);

            if (hbResult.Kind == HeartbeatResultKind.Revoked)
            {
                await _envelopeCache.ClearAsync(ct);
                await _deviceKeyManager.ClearAsync();
                return new ManagedStartupResult { State = ManagedStartupState.InstanceRevoked, Message = hbResult.ErrorCode, RequestId = hbResult.RequestId };
            }
            if (hbResult.Kind == HeartbeatResultKind.AccountDisabled)
            {
                return new ManagedStartupResult { State = ManagedStartupState.AccountDisabled, Message = hbResult.ErrorCode, RequestId = hbResult.RequestId };
            }
            if (hbResult.Kind == HeartbeatResultKind.EmergencyStop)
            {
                return new ManagedStartupResult { State = ManagedStartupState.AccountDisabled, Message = ManagedErrorCode.ManagedEmergencyStop, RequestId = hbResult.RequestId };
            }
            if (hbResult.Kind == HeartbeatResultKind.MandatoryUpdate)
            {
                return new ManagedStartupResult { State = ManagedStartupState.MandatoryUpdate, Message = hbResult.ErrorCode, RequestId = hbResult.RequestId };
            }

            // 6. Policy / status
            var statusResult = await _selection.GetStatusAsync(ct);
            if (statusResult.Kind == PolicyStatusResultKind.Revoked)
            {
                await _envelopeCache.ClearAsync(ct);
                await _deviceKeyManager.ClearAsync();
                return new ManagedStartupResult { State = ManagedStartupState.InstanceRevoked, Message = statusResult.ErrorCode, RequestId = statusResult.RequestId };
            }
            if (statusResult.Kind == PolicyStatusResultKind.AccountDisabled)
            {
                return new ManagedStartupResult { State = ManagedStartupState.AccountDisabled, Message = statusResult.ErrorCode, RequestId = statusResult.RequestId };
            }
            if (statusResult.Kind == PolicyStatusResultKind.NetworkError)
            {
                return new ManagedStartupResult { State = ManagedStartupState.Offline, Message = "status_timeout" };
            }

            if (statusResult.Data?.Profiles.Count == 0)
            {
                return new ManagedStartupResult { State = ManagedStartupState.NoAssignment };
            }

            // 7. Config fetch / decrypt / ack using managed-envelope/v1
            ReportProgress(ManagedStartupPhase.SyncingConfiguration);
            var (payload, revision, envelopeParsed, fromOffline) = await FetchConfigV1Async(ct);

            if (envelopeParsed && revision.HasValue)
            {
                await SafeAckAsync(revision.Value, "received", null, null, ct);
            }

            if (payload != null)
            {
                _currentConfig = payload;
                await SafeAckAsync(payload.AssignmentRevision, "validated", null, null, ct);
                return new ManagedStartupResult { State = ManagedStartupState.Ready };
            }

            if (envelopeParsed && revision.HasValue)
            {
                await SafeAckAsync(revision.Value, "failed", "decrypt_failed", "decrypt_failed", ct);
            }

            // If we have an offline config, we can still report Ready
            if (fromOffline && _currentConfig != null)
            {
                return new ManagedStartupResult { State = ManagedStartupState.Ready };
            }

            // Config fetch failure is not fatal for startup state; we can retry later
            return new ManagedStartupResult { State = ManagedStartupState.Ready };
        }
        catch (ManagedOperationCancelledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await LogAsync($"Startup orchestration error: {ex.GetType().Name}");
            return new ManagedStartupResult { State = ManagedStartupState.Faulted, Message = "startup_failed" };
        }
    }

    public async Task<ManagedConfigPayload?> GetCurrentConfigAsync()
    {
        return _currentConfig;
    }

    private async Task<(EnsureResult Result, ManagedStartupResult? StartupResult)> EnsureDeviceKeyAsync(CancellationToken ct)
    {
        var deviceKey = await _deviceKeyManager.GetOrCreateKeyAsync();
        var instanceId = await _instance.GetInstanceIdAsync();
        var instanceCredential = await _vault.RetrieveAsync(CredentialVaultEntry.InstanceCredential);

        if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(instanceCredential))
        {
            return (new EnsureResult { IsSuccess = false }, new ManagedStartupResult { State = ManagedStartupState.Faulted, Message = "missing_instance_identity" });
        }

        var opResult = await _auth.ExecuteWithRefreshAsync(
            async (token, ctInner) => await _api.RegisterDeviceKeyAsync(
                new DeviceKeyRegisterRequest
                {
                    KeyId = deviceKey.KeyId,
                    PublicKeyPem = deviceKey.PublicKeyPem,
                },
                accessToken: token,
                instanceCredential: instanceCredential,
                ct: ctInner),
            ct);

        if (opResult.IsSuccess)
        {
            return (new EnsureResult { IsSuccess = true }, null);
        }

        if (opResult.RefreshResult != null)
        {
            var state = opResult.RefreshResult.Kind switch
            {
                AuthResultKind.AccountDisabled => ManagedStartupState.AccountDisabled,
                AuthResultKind.NetworkError => ManagedStartupState.Offline,
                _ => ManagedStartupState.Faulted,
            };
            return (new EnsureResult { IsSuccess = false }, new ManagedStartupResult { State = state, Message = opResult.RefreshResult.ErrorCode, RequestId = opResult.RefreshResult.RequestId });
        }

        if (opResult.OperationException is { } ex)
        {
            var state = ex.ErrorCode switch
            {
                ManagedErrorCode.ClientInstanceRevoked => ManagedStartupState.InstanceRevoked,
                ManagedErrorCode.ClientAccountInactive => ManagedStartupState.AccountDisabled,
                ManagedErrorCode.ManagedKeyRevoked => ManagedStartupState.InstanceRevoked,
                _ => ManagedStartupState.Faulted,
            };
            if (state == ManagedStartupState.InstanceRevoked)
            {
                await _envelopeCache.ClearAsync(ct);
                await _deviceKeyManager.ClearAsync();
            }
            return (new EnsureResult { IsSuccess = false }, new ManagedStartupResult { State = state, Message = ex.ErrorCode, RequestId = ex.RequestId });
        }

        return (new EnsureResult { IsSuccess = false }, new ManagedStartupResult { State = ManagedStartupState.Faulted, Message = "device_key_registration_failed" });
    }

    private async Task SafeAckAsync(int revision, string status, string? errorCode, string? errorDetail, CancellationToken ct)
    {
        try
        {
            await _configService.AckConfigAsync(revision, status, _clientVersion, _coreVersions, errorCode, errorDetail, ct);
        }
        catch (Exception ex)
        {
            await LogAsync($"Config ACK error: {ex.GetType().Name}");
        }
    }

    private async Task<(ManagedConfigPayload? Payload, int? Revision, bool EnvelopeParsed, bool FromOffline)> FetchConfigV1Async(CancellationToken ct)
    {
        var instanceCredential = await _vault.RetrieveAsync(CredentialVaultEntry.InstanceCredential);
        if (string.IsNullOrEmpty(instanceCredential))
        {
            return (null, null, false, false);
        }

        var instanceId = await _instance.GetInstanceIdAsync();
        var accountId = await _auth.GetAccountIdAsync();
        var deviceKey = await _deviceKeyManager.GetCurrentKeyAsync();
        var serverPublicKey = await _serverKeyProvider.GetServerPublicKeyPemAsync();
        var devicePrivateKey = await _deviceKeyManager.GetPrivateKeyPemAsync();

        if (string.IsNullOrEmpty(instanceId) || deviceKey == null || string.IsNullOrEmpty(serverPublicKey) || string.IsNullOrEmpty(devicePrivateKey))
        {
            await LogAsync("Fetch config skipped: missing identity or keys");
            return (null, null, false, false);
        }

        // Build If-None-Match from current in-memory config or cached envelope
        var cachedEntry = await _envelopeCache.ReadAsync(ct);
        string? ifNoneMatch = null;
        if (_currentConfig != null)
        {
            ifNoneMatch = $"\"{_currentConfig.AssignmentRevision}\"";
        }
        else if (cachedEntry != null)
        {
            ifNoneMatch = $"\"{cachedEntry.Metadata.AssignmentRevision}\"";
        }

        var extraHeaders = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(ifNoneMatch))
        {
            extraHeaders["If-None-Match"] = ifNoneMatch;
        }
        extraHeaders["X-Device-Key-Id"] = deviceKey.KeyId;

        var opResult = await _auth.ExecuteWithRefreshAsync(
            async (token, ctInner) => await _api.SendRawAsync(
                HttpMethod.Get,
                "/client/managed/envelope",
                null,
                accessToken: token,
                instanceCredential: instanceCredential,
                extraHeaders: extraHeaders,
                ct: ctInner),
            ct);

        if (!opResult.IsSuccess)
        {
            if (opResult.RefreshResult != null)
            {
                await LogAsync($"Config fetch refresh failed: {opResult.RefreshResult.Kind}");
            }
            else if (opResult.OperationException is { } ex)
            {
                await LogAsync($"Config fetch API error: {ex.ErrorCode}");
            }

            // Try offline fallback
            var offlineResult = _offlineRules.Evaluate(cachedEntry, instanceId, deviceKey.KeyId);
            if (offlineResult.CanUseOffline && cachedEntry != null)
            {
                var offlinePayload = TryDecryptCached(cachedEntry.Envelope, devicePrivateKey, serverPublicKey, instanceId, accountId, deviceKey.KeyId);
                if (offlinePayload != null)
                {
                    await LogAsync("Using offline cached config");
                    return (offlinePayload, cachedEntry.Metadata.AssignmentRevision, true, true);
                }
            }

            return (null, null, false, false);
        }

        var (status, content, _) = opResult.Value;

        if (status == System.Net.HttpStatusCode.NotModified)
        {
            // Use cached or in-memory config
            if (_currentConfig != null)
            {
                return (_currentConfig, _currentConfig.AssignmentRevision, true, false);
            }

            if (cachedEntry != null)
            {
                var payload = TryDecryptCached(cachedEntry.Envelope, devicePrivateKey, serverPublicKey, instanceId, accountId, deviceKey.KeyId);
                return (payload, cachedEntry.Metadata.AssignmentRevision, true, payload != null);
            }

            return (null, null, true, false);
        }

        try
        {
            if (!string.IsNullOrEmpty(content))
            {
                var apiEnvelope = System.Text.Json.JsonSerializer.Deserialize<ManagedApiResponse<ManagedEnvelopeV1>>(content, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });

                if (apiEnvelope?.Data != null)
                {
                    var envelope = apiEnvelope.Data;

                    // Verify and decrypt
                    var payload = ManagedEnvelopeV1Crypto.DecryptAndVerify(
                        envelope,
                        devicePrivateKey,
                        serverPublicKey,
                        instanceId,
                        accountId ?? envelope.AccountId,
                        deviceKey.KeyId);

                    if (payload != null)
                    {
                        var validation = ManagedProfileValidator.ValidatePayload(payload);
                        if (validation.Success)
                        {
                            // Atomically replace cache
                            try
                            {
                                await _envelopeCache.WriteAsync(envelope, ct);
                            }
                            catch (Exception ex)
                            {
                                await LogAsync($"Cache write error: {ex.GetType().Name}");
                            }

                            return (payload, envelope.AssignmentRevision, true, false);
                        }

                        await LogAsync($"Payload validation failed: {string.Join("; ", validation.Errors)}");

                        // On invalid new envelope, keep last valid cache and report failure
                        var cached = await _envelopeCache.ReadAsync(ct);
                        if (cached != null)
                        {
                            var fallback = TryDecryptCached(cached.Envelope, devicePrivateKey, serverPublicKey, instanceId, accountId, deviceKey.KeyId);
                            if (fallback != null)
                            {
                                return (fallback, envelope.AssignmentRevision, true, false);
                            }
                        }

                        return (null, envelope.AssignmentRevision, true, false);
                    }

                    await LogAsync("Envelope decrypt/verify failed");

                    // Decrypt failed but envelope was structurally valid; keep last valid cache
                    var lastValid = await _envelopeCache.ReadAsync(ct);
                    if (lastValid != null)
                    {
                        var fallback = TryDecryptCached(lastValid.Envelope, devicePrivateKey, serverPublicKey, instanceId, accountId, deviceKey.KeyId);
                        if (fallback != null)
                        {
                            return (fallback, envelope.AssignmentRevision, true, false);
                        }
                    }

                    return (null, envelope.AssignmentRevision, true, false);
                }
            }
        }
        catch (Exception ex)
        {
            await LogAsync($"Config fetch parse error: {ex.GetType().Name}");
        }

        return (null, null, false, false);
    }

    private ManagedConfigPayload? TryDecryptCached(
        ManagedEnvelopeV1 envelope,
        string devicePrivateKey,
        string serverPublicKey,
        string instanceId,
        int? accountId,
        string keyId)
    {
        try
        {
            return ManagedEnvelopeV1Crypto.DecryptAndVerify(
                envelope,
                devicePrivateKey,
                serverPublicKey,
                instanceId,
                accountId ?? envelope.AccountId,
                keyId);
        }
        catch
        {
            return null;
        }
    }

    private async Task LogAsync(string message)
    {
        if (_logAsync != null)
        {
            await _logAsync(message);
        }
    }

    private void ReportProgress(ManagedStartupPhase phase)
    {
        ProgressChanged?.Invoke(phase);
    }

    private sealed class EnsureResult
    {
        public bool IsSuccess { get; init; }
    }
}
