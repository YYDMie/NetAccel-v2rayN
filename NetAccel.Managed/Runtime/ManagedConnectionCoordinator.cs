using NetAccel.Managed;
using NetAccel.Managed.Api;
using NetAccel.Managed.Domain;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Selection;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Handler.SysProxy;
using ServiceLib.Manager;
using ServiceLib.Models.Configs;

namespace NetAccel.Managed.Runtime;

public enum ManagedConnectionState
{
    Ready,
    Starting,
    Connected,
    Stopping,
    Faulted,
}

public enum ManagedConnectionMode
{
    SystemProxy,
    Tun,
}

public enum ManagedProfileSelectionMode
{
    Automatic,
    Manual,
}

public enum ManagedConnectionFailureKind
{
    None,
    Busy,
    AlreadyConnected,
    NotConnected,
    OwnerConflict,
    NoConfig,
    NoAssignment,
    ProfileNotAssigned,
    ProfileUnavailable,
    CapabilityIncompatible,
    PolicyDenied,
    CoreStartFailed,
    SelectionRejected,
    RestoreFailed,
    Cancelled,
    Unknown,
}

public sealed record ManagedConnectionStartRequest
{
    public required ManagedConfigPayload Payload { get; init; }
    public ManagedConnectionMode NetworkMode { get; init; } = ManagedConnectionMode.SystemProxy;
    public ManagedProfileSelectionMode SelectionMode { get; init; } = ManagedProfileSelectionMode.Automatic;
    public string? ProfileId { get; init; }
    public bool PersistSelection { get; init; }
    public bool AllowAutomaticFallback { get; init; } = true;
}

public sealed record ManagedConnectionStatus
{
    public ManagedConnectionState State { get; init; } = ManagedConnectionState.Ready;
    public ManagedConnectionMode? NetworkMode { get; init; }
    public ManagedProfileSelectionMode? SelectionMode { get; init; }
    public string? PreferredProfileId { get; init; }
    public string? EffectiveProfileId { get; init; }
    public bool IsFallback { get; init; }
    public ManagedConnectionFailureKind FailureKind { get; init; } = ManagedConnectionFailureKind.None;
    public string? Message { get; init; }
}

public sealed record ManagedConnectionResult
{
    public bool Success { get; init; }
    public ManagedConnectionStatus Status { get; init; } = new();
    public ManagedConnectionFailureKind FailureKind { get; init; } = ManagedConnectionFailureKind.None;
    public string? Message { get; init; }

    public static ManagedConnectionResult FromStatus(ManagedConnectionStatus status)
        => new()
        {
            Success = status.State == ManagedConnectionState.Connected || status.State == ManagedConnectionState.Ready,
            Status = status,
            FailureKind = status.FailureKind,
            Message = status.Message,
        };

    public static ManagedConnectionResult Failed(ManagedConnectionStatus status, ManagedConnectionFailureKind kind, string message)
        => new()
        {
            Success = false,
            Status = status with { FailureKind = kind, Message = message },
            FailureKind = kind,
            Message = message,
        };
}

