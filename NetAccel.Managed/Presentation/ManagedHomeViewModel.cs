using System.ComponentModel;
using System.Runtime.CompilerServices;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Runtime;

namespace NetAccel.Managed.Presentation;

public enum ManagedHomeStage
{
    Loading,
    Idle,
    Starting,
    Connected,
    Stopping,
    Recovering,
    NoAssignment,
    NeedsPermission,
    ClassicRunning,
    Faulted,
}

public sealed class ManagedHomeViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IManagedConnectionCoordinator _connection;
    private readonly Func<Task<ManagedConfigPayload?>> _configProvider;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private ManagedConfigPayload? _payload;
    private ManagedHomeStage _stage = ManagedHomeStage.Loading;
    private ManagedConnectionMode _networkMode = ManagedConnectionMode.SystemProxy;
    private ManagedProfileSelectionMode _selectionMode = ManagedProfileSelectionMode.Automatic;
    private string? _selectedProfileId;
    private string _routeName = "智能通道";
    private string _detail = "正在检查当前设备状态。";
    private bool _disposed;

    public ManagedHomeViewModel(
        IManagedConnectionCoordinator connection,
        Func<Task<ManagedConfigPayload?>> configProvider)
    {
        _connection = connection;
        _configProvider = configProvider;
        _connection.StatusChanged += Connection_StatusChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ManagedHomeStage Stage
    {
        get => _stage;
        private set
        {
            if (!SetProperty(ref _stage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ActionText));
            OnPropertyChanged(nameof(IsActionEnabled));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(OrbState));
        }
    }

    public ManagedConnectionMode NetworkMode
    {
        get => _networkMode;
        set
        {
            if (IsConnected || IsBusy)
            {
                return;
            }

            if (SetProperty(ref _networkMode, value))
            {
                OnPropertyChanged(nameof(IsSystemProxyMode));
                OnPropertyChanged(nameof(IsTunMode));
                Detail = value == ManagedConnectionMode.SystemProxy
                    ? "适合日常使用，只调整常用网络访问。"
                    : "覆盖更完整的网络流量，可能需要系统授权。";
            }
        }
    }

    public bool IsSystemProxyMode => NetworkMode == ManagedConnectionMode.SystemProxy;
    public bool IsTunMode => NetworkMode == ManagedConnectionMode.Tun;

    public string RouteName
    {
        get => _routeName;
        private set => SetProperty(ref _routeName, value);
    }

    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public string StatusText => Stage switch
    {
        ManagedHomeStage.Idle => "未加速",
        ManagedHomeStage.Starting => "正在连接",
        ManagedHomeStage.Connected => "加速中",
        ManagedHomeStage.Stopping => "正在停止",
        ManagedHomeStage.Recovering => "正在恢复",
        ManagedHomeStage.NoAssignment => "暂无线路",
        ManagedHomeStage.NeedsPermission => "需要授权",
        ManagedHomeStage.ClassicRunning => "经典模式运行中",
        ManagedHomeStage.Faulted => "连接失败",
        _ => "正在准备",
    };

    public string ActionText => Stage switch
    {
        ManagedHomeStage.Connected => "停止加速",
        ManagedHomeStage.Faulted => "重新加速",
        ManagedHomeStage.NoAssignment => "重新检查",
        ManagedHomeStage.NeedsPermission => "授权并继续",
        _ => "一键加速",
    };

    public bool IsActionEnabled => Stage is ManagedHomeStage.Idle
        or ManagedHomeStage.Connected
        or ManagedHomeStage.Faulted
        or ManagedHomeStage.NoAssignment
        or ManagedHomeStage.NeedsPermission;

    public bool IsConnected => Stage == ManagedHomeStage.Connected;

    public bool IsBusy => Stage is ManagedHomeStage.Loading
        or ManagedHomeStage.Starting
        or ManagedHomeStage.Stopping
        or ManagedHomeStage.Recovering;

    public string OrbState => Stage switch
    {
        ManagedHomeStage.Starting or ManagedHomeStage.Stopping => "Starting",
        ManagedHomeStage.Connected => "Connected",
        ManagedHomeStage.Recovering => "Recovering",
        ManagedHomeStage.Faulted => "Faulted",
        ManagedHomeStage.NoAssignment or ManagedHomeStage.ClassicRunning => "Disabled",
        _ => "Idle",
    };

    public async Task RefreshAsync()
    {
        if (!await _operationGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            Stage = ManagedHomeStage.Loading;
            _payload = await _configProvider();
            ApplyPayload();
        }
        catch
        {
            Detail = "暂时无法检查线路，请稍后重试。";
            Stage = ManagedHomeStage.Faulted;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ToggleConnectionAsync(CancellationToken ct = default)
    {
        if (!await _operationGate.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            if (Stage == ManagedHomeStage.NoAssignment)
            {
                Stage = ManagedHomeStage.Loading;
                _payload = await _configProvider();
                ApplyPayload();
                return;
            }

            if (Stage == ManagedHomeStage.Connected)
            {
                await _connection.StopAsync(ct);
                return;
            }

            if (_payload == null)
            {
                Stage = ManagedHomeStage.Loading;
                _payload = await _configProvider();
                ApplyPayload();
                if (_payload?.Profiles.Count is not > 0)
                {
                    return;
                }
            }

            await _connection.StartAsync(
                new ManagedConnectionStartRequest
                {
                    Payload = _payload,
                    NetworkMode = NetworkMode,
                    SelectionMode = _selectionMode,
                    ProfileId = _selectedProfileId,
                    PersistSelection = false,
                    AllowAutomaticFallback = _selectionMode == ManagedProfileSelectionMode.Automatic,
                },
                ct);
        }
        catch (OperationCanceledException)
        {
            Detail = "连接操作已取消。";
            Stage = ManagedHomeStage.Idle;
        }
        catch
        {
            Detail = "网络设置没有完成，请重新尝试或打开诊断。";
            Stage = ManagedHomeStage.Faulted;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.StatusChanged -= Connection_StatusChanged;
        _operationGate.Dispose();
    }

    public void UpdateSelectionPreference(
        ManagedProfileSelectionMode mode,
        string? profileId,
        int selectionRevision)
    {
        _selectionMode = mode;
        _selectedProfileId = mode == ManagedProfileSelectionMode.Manual ? profileId : null;
        if (_payload != null)
        {
            _payload.SelectionRevision = selectionRevision;
        }
    }

    private void Connection_StatusChanged(ManagedConnectionStatus status)
    {
        if (_payload != null)
        {
            RouteName = ResolveDisplayName(_payload, status.EffectiveProfileId)
                ?? ResolveDisplayName(_payload, status.PreferredProfileId)
                ?? RouteName;
        }

        Stage = status.State switch
        {
            ManagedConnectionState.Ready => ManagedHomeStage.Idle,
            ManagedConnectionState.Starting => ManagedHomeStage.Starting,
            ManagedConnectionState.Connected => ManagedHomeStage.Connected,
            ManagedConnectionState.Stopping => ManagedHomeStage.Stopping,
            ManagedConnectionState.Faulted when status.FailureKind == ManagedConnectionFailureKind.PolicyDenied
                && status.NetworkMode == ManagedConnectionMode.Tun => ManagedHomeStage.NeedsPermission,
            ManagedConnectionState.Faulted when status.FailureKind == ManagedConnectionFailureKind.NoAssignment
                => ManagedHomeStage.NoAssignment,
            ManagedConnectionState.Faulted when status.FailureKind == ManagedConnectionFailureKind.OwnerConflict
                => ManagedHomeStage.ClassicRunning,
            ManagedConnectionState.Faulted => ManagedHomeStage.Faulted,
            _ => ManagedHomeStage.Faulted,
        };

        Detail = BuildDetail(status);
    }

    private string BuildDetail(ManagedConnectionStatus status)
    {
        if (Stage == ManagedHomeStage.Connected)
        {
            return status.IsFallback
                ? "当前使用临时备用线路，不会改变你的线路偏好。"
                : "当前网络已通过 NetAccel 加速。";
        }

        if (Stage == ManagedHomeStage.Starting)
        {
            return "正在检查线路并建立连接。";
        }

        if (Stage == ManagedHomeStage.Stopping)
        {
            return "正在恢复系统网络设置。";
        }

        if (Stage == ManagedHomeStage.NeedsPermission)
        {
            return "全局加速需要系统授权，请授权后重试。";
        }

        if (Stage == ManagedHomeStage.ClassicRunning)
        {
            return "经典模式正在使用系统网络，请先停止经典连接。";
        }

        if (Stage == ManagedHomeStage.Faulted)
        {
            return status.FailureKind switch
            {
                ManagedConnectionFailureKind.OwnerConflict => "经典模式正在使用系统网络，请先停止经典连接。",
                ManagedConnectionFailureKind.ProfileUnavailable => "当前线路暂不可用，请重新检查。",
                ManagedConnectionFailureKind.NoAssignment => "当前设备还没有可用线路。",
                _ => "网络设置没有完成，请重新尝试或打开诊断。",
            };
        }

        return "设备已就绪，可以开始网络加速。";
    }

    private void ApplyPayload()
    {
        if (_payload?.Profiles.Count is not > 0)
        {
            RouteName = "智能通道";
            Detail = "当前设备还没有可用线路。";
            Stage = ManagedHomeStage.NoAssignment;
            return;
        }

        RouteName = ResolveDisplayName(_payload, _payload.RecommendedProfileId) ?? "智能通道";
        Detail = NetworkMode == ManagedConnectionMode.SystemProxy
            ? "设备已就绪，可以开始网络加速。"
            : "全局加速可能需要系统授权。";
        Stage = ManagedHomeStage.Idle;
    }

    private static string? ResolveDisplayName(ManagedConfigPayload payload, string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        return payload.Profiles.FirstOrDefault(profile => profile.Id == profileId)?.DisplayName;
    }

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
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
