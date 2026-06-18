using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Runtime;
using NetAccel.Managed.Selection;

namespace NetAccel.Managed.Presentation;

public delegate Task<ManagedConnectionResult> ManagedRouteSwitcher(
    ManagedConfigPayload payload,
    ManagedProfileSelectionMode selectionMode,
    string? profileId,
    bool persistSelection,
    CancellationToken ct);

public sealed record ManagedRouteItem
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string Region { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public bool IsRecommended { get; init; }
    public bool IsSelected { get; init; }
    public bool IsSelectable { get; init; }
}

public sealed class ManagedRoutesViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IManagedSelectionService _selection;
    private readonly IManagedConnectionCoordinator _connection;
    private readonly Func<Task<ManagedConfigPayload?>> _configProvider;
    private readonly ManagedRouteSwitcher? _switcher;
    private readonly Action<ManagedProfileSelectionMode, string?, int>? _selectionChanged;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private ManagedConfigPayload? _payload;
    private bool _isBusy;
    private bool _canChangeSelection;
    private bool _automaticSelected = true;
    private string _message = "正在检查已分配线路。";
    private int _selectionRevision;
    private string? _selectedProfileId;
    private bool _disposed;

    public ManagedRoutesViewModel(
        IManagedSelectionService selection,
        IManagedConnectionCoordinator connection,
        Func<Task<ManagedConfigPayload?>> configProvider,
        ManagedRouteSwitcher? switcher = null,
        Action<ManagedProfileSelectionMode, string?, int>? selectionChanged = null)
    {
        _selection = selection;
        _connection = connection;
        _configProvider = configProvider;
        _switcher = switcher;
        _selectionChanged = selectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ManagedRouteItem> Routes { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsSelectionEnabled));
            }
        }
    }

    public bool CanChangeSelection
    {
        get => _canChangeSelection;
        private set
        {
            if (SetProperty(ref _canChangeSelection, value))
            {
                OnPropertyChanged(nameof(IsSelectionEnabled));
            }
        }
    }

    public bool IsSelectionEnabled => CanChangeSelection && !IsBusy;

    public bool AutomaticSelected
    {
        get => _automaticSelected;
        private set => SetProperty(ref _automaticSelected, value);
    }

    public bool HasRoutes => Routes.Count > 0;

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await _operationGate.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            IsBusy = true;
            await LoadAsync(ct);
        }
        finally
        {
            IsBusy = false;
            _operationGate.Release();
        }
    }

    public async Task SelectAutomaticAsync(CancellationToken ct = default)
        => await SelectAsync(null, ct);

    public async Task SelectRouteAsync(string profileId, CancellationToken ct = default)
        => await SelectAsync(profileId, ct);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _operationGate.Dispose();
    }

    private async Task SelectAsync(string? profileId, CancellationToken ct)
    {
        if (!await _operationGate.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            IsBusy = true;
            if (!CanChangeSelection || _payload == null)
            {
                Message = "暂时无法更改线路，请重新检查。";
                return;
            }

            if (profileId != null && _payload.ClientPolicy?.AllowManualSelection != true)
            {
                Message = "管理员当前未开放手动选线。";
                return;
            }

            if (profileId != null)
            {
                var route = Routes.FirstOrDefault(item => item.Id == profileId);
                if (route is not { IsSelectable: true })
                {
                    Message = "这条线路暂时不可用，请选择其他线路。";
                    return;
                }
            }

            var mode = profileId == null
                ? ManagedProfileSelectionMode.Automatic
                : ManagedProfileSelectionMode.Manual;

            if (_connection.Status.State == ManagedConnectionState.Connected)
            {
                if (_switcher == null)
                {
                    Message = "当前连接暂时无法切换线路。";
                    return;
                }

                _payload.SelectionRevision = _selectionRevision;
                var switched = await _switcher(_payload, mode, profileId, true, ct);
                if (!switched.Success)
                {
                    Message = FriendlyConnectionFailure(switched.FailureKind);
                    return;
                }

                await LoadAsync(ct);
                return;
            }

            var result = await _selection.UpdateSelectionAsync(
                mode == ManagedProfileSelectionMode.Automatic ? "automatic" : "manual",
                profileId,
                _selectionRevision,
                ct);

            if (!result.IsSuccess || result.State == null)
            {
                Message = FriendlySelectionFailure(result.Kind);
                if (result.Kind == SelectionResultKind.RevisionConflict)
                {
                    await LoadAsync(ct);
                }
                return;
            }

            ApplySelection(result.State.SelectionMode, result.State.SelectedProfileId, result.State.SelectionRevision);
            Message = mode == ManagedProfileSelectionMode.Automatic
                ? "已启用智能选择。"
                : "已保存当前设备的线路选择。";
        }
        catch (OperationCanceledException)
        {
            Message = "线路切换已取消。";
        }
        catch
        {
            Message = "暂时无法更改线路，请稍后重试。";
        }
        finally
        {
            IsBusy = false;
            _operationGate.Release();
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        _payload = await _configProvider();
        var statusResult = await _selection.GetStatusAsync(ct);

        if (statusResult.Kind == PolicyStatusResultKind.Success && statusResult.Data != null)
        {
            var status = statusResult.Data;
            var assignmentIsCurrent = _payload != null
                && _payload.AssignmentRevision == status.AssignmentRevision;
            _selectionRevision = status.SelectionRevision;
            _selectedProfileId = status.SelectedProfileId;
            CanChangeSelection = !status.InstanceRevoked
                && !status.AccountDisabled
                && !status.EmergencyStop
                && !status.MandatoryUpdate
                && assignmentIsCurrent;
            AutomaticSelected = !status.SelectionMode.Equals("manual", StringComparison.OrdinalIgnoreCase);

            Routes.Clear();
            foreach (var profile in status.Profiles)
            {
                Routes.Add(MapStatusProfile(profile, status, _payload, assignmentIsCurrent));
            }

            OnPropertyChanged(nameof(HasRoutes));
            NotifySelectionChanged();
            Message = !assignmentIsCurrent
                ? "线路配置正在更新，请重新检查。"
                : _payload.ClientPolicy?.AllowManualSelection != true
                    ? "管理员已启用智能选择，当前不可手动切换。"
                    : Routes.Count == 0
                        ? "当前设备还没有分配线路。"
                        : "这里只显示管理员已分配给当前设备的线路。";
            return;
        }

        CanChangeSelection = false;
        AutomaticSelected = true;
        _selectionRevision = _payload?.SelectionRevision ?? 0;
        _selectedProfileId = null;
        Routes.Clear();
        if (_payload != null)
        {
            foreach (var profile in _payload.Profiles)
            {
                Routes.Add(MapCachedProfile(profile));
            }
        }

        OnPropertyChanged(nameof(HasRoutes));
        Message = Routes.Count == 0
            ? "暂时无法获取线路，请重新检查。"
            : "当前离线，仅显示上次已同步线路；联网后才能更改。";
    }

    private void ApplySelection(string selectionMode, string? profileId, int revision)
    {
        _selectionRevision = revision;
        _selectedProfileId = profileId;
        if (_payload != null)
        {
            _payload.SelectionRevision = revision;
        }

        AutomaticSelected = !selectionMode.Equals("manual", StringComparison.OrdinalIgnoreCase);
        ReplaceSelectionFlags();
        NotifySelectionChanged();
    }

    private void ReplaceSelectionFlags()
    {
        for (var index = 0; index < Routes.Count; index++)
        {
            var route = Routes[index];
            Routes[index] = route with
            {
                IsSelected = !AutomaticSelected && route.Id == _selectedProfileId,
            };
        }
    }

    private void NotifySelectionChanged()
    {
        _selectionChanged?.Invoke(
            AutomaticSelected ? ManagedProfileSelectionMode.Automatic : ManagedProfileSelectionMode.Manual,
            AutomaticSelected ? null : _selectedProfileId,
            _selectionRevision);
    }

    private static ManagedRouteItem MapStatusProfile(
        ProfileSummary profile,
        ManagedStatus status,
        ManagedConfigPayload? payload,
        bool assignmentIsCurrent)
    {
        var payloadProfile = payload?.Profiles.FirstOrDefault(item => item.Id == profile.Id);
        var capabilityCompatible = !profile.CapabilityStatus.Equals("incompatible", StringComparison.OrdinalIgnoreCase);
        var selectable = assignmentIsCurrent
            && payload?.ClientPolicy?.AllowManualSelection == true
            && payloadProfile is { Available: true }
            && profile.Available && !profile.Maintenance && capabilityCompatible;
        return new ManagedRouteItem
        {
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            Region = FriendlyRegion(profile.Region),
            StatusText = FriendlyStatus(profile, status.EffectiveProfileId),
            IsRecommended = profile.Recommended || profile.Id == status.RecommendedProfileId,
            IsSelected = status.SelectionMode.Equals("manual", StringComparison.OrdinalIgnoreCase)
                && profile.Id == status.SelectedProfileId,
            IsSelectable = selectable,
        };
    }

    private static ManagedRouteItem MapCachedProfile(ManagedProfile profile)
        => new()
        {
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            Region = FriendlyRegion(profile.Region),
            StatusText = profile.Available ? "上次已同步" : "暂不可用",
            IsRecommended = profile.Recommended,
            IsSelected = false,
            IsSelectable = false,
        };

    private static string FriendlyStatus(ProfileSummary profile, string? effectiveProfileId)
    {
        if (profile.Maintenance)
        {
            return "维护中";
        }

        if (!profile.Available || profile.CapabilityStatus.Equals("incompatible", StringComparison.OrdinalIgnoreCase))
        {
            return "暂不可用";
        }

        if (profile.Id == effectiveProfileId)
        {
            return "当前使用";
        }

        if (profile.Quality.LatencyMs.HasValue)
        {
            return $"{Math.Round(profile.Quality.LatencyMs.Value)} ms";
        }

        return profile.Recommended ? "推荐" : "可用";
    }

    private static string FriendlyRegion(string? region)
        => string.IsNullOrWhiteSpace(region) ? "已分配线路" : region.Trim().ToUpperInvariant();

    private static string FriendlySelectionFailure(SelectionResultKind kind)
        => kind switch
        {
            SelectionResultKind.RevisionConflict => "线路状态已经更新，请重新选择。",
            SelectionResultKind.PlanNotAssigned => "这条线路已不在当前设备的分配范围内。",
            SelectionResultKind.PlanUnavailable => "这条线路暂时不可用，请选择其他线路。",
            SelectionResultKind.Revoked => "当前设备已停用。",
            SelectionResultKind.AccountDisabled => "当前账号已停用。",
            SelectionResultKind.NetworkError => "暂时无法连接服务，请检查网络。",
            _ => "暂时无法保存线路选择，请稍后重试。",
        };

    private static string FriendlyConnectionFailure(ManagedConnectionFailureKind kind)
        => kind switch
        {
            ManagedConnectionFailureKind.ProfileUnavailable => "这条线路暂时不可用，已恢复原连接。",
            ManagedConnectionFailureKind.ProfileNotAssigned => "这条线路已不在当前设备的分配范围内。",
            ManagedConnectionFailureKind.SelectionRejected => "线路状态已经更新，已恢复原连接。",
            ManagedConnectionFailureKind.RestoreFailed => "原连接没有恢复，请先停止加速并打开诊断。",
            _ => "线路切换没有完成，已尽量恢复原连接。",
        };

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