public interface IManagedCoreRunner
{
    bool IsRunning { get; }
    Task StartAsync(ManagedRuntimeConfig runtimeConfig, ManagedConnectionMode mode, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

public sealed class ServiceLibManagedCoreRunner : IManagedCoreRunner, IManagedDiagnosticsRuntime
{
    private readonly Func<bool, string, Task> _updateFunc;
    private IDisposable? _runtimeConfigScope;
    private Config? _activeConfig;
    private ProxySettingWindows.WindowsProxySnapshot? _windowsProxySnapshot;
    private bool _managedSystemProxyApplied;
    private bool _managedTunApplied;

    public ServiceLibManagedCoreRunner(Func<bool, string, Task>? updateFunc = null)
    {
        _updateFunc = updateFunc ?? ((_, _) => Task.CompletedTask);
    }

    public bool IsRunning => CoreManager.Instance.IsCoreRunning;

    public async Task StartAsync(ManagedRuntimeConfig runtimeConfig, ManagedConnectionMode mode, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await StopAsync(ct);

        var effectiveRuntime = runtimeConfig with { EnableTun = mode == ManagedConnectionMode.Tun };
        var context = effectiveRuntime.ToCoreConfigContext();
        context.AppConfig.SystemProxyItem.SysProxyType = mode == ManagedConnectionMode.SystemProxy
            ? ESysProxyType.ForcedChange
            : ESysProxyType.ForcedClear;
        context.AppConfig.TunModeItem.EnableTun = mode == ManagedConnectionMode.Tun;

        _runtimeConfigScope = AppManager.Instance.PushRuntimeConfigOverride(context.AppConfig);
        _activeConfig = context.AppConfig;

        try
        {
            await CoreManager.Instance.Init(context.AppConfig, _updateFunc);
            await CoreManager.Instance.LoadCore(context, null);
            if (!CoreManager.Instance.IsCoreRunning)
            {
                throw new InvalidOperationException("Managed core did not stay running.");
            }

            if (mode == ManagedConnectionMode.SystemProxy)
            {
                if (OperatingSystem.IsWindows())
                {
                    _windowsProxySnapshot = ProxySettingWindows.CaptureSnapshot();
                }

                var proxySet = await SysProxyHandler.UpdateSysProxy(context.AppConfig, false);
                if (!proxySet)
                {
                    throw new InvalidOperationException("System proxy could not be applied.");
                }

                _managedSystemProxyApplied = true;
            }
            else
            {
                _managedTunApplied = true;
            }
        }
        catch
        {
            await StopAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        var activeConfig = _activeConfig;
        Exception? restoreError = null;

        if (activeConfig != null && _managedSystemProxyApplied)
        {
            try
            {
                await RestoreSystemProxySnapshotAsync(activeConfig);
                _managedSystemProxyApplied = false;
            }
            catch (Exception ex)
            {
                restoreError = ex;
            }
        }

        await CoreManager.Instance.CoreStop();

        if (activeConfig?.TunModeItem.EnableTun == true && _managedTunApplied)
        {
            await AppManager.Instance.RemoveTunDeviceAsync();
            _managedTunApplied = false;
        }

        ResetManagedRuntime();
        if (restoreError != null)
        {
            throw new InvalidOperationException("Managed system proxy snapshot could not be restored.", restoreError);
        }
    }

    public ManagedCoreRuntimeSnapshot Inspect()
        => new()
        {
            IsRunning = IsRunning,
            HasManagedRuntime = _activeConfig != null,
            HasManagedSystemProxy = _managedSystemProxyApplied,
            HasManagedTun = _managedTunApplied,
        };

    public Task ClearResidualCoreAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return CoreManager.Instance.CoreStop();
    }

    public async Task RestoreManagedSystemProxyAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_managedSystemProxyApplied || _activeConfig == null)
        {
            return;
        }

        await RestoreSystemProxySnapshotAsync(_activeConfig);
        _managedSystemProxyApplied = false;
        ResetManagedRuntimeIfClean();
    }

    public async Task ClearManagedTunStateAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_managedTunApplied)
        {
            return;
        }

        if (IsRunning)
        {
            throw new InvalidOperationException("Cannot clear managed TUN while the core is running.");
        }

        await AppManager.Instance.RemoveTunDeviceAsync();
        _managedTunApplied = false;
        ResetManagedRuntimeIfClean();
    }

    private void ResetManagedRuntimeIfClean()
    {
        if (!IsRunning && !_managedSystemProxyApplied && !_managedTunApplied)
        {
            ResetManagedRuntime();
        }
    }

    private void ResetManagedRuntime()
    {
        _activeConfig = null;
        _windowsProxySnapshot = null;
        _managedSystemProxyApplied = false;
        _managedTunApplied = false;
        _runtimeConfigScope?.Dispose();
        _runtimeConfigScope = null;
    }

    private async Task RestoreSystemProxySnapshotAsync(Config activeConfig)
    {
        if (OperatingSystem.IsWindows() && _windowsProxySnapshot != null)
        {
            ProxySettingWindows.RestoreSnapshot(_windowsProxySnapshot);
            return;
        }

        await SysProxyHandler.UpdateSysProxy(activeConfig, true);
    }
}

public interface IManagedConnectionCoordinator
{
    event Action<ManagedConnectionStatus>? StatusChanged;
    ManagedConnectionStatus Status { get; }
    Task<ManagedConnectionResult> StartAsync(ManagedConnectionStartRequest request, CancellationToken ct = default);
    Task<ManagedConnectionResult> StopAsync(CancellationToken ct = default);
}

