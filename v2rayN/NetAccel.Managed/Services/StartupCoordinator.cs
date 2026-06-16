using NetAccel.Managed.Api;
using NetAccel.Managed.Crypto;
using NetAccel.Managed.Dtos;
using NetAccel.Managed.Identity;
using NetAccel.Managed.Storage;

namespace NetAccel.Managed.Services;

public enum StartupState
{
    NeedsLogin,
    Ready,
    NoAssignment,
    InstanceRevoked,
    AccountDisabled,
    MandatoryUpdate,
    Offline,
    Faulted
}

public sealed class StartupResult
{
    public StartupState State { get; }
    public string? ErrorCode { get; }
    public ManagedPolicy? Policy { get; }
    public ManagedStatus? Status { get; }
    public ConfigSyncResult? ConfigResult { get; }

    public StartupResult(StartupState state, string? errorCode = null, ManagedPolicy? policy = null, ManagedStatus? status = null, ConfigSyncResult? configResult = null)
    {
        State = state;
        ErrorCode = errorCode;
        Policy = policy;
        Status = status;
        ConfigResult = configResult;
    }
}

public interface IStartupCoordinator
{
    Task<StartupResult> RunAsync(CancellationToken ct = default);
}

public sealed class StartupCoordinator : IStartupCoordinator
{
    private readonly IAuthService _authService;
    private readonly IInstanceService _instanceService;
    private readonly IManagedSelectionService _selectionService;
    private readonly IManagedConfigSyncService _configSyncService;
    private readonly IManagedApiClient _apiClient;

    public StartupCoordinator(
        IAuthService authService,
        IInstanceService instanceService,
        IManagedSelectionService selectionService,
        IManagedConfigSyncService configSyncService,
        IManagedApiClient apiClient)
    {
        _authService = authService;
        _instanceService = instanceService;
        _selectionService = selectionService;
        _configSyncService = configSyncService;
        _apiClient = apiClient;
    }

    public async Task<StartupResult> RunAsync(CancellationToken ct = default)
    {
        try
        {
            if (!await _authService.IsAuthenticatedAsync(ct))
            {
                return new StartupResult(StartupState.NeedsLogin);
            }

            if (!await _instanceService.EnsureRegisteredAsync(ct))
            {
                return new StartupResult(StartupState.Faulted, "instance_register_failed");
            }

            var heartbeat = await _instanceService.HeartbeatAsync(ct);
            if (heartbeat != null)
            {
                var control = heartbeat.Control;
                if (control.InstanceRevoked)
                    return new StartupResult(StartupState.InstanceRevoked, "client_instance_revoked");
                if (control.AccountDisabled)
                    return new StartupResult(StartupState.AccountDisabled, "client_account_inactive");
                if (control.EmergencyStop)
                    return new StartupResult(StartupState.Faulted, "managed_emergency_stop");
                if (control.MandatoryUpdate)
                    return new StartupResult(StartupState.MandatoryUpdate, "mandatory_update_required");
            }

            var policy = await _selectionService.GetPolicyAsync(ct);
            if (policy?.MandatoryUpdate == true)
                return new StartupResult(StartupState.MandatoryUpdate, "mandatory_update_required");

            var status = await _selectionService.GetStatusAsync(ct);
            if (status == null)
                return new StartupResult(StartupState.Offline, "assignment_fetch_failed");

            if (status.Profiles.Count == 0)
                return new StartupResult(StartupState.NoAssignment);

            var configResult = await _configSyncService.FetchConfigAsync(status.AssignmentRevision, ct);

            return new StartupResult(StartupState.Ready, policy: policy, status: status, configResult: configResult);
        }
        catch (ManagedApiError ex)
        {
            return new StartupResult(StartupState.Faulted, ex.ErrorCode);
        }
        catch (TimeoutException)
        {
            return new StartupResult(StartupState.Offline, "network_timeout");
        }
        catch (Exception)
        {
            return new StartupResult(StartupState.Faulted, "unknown_error");
        }
    }
}
