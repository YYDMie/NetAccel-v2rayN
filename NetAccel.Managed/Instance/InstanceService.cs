using System.Net;
using NetAccel.Managed.Api;
using NetAccel.Managed.Auth;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Vault;

namespace NetAccel.Managed.Instance;

public enum InstanceResultKind
{
    Success,
    RebindRequired,
    LimitReached,
    Revoked,
    AccountDisabled,
    MandatoryUpdate,
    NetworkError,
    UnknownError,
}

public sealed class InstanceResult
{
    public InstanceResultKind Kind { get; init; }
    public string? ErrorCode { get; init; }
    public string? RequestId { get; init; }
    public bool IsSuccess => Kind == InstanceResultKind.Success;
}

public enum HeartbeatResultKind
{
    Success,
    Revoked,
    AccountDisabled,
    EmergencyStop,
    MandatoryUpdate,
    CredentialScopeError,
    NetworkError,
    UnknownError,
}

public sealed class HeartbeatResult
{
    public HeartbeatResultKind Kind { get; init; }
    public HeartbeatControl? Control { get; init; }
    public string? ErrorCode { get; init; }
    public string? RequestId { get; init; }
}

public interface IInstanceService
{
    Task<InstanceResult> RegisterOrRecoverAsync(
        string installationKey,
        string platform,
        string platformVersion,
        string arch,
        string clientVersion,
        Dictionary<string, string> coreVersions,
        ClientCapabilities capabilities,
        string? oldInstanceCredential,
        CancellationToken ct = default);

    Task<HeartbeatResult> HeartbeatAsync(
        string clientVersion,
        Dictionary<string, string> coreVersions,
        ClientCapabilities capabilities,
        string? effectiveProfileId,
        bool sessionActive,
        CancellationToken ct = default);

    Task<InstanceResult> RotateCredentialAsync(CancellationToken ct = default);

    Task<string?> GetInstanceIdAsync();
    Task<string?> GetInstanceCredentialAsync();
}

public sealed class InstanceService : IInstanceService
{
    private readonly ManagedApiClient _api;
    private readonly ICredentialVault _vault;
    private readonly IAuthService _auth;
    private readonly Func<string, Task>? _logAsync;

    public InstanceService(ManagedApiClient api, ICredentialVault vault, IAuthService auth, Func<string, Task>? logAsync = null)
    {
        _api = api;
        _vault = vault;
        _auth = auth;
        _logAsync = logAsync;
    }

    public async Task<InstanceResult> RegisterOrRecoverAsync(
        string installationKey,
        string platform,
        string platformVersion,
        string arch,
        string clientVersion,
        Dictionary<string, string> coreVersions,
        ClientCapabilities capabilities,
        string? oldInstanceCredential,
        CancellationToken ct = default)
    {
        var request = new ClientInstanceRegisterRequest
        {
            InstallationKey = installationKey,
            DisplayName = $"{Environment.MachineName} ({Environment.UserName})",
            Platform = platform,
            PlatformVersion = platformVersion,
            Arch = arch,
            ClientVersion = clientVersion,
            EngineVersion = GetEngineVersion(coreVersions),
            Capabilities = capabilities,
        };

        var opResult = await _auth.ExecuteWithRefreshAsync(
            async (token, ctInner) => await _api.PostAsync<ClientInstanceRegisterResponse>("/client/instances/register", request, accessToken: token, ct: ctInner),
            ct);

        if (opResult.IsSuccess)
        {
            var result = opResult.Value!;
            await _vault.StoreAsync(CredentialVaultEntry.InstanceCredential, result.InstanceCredential.Credential);
            await _vault.StoreAsync(CredentialVaultEntry.InstanceMetadata, result.Instance.Id);
            return new InstanceResult { Kind = InstanceResultKind.Success };
        }

        if (opResult.RefreshResult != null)
        {
            var kind = opResult.RefreshResult.Kind switch
            {
                AuthResultKind.AccountDisabled => InstanceResultKind.AccountDisabled,
                AuthResultKind.NetworkError => InstanceResultKind.NetworkError,
                _ => InstanceResultKind.UnknownError,
            };
            return new InstanceResult { Kind = kind, ErrorCode = opResult.RefreshResult.ErrorCode, RequestId = opResult.RefreshResult.RequestId };
        }

        if (opResult.OperationException is { } ex)
        {
            var kind = ex.ErrorCode switch
            {
                ManagedErrorCode.ClientInstanceRebindRequired => InstanceResultKind.RebindRequired,
                ManagedErrorCode.ClientInstanceLimitReached => InstanceResultKind.LimitReached,
                ManagedErrorCode.ClientInstanceRevoked => InstanceResultKind.Revoked,
                ManagedErrorCode.ClientAccountInactive => InstanceResultKind.AccountDisabled,
                ManagedErrorCode.MandatoryUpdateRequired => InstanceResultKind.MandatoryUpdate,
                _ => InstanceResultKind.UnknownError,
            };
            return new InstanceResult { Kind = kind, ErrorCode = ex.ErrorCode, RequestId = ex.RequestId };
        }

        return new InstanceResult { Kind = InstanceResultKind.UnknownError, ErrorCode = "missing_access_token" };
    }

    public async Task<HeartbeatResult> HeartbeatAsync(
        string clientVersion,
        Dictionary<string, string> coreVersions,
        ClientCapabilities capabilities,
        string? effectiveProfileId,
        bool sessionActive,
        CancellationToken ct = default)
    {
        var instanceId = await GetInstanceIdAsync();
        var instanceCredential = await GetInstanceCredentialAsync();
        if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(instanceCredential))
        {
            return new HeartbeatResult { Kind = HeartbeatResultKind.UnknownError, ErrorCode = "missing_instance_identity" };
        }