public sealed class ManagedConnectionCoordinator : IManagedConnectionCoordinator
{
    private readonly ConnectionOwnershipCoordinator _ownership;
    private readonly IManagedCoreRunner _coreRunner;
    private readonly ManagedRuntimeConfigBuilder _runtimeBuilder;
    private readonly IManagedSelectionService? _selectionService;
    private readonly IManagedConfigService? _configService;
    private readonly string _clientVersion;
    private readonly Dictionary<string, string> _coreVersions;
    private readonly Func<string, Task>? _logAsync;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private ActiveConnection? _active;
    private ManagedConnectionStatus _status = new();

    public ManagedConnectionCoordinator(
        ConnectionOwnershipCoordinator ownership,
        IManagedCoreRunner coreRunner,
        ManagedRuntimeConfigBuilder? runtimeBuilder = null,
        IManagedSelectionService? selectionService = null,
        IManagedConfigService? configService = null,
        string clientVersion = "",
        Dictionary<string, string>? coreVersions = null,
        Func<string, Task>? logAsync = null)
    {
        _ownership = ownership;
        _coreRunner = coreRunner;
        _runtimeBuilder = runtimeBuilder ?? new ManagedRuntimeConfigBuilder();
        _selectionService = selectionService;
        _configService = configService;
        _clientVersion = clientVersion;
        _coreVersions = coreVersions ?? [];
        _logAsync = logAsync;

        ManagedExitCleanup.Register(_ownership, this);
    }

    public event Action<ManagedConnectionStatus>? StatusChanged;

    public ManagedConnectionStatus Status => _status;

