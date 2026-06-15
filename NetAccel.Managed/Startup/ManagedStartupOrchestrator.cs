using NetAccel.Managed.Api;
using NetAccel.Managed.Auth;
using NetAccel.Managed.Crypto;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Identity;
using NetAccel.Managed.Instance;
using NetAccel.Managed.Selection;
using NetAccel.Managed.Vault;

namespace NetAccel.Managed.Startup;

/// <summary>
/// Coordinates managed startup: vault -> refresh -> instance -> heartbeat -> policy/status -> config fetch/decrypt/ack.
/// Does not start core, modify system proxy, enable TUN, or write to classic SQLite.
/// </summary>
public interface IManagedStartupOrchestrator
{
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
    private readonly ManagedApiClient _api;
    private readonly ICredentialVault _vault;
    private readonly string _clientVersion;
    private readonly Dictionary<string, string> _coreVersions;
    private readonly ClientCapabilities _capabilities;
    private readonly Func<string, Task>? _logAsync;

    private ManagedConfigPayload? _currentConfig;

    public ManagedStartupOrchestrator(
        IAuthService auth,
        IInstallationIdentityService installation,
        IInstanceService instance,
        IManagedSelectionService selection,
        IManagedConfigService configService,
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
            if (!await _auth.HasCredentialsAsync())
            {
                return new ManagedStartupResult { State = ManagedStartupState.NeedsLogin };
            }

            // 2. Refresh if needed
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

            // 4. Heartbeat / control
            var hbResult = await _instance.HeartbeatAsync(
                _clientVersion,
                _coreVersions,
                _capabilities,
                effectiveProfileId: null,
                sessionActive: false,
                ct: ct);

            if (hbResult.Kind == HeartbeatResultKind.Revoked)
            {
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

            // 5. Policy / status
            var statusResult = await _selection.GetStatusAsync(ct);
            if (statusResult.Kind == PolicyStatusResultKind.Revoked)
            {
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

            // 6. Config fetch / decrypt / ack (spike-v0)
            var (payload, revision, envelopeParsed) = await FetchConfigAsync(ct);

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

            // Config fetch failure is not fatal for startup state; we can retry later
            return new ManagedStartupResult { State = ManagedStartupState.Ready };
        }
        catch (ManagedOperationCancelledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await LogAsync($"Startup orchestration error: {ex.GetType().Name}: {ex.Message}");
            return new ManagedStartupResult { State = ManagedStartupState.Faulted, Message = ex.Message };
        }
    }

    public async Task<ManagedConfigPayload?> GetCurrentConfigAsync()
    {
        return _currentConfig;
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

    private async Task<(ManagedConfigPayload? Payload, int? Revision, bool EnvelopeParsed)> FetchConfigAsync(CancellationToken ct)
    {
        var instanceCredential = await _vault.RetrieveAsync(CredentialVaultEntry.InstanceCredential);
        if (string.IsNullOrEmpty(instanceCredential))
        {
            return (null, null, false);
        }

        Dictionary<string, string>? extraHeaders = null;
        if (_currentConfig != null)
        {
            extraHeaders = new Dictionary<string, string>
            {
                ["If-None-Match"] = $"\"{_currentConfig.AssignmentRevision}\"",
            };
        }

        var opResult = await _auth.ExecuteWithRefreshAsync(
            async (token, ctInner) => await _api.SendRawAsync(
                HttpMethod.Get,
                "/client/managed/config",
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
            return (null, null, false);
        }

        var (status, content, _) = opResult.Value;

        if (status == System.Net.HttpStatusCode.NotModified)
        {
            return (_currentConfig, _currentConfig?.AssignmentRevision, true);
        }

        try
        {
            if (!string.IsNullOrEmpty(content))
            {
                var apiEnvelope = System.Text.Json.JsonSerializer.Deserialize<ManagedApiResponse<ManagedEnvelopeSpikeV0>>(content, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
                if (apiEnvelope?.Data != null)
                {
                    var envelope = apiEnvelope.Data;
                    try
                    {
                        var decrypted = SpikeV0EnvelopeCrypto.Decrypt(envelope, await _auth.GetAccessTokenAsync() ?? string.Empty);
                        return (decrypted, envelope.AssignmentRevision, true);
                    }
                    catch
                    {
                        return (null, envelope.AssignmentRevision, true);
                    }
                }
            }
        }
        catch (NotSupportedException)
        {
            await LogAsync("Config fetch envelope algorithm not supported");
        }

        return (null, null, false);
    }

    private async Task LogAsync(string message)
    {
        if (_logAsync != null)
        {
            await _logAsync(message);
        }
    }
}