        try
        {
            var request = new HeartbeatRequest
            {
                ClientVersion = clientVersion,
                EngineVersion = GetEngineVersion(coreVersions),
                Capabilities = capabilities,
            };

            var result = await _api.PostAsync<HeartbeatResponse>($"/client/runtime/instances/{instanceId}/heartbeat", request, instanceCredential: instanceCredential, ct: ct);

            var control = result.Control;
            if (control.InstanceRevoked)
            {
                await ClearInstanceAsync();
                return new HeartbeatResult { Kind = HeartbeatResultKind.Revoked, Control = control };
            }
            if (control.AccountDisabled)
            {
                return new HeartbeatResult { Kind = HeartbeatResultKind.AccountDisabled, Control = control };
            }
            if (control.EmergencyStop)
            {
                return new HeartbeatResult { Kind = HeartbeatResultKind.EmergencyStop, Control = control };
            }
            if (control.MandatoryUpdate)
            {
                return new HeartbeatResult { Kind = HeartbeatResultKind.MandatoryUpdate, Control = control };
            }

            return new HeartbeatResult { Kind = HeartbeatResultKind.Success, Control = control };
        }
        catch (ManagedApiException ex) when (ex.HttpStatusCode == 401 || ex.HttpStatusCode == 403)
        {
            var kind = ex.ErrorCode switch
            {
                ManagedErrorCode.ClientInstanceRevoked => HeartbeatResultKind.Revoked,
                ManagedErrorCode.ClientAccountInactive => HeartbeatResultKind.AccountDisabled,
                ManagedErrorCode.InstanceCredentialScopeDenied => HeartbeatResultKind.CredentialScopeError,
                ManagedErrorCode.ManagedEmergencyStop => HeartbeatResultKind.EmergencyStop,
                ManagedErrorCode.MandatoryUpdateRequired => HeartbeatResultKind.MandatoryUpdate,
                _ => HeartbeatResultKind.UnknownError,
            };
            if (kind == HeartbeatResultKind.Revoked)
            {
                await ClearInstanceAsync();
            }
            return new HeartbeatResult { Kind = kind, ErrorCode = ex.ErrorCode, RequestId = ex.RequestId };
        }
        catch (ManagedNetworkTimeoutException)
        {
            return new HeartbeatResult { Kind = HeartbeatResultKind.NetworkError };
        }
        catch (Exception ex)
        {
            await LogAsync($"Heartbeat unexpected error: {ex.GetType().Name}");
            return new HeartbeatResult { Kind = HeartbeatResultKind.UnknownError };
        }
    }

    private static string GetEngineVersion(IReadOnlyDictionary<string, string> coreVersions)
    {
        if (coreVersions.TryGetValue("sing-box", out var singBoxVersion))
        {
            return singBoxVersion;
        }
        if (coreVersions.TryGetValue("xray", out var xrayVersion))
        {
            return xrayVersion;
        }
        return string.Empty;
    }

    public async Task<InstanceResult> RotateCredentialAsync(CancellationToken ct = default)
    {
        var accessToken = await _auth.GetAccessTokenAsync();
        var instanceId = await GetInstanceIdAsync();
        var instanceCredential = await GetInstanceCredentialAsync();
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(instanceCredential))
        {
            return new InstanceResult { Kind = InstanceResultKind.UnknownError, ErrorCode = "missing_identity" };
        }

        try
        {
            var result = await _api.PostAsync<CredentialRotateResponse>(
                $"/client/instances/{instanceId}/credentials/rotate",
                null,
                accessToken: accessToken,
                instanceCredential: instanceCredential,
                ct: ct);

            await _vault.StoreAsync(CredentialVaultEntry.InstanceCredential, result.InstanceCredential.Credential);
            return new InstanceResult { Kind = InstanceResultKind.Success };
        }
        catch (ManagedApiException ex)
        {
            var kind = ex.ErrorCode switch
            {
                ManagedErrorCode.ClientInstanceRevoked => InstanceResultKind.Revoked,
                ManagedErrorCode.ClientAccountInactive => InstanceResultKind.AccountDisabled,
                ManagedErrorCode.MandatoryUpdateRequired => InstanceResultKind.MandatoryUpdate,
                _ => InstanceResultKind.UnknownError,
            };
            if (kind == InstanceResultKind.Revoked)
            {
                await ClearInstanceAsync();
            }
            return new InstanceResult { Kind = kind, ErrorCode = ex.ErrorCode, RequestId = ex.RequestId };
        }
        catch (ManagedNetworkTimeoutException)
        {
            return new InstanceResult { Kind = InstanceResultKind.NetworkError };
        }
        catch (Exception ex)
        {
            await LogAsync($"Rotate unexpected error: {ex.GetType().Name}");
            return new InstanceResult { Kind = InstanceResultKind.UnknownError };
        }
    }

    public async Task<string?> GetInstanceIdAsync()
    {
        return await _vault.RetrieveAsync(CredentialVaultEntry.InstanceMetadata);
    }

    public async Task<string?> GetInstanceCredentialAsync()
    {
        return await _vault.RetrieveAsync(CredentialVaultEntry.InstanceCredential);
    }

    private async Task ClearInstanceAsync()
    {
        await _vault.DeleteAsync(CredentialVaultEntry.InstanceCredential);
        await _vault.DeleteAsync(CredentialVaultEntry.InstanceMetadata);
    }

    private async Task LogAsync(string message)
    {
        if (_logAsync != null)
        {
            await _logAsync(message);
        }
    }
}