    public async Task<ManagedConnectionResult> StartAsync(ManagedConnectionStartRequest request, CancellationToken ct = default)
    {
        if (!await _operationGate.WaitAsync(0, ct))
        {
            return Failure(_status, ManagedConnectionFailureKind.Busy, "managed_connection_busy");
        }

        ConnectionOwnershipLease? lease = null;
        try
        {
            if (_active != null)
            {
                return Failure(_status, ManagedConnectionFailureKind.AlreadyConnected, "managed_connection_already_connected");
            }

            SetStatus(new ManagedConnectionStatus
            {
                State = ManagedConnectionState.Starting,
                NetworkMode = request.NetworkMode,
                SelectionMode = request.SelectionMode,
                PreferredProfileId = request.ProfileId,
            });

            var owner = await _ownership.AcquireAsync(ConnectionOwner.Managed, ct);
            if (!owner.Acquired || owner.Lease == null)
            {
                SetStatus(new ManagedConnectionStatus
                {
                    State = ManagedConnectionState.Faulted,
                    FailureKind = ManagedConnectionFailureKind.OwnerConflict,
                    Message = owner.ConflictReason,
                });
                return Failure(_status, ManagedConnectionFailureKind.OwnerConflict, owner.ConflictReason ?? "owner_conflict");
            }

            lease = owner.Lease;
            var active = await StartWithFallbackAsync(request, lease, allowFallback: ShouldAllowFallback(request), ct);
            if (!active.Success || active.Connection == null)
            {
                await CleanupAfterFailedStartAsync(lease, ct);
                SetStatus(new ManagedConnectionStatus
                {
                    State = ManagedConnectionState.Faulted,
                    NetworkMode = request.NetworkMode,
                    SelectionMode = request.SelectionMode,
                    PreferredProfileId = request.ProfileId,
                    FailureKind = active.FailureKind,
                    Message = active.Message,
                });
                return Failure(_status, active.FailureKind, active.Message ?? "managed_connection_start_failed");
            }

            if (request.PersistSelection)
            {
                var persisted = await PersistSelectionAsync(request, active.Connection, ct);
                if (!persisted.Success)
                {
                    await CleanupAfterFailedStartAsync(lease, ct);
                    SetStatus(new ManagedConnectionStatus
                    {
                        State = ManagedConnectionState.Faulted,
                        NetworkMode = request.NetworkMode,
                        SelectionMode = request.SelectionMode,
                        PreferredProfileId = active.Connection.PreferredProfileId,
                        EffectiveProfileId = active.Connection.EffectiveProfileId,
                        IsFallback = active.Connection.IsFallback,
                        FailureKind = ManagedConnectionFailureKind.SelectionRejected,
                        Message = persisted.Message,
                    });
                    return Failure(_status, ManagedConnectionFailureKind.SelectionRejected, persisted.Message ?? "selection_rejected");
                }
            }

            await ReportAppliedAsync(active.Connection.Payload.AssignmentRevision, ct);

            _active = active.Connection;
            lease = null;
            SetConnectedStatus(_active);
            return ManagedConnectionResult.FromStatus(_status);
        }
        catch (OperationCanceledException)
        {
            if (lease != null)
            {
                await CleanupAfterFailedStartAsync(lease, CancellationToken.None);
            }
            SetStatus(new ManagedConnectionStatus
            {
                State = ManagedConnectionState.Faulted,
                FailureKind = ManagedConnectionFailureKind.Cancelled,
                Message = "cancelled",
            });
            return Failure(_status, ManagedConnectionFailureKind.Cancelled, "cancelled");
        }
        catch (Exception ex)
        {
            await LogAsync($"Managed start failed: {ex.GetType().Name}");
            if (lease != null)
            {
                await CleanupAfterFailedStartAsync(lease, CancellationToken.None);
            }
            SetStatus(new ManagedConnectionStatus
            {
                State = ManagedConnectionState.Faulted,
                FailureKind = ManagedConnectionFailureKind.Unknown,
                Message = ex.Message,
            });
            return Failure(_status, ManagedConnectionFailureKind.Unknown, ex.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ManagedConnectionResult> StopAsync(CancellationToken ct = default)
    {
        if (!await _operationGate.WaitAsync(0, ct))
        {
            return Failure(_status, ManagedConnectionFailureKind.Busy, "managed_connection_busy");
        }

        try
        {
            if (_active == null)
            {
                SetStatus(new ManagedConnectionStatus { State = ManagedConnectionState.Ready });
                return ManagedConnectionResult.FromStatus(_status);
            }

            SetStatus(_status with { State = ManagedConnectionState.Stopping });
            await StopActiveCoreAsync(CancellationToken.None);
            await _active.Lease.DisposeAsync();
            _active = null;
            SetStatus(new ManagedConnectionStatus { State = ManagedConnectionState.Ready });
            return ManagedConnectionResult.FromStatus(_status);
        }
        catch (Exception ex)
        {
            await LogAsync($"Managed stop failed: {ex.GetType().Name}");
            SetStatus(_status with
            {
                State = ManagedConnectionState.Faulted,
                FailureKind = ManagedConnectionFailureKind.Unknown,
                Message = ex.Message,
            });
            return Failure(_status, ManagedConnectionFailureKind.Unknown, ex.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ManagedConnectionResult> SwitchAsync(
        ManagedConfigPayload payload,
        ManagedProfileSelectionMode selectionMode,
        string? profileId,
        bool persistSelection = true,
        CancellationToken ct = default)
    {
        if (!await _operationGate.WaitAsync(0, ct))
        {
            return Failure(_status, ManagedConnectionFailureKind.Busy, "managed_connection_busy");
        }

        ActiveConnection? previous = null;
        try
        {
            if (_active == null)
            {
                return Failure(_status, ManagedConnectionFailureKind.NotConnected, "managed_connection_not_connected");
            }

            previous = _active;
            var request = new ManagedConnectionStartRequest
            {
                Payload = payload,
                NetworkMode = previous.NetworkMode,
                SelectionMode = selectionMode,
                ProfileId = profileId,
                PersistSelection = persistSelection,
                AllowAutomaticFallback = selectionMode == ManagedProfileSelectionMode.Automatic,
            };

            SetStatus(new ManagedConnectionStatus
            {
                State = ManagedConnectionState.Starting,
                NetworkMode = previous.NetworkMode,
                SelectionMode = selectionMode,
                PreferredProfileId = profileId,
            });

            await StopActiveCoreAsync(CancellationToken.None);

            var active = await StartWithFallbackAsync(request, previous.Lease, request.AllowAutomaticFallback && ShouldAllowFallback(request), ct);
            if (!active.Success || active.Connection == null)
            {
                var restored = await RestorePreviousAsync(previous, CancellationToken.None);
                if (!restored)
                {
                    await previous.Lease.DisposeAsync();
                    _active = null;
                    SetStatus(new ManagedConnectionStatus
                    {
                        State = ManagedConnectionState.Faulted,
                        FailureKind = ManagedConnectionFailureKind.RestoreFailed,
                        Message = "switch_restore_failed",
                    });
                    return Failure(_status, ManagedConnectionFailureKind.RestoreFailed, "switch_restore_failed");
                }

                _active = previous;
                SetConnectedStatus(previous, active.FailureKind, active.Message ?? "switch_failed_restored_previous");
                return Failure(_status, active.FailureKind, active.Message ?? "switch_failed_restored_previous");
            }

            if (persistSelection)
            {
                var persisted = await PersistSelectionAsync(request, active.Connection, ct);
                if (!persisted.Success)
                {
                    await StopActiveCoreAsync(CancellationToken.None);
                    var restored = await RestorePreviousAsync(previous, CancellationToken.None);
                    if (!restored)
                    {
                        await previous.Lease.DisposeAsync();
                        _active = null;
                        SetStatus(new ManagedConnectionStatus
                        {
                            State = ManagedConnectionState.Faulted,
                            FailureKind = ManagedConnectionFailureKind.RestoreFailed,
                            Message = "switch_restore_failed",
                        });
                        return Failure(_status, ManagedConnectionFailureKind.RestoreFailed, "switch_restore_failed");
                    }

                    _active = previous;
                    SetConnectedStatus(previous, ManagedConnectionFailureKind.SelectionRejected, persisted.Message ?? "selection_rejected");
                    return Failure(_status, ManagedConnectionFailureKind.SelectionRejected, persisted.Message ?? "selection_rejected");
                }
            }

            await ReportAppliedAsync(active.Connection.Payload.AssignmentRevision, ct);

            _active = active.Connection;
            SetConnectedStatus(_active);
            return ManagedConnectionResult.FromStatus(_status);
        }
        catch (OperationCanceledException)
        {
            if (previous != null)
            {
                await SafeStopCoreAsync();
                var restored = await RestorePreviousAsync(previous, CancellationToken.None);
                if (restored)
                {
                    _active = previous;
                    SetConnectedStatus(previous, ManagedConnectionFailureKind.Cancelled, "switch_cancelled_restored_previous");
                    return Failure(_status, ManagedConnectionFailureKind.Cancelled, "switch_cancelled_restored_previous");
                }

                await previous.Lease.DisposeAsync();
                _active = null;
                SetStatus(new ManagedConnectionStatus
                {
                    State = ManagedConnectionState.Faulted,
                    FailureKind = ManagedConnectionFailureKind.RestoreFailed,
                    Message = "switch_cancelled_restore_failed",
                });
                return Failure(_status, ManagedConnectionFailureKind.RestoreFailed, "switch_cancelled_restore_failed");
            }

            SetStatus(_status with
            {
                State = ManagedConnectionState.Faulted,
                FailureKind = ManagedConnectionFailureKind.Cancelled,
                Message = "cancelled",
            });
            return Failure(_status, ManagedConnectionFailureKind.Cancelled, "cancelled");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ManagedConnectionResult> RecoverFromCoreFaultAsync(string reason, CancellationToken ct = default)
    {
        if (!await _operationGate.WaitAsync(0, ct))
        {
            return Failure(_status, ManagedConnectionFailureKind.Busy, "managed_connection_busy");
        }

        try
        {
            if (_active == null)
            {
                return Failure(_status, ManagedConnectionFailureKind.NotConnected, "managed_connection_not_connected");
            }

            if (_coreRunner.IsRunning)
            {
                return ManagedConnectionResult.FromStatus(_status);
            }

            if (_active.Payload.ClientPolicy?.AllowAutomaticFailover != true)
            {
                SetStatus(_status with
                {
                    State = ManagedConnectionState.Faulted,
                    FailureKind = ManagedConnectionFailureKind.CoreStartFailed,
                    Message = reason,
                });
                return Failure(_status, ManagedConnectionFailureKind.CoreStartFailed, reason);
            }

            SetStatus(_status with { State = ManagedConnectionState.Starting, Message = reason });

            var request = new ManagedConnectionStartRequest
            {
                Payload = _active.Payload,
                NetworkMode = _active.NetworkMode,
                SelectionMode = _active.SelectionMode,
                ProfileId = _active.PreferredProfileId,
                PersistSelection = false,
                AllowAutomaticFallback = true,
            };

            await StopActiveCoreAsync(ct);
            var recovered = await StartFallbackOnlyAsync(request, _active.Lease, _active.EffectiveProfileId, ct);
            if (!recovered.Success || recovered.Connection == null)
            {
                await _active.Lease.DisposeAsync();
                _active = null;
                SetStatus(new ManagedConnectionStatus
                {
                    State = ManagedConnectionState.Faulted,
                    FailureKind = recovered.FailureKind,
                    Message = recovered.Message,
                });
                return Failure(_status, recovered.FailureKind, recovered.Message ?? "fallback_failed");
            }

            _active = recovered.Connection;
            SetConnectedStatus(_active);
            return ManagedConnectionResult.FromStatus(_status);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<StartActiveResult> StartWithFallbackAsync(
        ManagedConnectionStartRequest request,
        ConnectionOwnershipLease lease,
        bool allowFallback,
        CancellationToken ct)
    {
        var preferred = ResolvePreferredProfile(request.Payload, request.SelectionMode, request.ProfileId, request.NetworkMode);
        if (!preferred.Success || preferred.Profile == null)
        {
            return StartActiveResult.Failed(preferred.FailureKind, preferred.Message);
        }

        var first = await TryStartProfileAsync(request, preferred.Profile, preferred.Profile.Id, false, lease, ct);
        if (first.Success)
        {
            return first;
        }

        if (!allowFallback)
        {
            return first;
        }

        foreach (var fallback in EnumerateFallbackProfiles(request.Payload, request.NetworkMode, preferred.Profile.Id))
        {
            var attempt = await TryStartProfileAsync(request, fallback, preferred.Profile.Id, true, lease, ct);
            if (attempt.Success)
            {
                return attempt;
            }
        }

        return first;
    }

    private async Task<StartActiveResult> StartFallbackOnlyAsync(
        ManagedConnectionStartRequest request,
        ConnectionOwnershipLease lease,
        string? excludeProfileId,
        CancellationToken ct)
    {
        var preferred = ResolvePreferredProfile(request.Payload, request.SelectionMode, request.ProfileId, request.NetworkMode);
        var preferredId = preferred.Profile?.Id ?? request.ProfileId;
        foreach (var fallback in EnumerateFallbackProfiles(request.Payload, request.NetworkMode, excludeProfileId, preferredId))
        {
            var attempt = await TryStartProfileAsync(request, fallback, preferredId ?? fallback.Id, true, lease, ct);
            if (attempt.Success)
            {
                return attempt;
            }
        }

        return StartActiveResult.Failed(ManagedConnectionFailureKind.CoreStartFailed, "fallback_failed");
    }

    private async Task<StartActiveResult> TryStartProfileAsync(
        ManagedConnectionStartRequest request,
        ManagedProfile profile,
        string preferredProfileId,
        bool isFallback,
        ConnectionOwnershipLease lease,
        CancellationToken ct)
    {
        try
        {
            var runtime = _runtimeBuilder.Build(request.Payload, profile.Id);
            runtime = runtime with { EnableTun = request.NetworkMode == ManagedConnectionMode.Tun };
            await _coreRunner.StartAsync(runtime, request.NetworkMode, ct);

            return StartActiveResult.Started(new ActiveConnection(
                request.Payload,
                request.NetworkMode,
                request.SelectionMode,
                preferredProfileId,
                profile.Id,
                runtime,
                isFallback,
                lease));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await LogAsync($"Managed profile start failed: {profile.Id} {ex.GetType().Name}");
            await SafeStopCoreAsync();
            return StartActiveResult.Failed(ManagedConnectionFailureKind.CoreStartFailed, ex.Message);
        }
    }

    private ProfileResolveResult ResolvePreferredProfile(
        ManagedConfigPayload payload,
        ManagedProfileSelectionMode selectionMode,
        string? profileId,
        ManagedConnectionMode networkMode)
    {
        if (payload.Profiles.Count == 0)
        {
            return ProfileResolveResult.Failed(ManagedConnectionFailureKind.NoAssignment, "no_assignment");
        }

        if (selectionMode == ManagedProfileSelectionMode.Manual)
        {
            if (payload.ClientPolicy?.AllowManualSelection != true)
            {
                return ProfileResolveResult.Failed(ManagedConnectionFailureKind.PolicyDenied, "manual_selection_denied");
            }

            if (string.IsNullOrWhiteSpace(profileId))
            {
                return ProfileResolveResult.Failed(ManagedConnectionFailureKind.ProfileNotAssigned, "manual_profile_required");
            }

            var manual = payload.Profiles.FirstOrDefault(p => p.Id == profileId);
            return manual == null
                ? ProfileResolveResult.Failed(ManagedConnectionFailureKind.ProfileNotAssigned, "profile_not_assigned")
                : ValidateProfileForMode(manual, payload, networkMode);
        }

        var automatic = EnumerateAutomaticProfiles(payload)
            .Select(profile => ValidateProfileForMode(profile, payload, networkMode))
            .FirstOrDefault(result => result.Success);

        return automatic ?? ProfileResolveResult.Failed(ManagedConnectionFailureKind.NoAssignment, "no_available_profile");
    }

    private ProfileResolveResult ValidateProfileForMode(ManagedProfile profile, ManagedConfigPayload payload, ManagedConnectionMode networkMode)
    {
        if (!profile.Available)
        {
            return ProfileResolveResult.Failed(ManagedConnectionFailureKind.ProfileUnavailable, "profile_unavailable");
        }

        if (ManagedProfileAdapter.SelectCore(profile) == null)
        {
            return ProfileResolveResult.Failed(ManagedConnectionFailureKind.CapabilityIncompatible, "profile_capability_incompatible");
        }

        if (networkMode == ManagedConnectionMode.SystemProxy && !profile.Policy.AllowSystemProxy)
        {
            return ProfileResolveResult.Failed(ManagedConnectionFailureKind.PolicyDenied, "system_proxy_denied");
        }

        if (networkMode == ManagedConnectionMode.Tun && (payload.ClientPolicy?.AllowTun != true || !profile.Policy.AllowTun))
        {
            return ProfileResolveResult.Failed(ManagedConnectionFailureKind.PolicyDenied, "tun_denied");
        }

        return ProfileResolveResult.SuccessResult(profile);
    }

    private IEnumerable<ManagedProfile> EnumerateAutomaticProfiles(ManagedConfigPayload payload)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in new[] { payload.RecommendedProfileId }.Concat(payload.FallbackProfileIds))
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
            {
                continue;
            }

            var profile = payload.Profiles.FirstOrDefault(p => p.Id == id);
            if (profile != null)
            {
                yield return profile;
            }
        }

        foreach (var profile in payload.Profiles
            .Where(p => seen.Add(p.Id))
            .OrderByDescending(p => p.Recommended)
            .ThenByDescending(p => p.Priority))
        {
            yield return profile;
        }
    }

    private IEnumerable<ManagedProfile> EnumerateFallbackProfiles(
        ManagedConfigPayload payload,
        ManagedConnectionMode networkMode,
        params string?[] excludeProfileIds)
    {
        var excluded = excludeProfileIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in payload.FallbackProfileIds)
        {
            if (string.IsNullOrWhiteSpace(id) || excluded.Contains(id) || !seen.Add(id))
            {
                continue;
            }

            var profile = payload.Profiles.FirstOrDefault(p => p.Id == id);
            if (profile != null && ValidateProfileForMode(profile, payload, networkMode).Success)
            {
                yield return profile;
            }
        }

        foreach (var profile in payload.Profiles
            .Where(p => !excluded.Contains(p.Id) && seen.Add(p.Id))
            .OrderByDescending(p => p.Priority))
        {
            if (ValidateProfileForMode(profile, payload, networkMode).Success)
            {
                yield return profile;
            }
        }
    }

    private bool ShouldAllowFallback(ManagedConnectionStartRequest request)
        => request.AllowAutomaticFallback && request.Payload.ClientPolicy?.AllowAutomaticFailover == true;

    private async Task<PersistSelectionResult> PersistSelectionAsync(
        ManagedConnectionStartRequest request,
        ActiveConnection active,
        CancellationToken ct)
    {
        if (_selectionService == null)
        {
            return PersistSelectionResult.SuccessResult();
        }

        if (active.IsFallback && request.SelectionMode == ManagedProfileSelectionMode.Manual)
        {
            return PersistSelectionResult.SuccessResult();
        }

        var mode = request.SelectionMode == ManagedProfileSelectionMode.Manual ? "manual" : "automatic";
        var profileId = request.SelectionMode == ManagedProfileSelectionMode.Manual ? active.PreferredProfileId : null;
        var result = await _selectionService.UpdateSelectionAsync(mode, profileId, request.Payload.SelectionRevision, ct);

        return result.IsSuccess
            ? PersistSelectionResult.SuccessResult()
            : PersistSelectionResult.Failed(result.ErrorCode ?? result.Kind.ToString());
    }

    private async Task ReportAppliedAsync(int revision, CancellationToken ct)
    {
        if (_configService == null)
        {
            return;
        }

        try
        {
            await _configService.AckConfigAsync(revision, "applied", _clientVersion, _coreVersions, ct: ct);
        }
        catch (Exception ex)
        {
            await LogAsync($"Managed applied ACK failed: {ex.GetType().Name}");
        }
    }

    private async Task<bool> RestorePreviousAsync(ActiveConnection previous, CancellationToken ct)
    {
        try
        {
            await _coreRunner.StartAsync(previous.RuntimeConfig, previous.NetworkMode, ct);
            return true;
        }
        catch (Exception ex)
        {
            await LogAsync($"Managed restore failed: {ex.GetType().Name}");
            await SafeStopCoreAsync();
            return false;
        }
    }

    private async Task StopActiveCoreAsync(CancellationToken ct)
    {
        await _coreRunner.StopAsync(ct);
    }

    private async Task SafeStopCoreAsync()
    {
        try
        {
            await _coreRunner.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await LogAsync($"Managed safe stop failed: {ex.GetType().Name}");
        }
    }

    private async Task CleanupAfterFailedStartAsync(ConnectionOwnershipLease lease, CancellationToken ct)
    {
        await SafeStopCoreAsync();
        await lease.DisposeAsync();
    }

    private void SetConnectedStatus(
        ActiveConnection active,
        ManagedConnectionFailureKind failureKind = ManagedConnectionFailureKind.None,
        string? message = null)
    {
        SetStatus(new ManagedConnectionStatus
        {
            State = ManagedConnectionState.Connected,
            NetworkMode = active.NetworkMode,
            SelectionMode = active.SelectionMode,
            PreferredProfileId = active.PreferredProfileId,
            EffectiveProfileId = active.EffectiveProfileId,
            IsFallback = active.IsFallback,
            FailureKind = failureKind,
            Message = message,
        });
    }

    private void SetStatus(ManagedConnectionStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(status);
    }

    private ManagedConnectionResult Failure(ManagedConnectionStatus status, ManagedConnectionFailureKind kind, string message)
        => ManagedConnectionResult.Failed(status, kind, message);

    private async Task LogAsync(string message)
    {
        if (_logAsync != null)
        {
            await _logAsync(message);
        }
    }

    private sealed record ActiveConnection(
        ManagedConfigPayload Payload,
        ManagedConnectionMode NetworkMode,
        ManagedProfileSelectionMode SelectionMode,
        string PreferredProfileId,
        string EffectiveProfileId,
        ManagedRuntimeConfig RuntimeConfig,
        bool IsFallback,
        ConnectionOwnershipLease Lease);

    private sealed record StartActiveResult(
        bool Success,
        ActiveConnection? Connection,
        ManagedConnectionFailureKind FailureKind,
        string? Message)
    {
        public static StartActiveResult Started(ActiveConnection connection)
            => new(true, connection, ManagedConnectionFailureKind.None, null);

        public static StartActiveResult Failed(ManagedConnectionFailureKind kind, string? message)
            => new(false, null, kind, message);
    }

    private sealed record ProfileResolveResult(
        bool Success,
        ManagedProfile? Profile,
        ManagedConnectionFailureKind FailureKind,
        string Message)
    {
        public static ProfileResolveResult SuccessResult(ManagedProfile profile)
            => new(true, profile, ManagedConnectionFailureKind.None, string.Empty);

        public static ProfileResolveResult Failed(ManagedConnectionFailureKind kind, string message)
            => new(false, null, kind, message);
    }

    private sealed record PersistSelectionResult(bool Success, string? Message)
    {
        public static PersistSelectionResult SuccessResult() => new(true, null);
        public static PersistSelectionResult Failed(string message) => new(false, message);
    }
}
