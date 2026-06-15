using NetAccel.Managed.Api;
using NetAccel.Managed.Auth;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Vault;

namespace NetAccel.Managed.Selection;

public enum SelectionResultKind
{
    Success,
    NoAssignment,
    PlanUnavailable,
    RevisionConflict,
    PlanNotAssigned,
    Revoked,
    AccountDisabled,
    NetworkError,
    UnknownError,
}

public sealed class SelectionResult
{
    public SelectionResultKind Kind { get; init; }
    public ManagedSelectionResponse? State { get; init; }
    public string? ErrorCode { get; init; }
    public string? RequestId { get; init; }
    public bool IsSuccess => Kind == SelectionResultKind.Success;
}

public enum PolicyStatusResultKind
{
    Success,
    Revoked,
    AccountDisabled,
    NetworkError,
    UnknownError,
}

public sealed class PolicyStatusResult<T>
{
    public PolicyStatusResultKind Kind { get; init; }
    public T? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? RequestId { get; init; }
}

public interface IManagedSelectionService
{
    Task<PolicyStatusResult<ManagedPolicy>> GetPolicyAsync(CancellationToken ct = default);
    Task<PolicyStatusResult<ManagedStatus>> GetStatusAsync(CancellationToken ct = default);
    Task<SelectionResult> UpdateSelectionAsync(string mode, string? profileId, int expectedSelectionRevision, CancellationToken ct = default);
}

public sealed class ManagedSelectionService : IManagedSelectionService
{
    private readonly ManagedApiClient _api;
    private readonly ICredentialVault _vault;
    private readonly IAuthService _auth;
    private readonly Func<string, Task>? _logAsync;

    public ManagedSelectionService(ManagedApiClient api, ICredentialVault vault, IAuthService auth, Func<string, Task>? logAsync = null)
    {
        _api = api;
        _vault = vault;
        _auth = auth;
        _logAsync = logAsync;
    }

    public async Task<PolicyStatusResult<ManagedPolicy>> GetPolicyAsync(CancellationToken ct = default)
    {
        return await GetDualIdentityAsync<ManagedPolicy>("/client/managed/policy", ct);
    }

    public async Task<PolicyStatusResult<ManagedStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        return await GetDualIdentityAsync<ManagedStatus>("/client/managed/status", ct);
    }

    private async Task<PolicyStatusResult<T>> GetDualIdentityAsync<T>(string path, CancellationToken ct)
    {
        var instanceCredential = await _vault.RetrieveAsync(CredentialVaultEntry.InstanceCredential);
        if (string.IsNullOrEmpty(instanceCredential))
        {
            return new PolicyStatusResult<T> { Kind = PolicyStatusResultKind.UnknownError, ErrorCode = "missing_identity" };
        }

        var opResult = await _auth.ExecuteWithRefreshAsync(
            async (token, ctInner) => await _api.GetAsync<T>(path, accessToken: token, instanceCredential: instanceCredential, ct: ctInner),
            ct);

        if (opResult.IsSuccess)
        {
            return new PolicyStatusResult<T> { Kind = PolicyStatusResultKind.Success, Data = opResult.Value };
        }

        if (opResult.RefreshResult != null)
        {
            var kind = opResult.RefreshResult.Kind switch
            {
                AuthResultKind.AccountDisabled => PolicyStatusResultKind.AccountDisabled,
                AuthResultKind.NetworkError => PolicyStatusResultKind.NetworkError,
                _ => PolicyStatusResultKind.UnknownError,
            };
            return new PolicyStatusResult<T> { Kind = kind, ErrorCode = opResult.RefreshResult.ErrorCode, RequestId = opResult.RefreshResult.RequestId };
        }

        if (opResult.OperationException is { } ex)
        {
            var kind = ex.ErrorCode switch
            {
                ManagedErrorCode.ClientInstanceRevoked => PolicyStatusResultKind.Revoked,
                ManagedErrorCode.ClientAccountInactive => PolicyStatusResultKind.AccountDisabled,
                _ => PolicyStatusResultKind.UnknownError,
            };
            return new PolicyStatusResult<T> { Kind = kind, ErrorCode = ex.ErrorCode, RequestId = ex.RequestId };
        }

        return new PolicyStatusResult<T> { Kind = PolicyStatusResultKind.UnknownError, ErrorCode = "missing_identity" };
    }

    public async Task<SelectionResult> UpdateSelectionAsync(string mode, string? profileId, int expectedSelectionRevision, CancellationToken ct = default)
    {
        var instanceCredential = await _vault.RetrieveAsync(CredentialVaultEntry.InstanceCredential);
        if (string.IsNullOrEmpty(instanceCredential))
        {
            return new SelectionResult { Kind = SelectionResultKind.UnknownError, ErrorCode = "missing_identity" };
        }

        var request = new ManagedSelectionRequest
        {
            Mode = mode,
            ProfileId = profileId,
            ExpectedSelectionRevision = expectedSelectionRevision,
        };

        var opResult = await _auth.ExecuteWithRefreshAsync(
            async (token, ctInner) => await _api.PutAsync<ManagedSelectionResponse>("/client/managed/selection", request, accessToken: token, instanceCredential: instanceCredential, ct: ctInner),
            ct);

        if (opResult.IsSuccess)
        {
            return new SelectionResult { Kind = SelectionResultKind.Success, State = opResult.Value };
        }

        if (opResult.RefreshResult != null)
        {
            var kind = opResult.RefreshResult.Kind switch
            {
                AuthResultKind.AccountDisabled => SelectionResultKind.AccountDisabled,
                AuthResultKind.NetworkError => SelectionResultKind.NetworkError,
                _ => SelectionResultKind.UnknownError,
            };
            return new SelectionResult { Kind = kind, ErrorCode = opResult.RefreshResult.ErrorCode, RequestId = opResult.RefreshResult.RequestId };
        }

        if (opResult.OperationException is { } ex)
        {
            var kind = ex.ErrorCode switch
            {
                ManagedErrorCode.ManagedSelectionRevisionConflict => SelectionResultKind.RevisionConflict,
                ManagedErrorCode.ManagedSelectionPlanNotAssigned => SelectionResultKind.PlanNotAssigned,
                ManagedErrorCode.ManagedSelectionPlanUnavailable => SelectionResultKind.PlanUnavailable,
                ManagedErrorCode.ManagedProfileNotAssigned => SelectionResultKind.PlanNotAssigned,
                ManagedErrorCode.ManagedProfileMaintenance => SelectionResultKind.PlanUnavailable,
                ManagedErrorCode.ManagedProfileCapabilityIncompatible => SelectionResultKind.PlanUnavailable,
                ManagedErrorCode.ClientInstanceRevoked => SelectionResultKind.Revoked,
                ManagedErrorCode.ClientAccountInactive => SelectionResultKind.AccountDisabled,
                _ => SelectionResultKind.UnknownError,
            };
            return new SelectionResult { Kind = kind, ErrorCode = ex.ErrorCode, RequestId = ex.RequestId };
        }

        return new SelectionResult { Kind = SelectionResultKind.UnknownError, ErrorCode = "missing_identity" };
    }

    private async Task LogAsync(string message)
    {
        if (_logAsync != null)
        {
            await _logAsync(message);
        }
    }
}
