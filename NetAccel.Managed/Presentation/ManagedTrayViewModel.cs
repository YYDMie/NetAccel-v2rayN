using System.ComponentModel;
using System.Runtime.CompilerServices;
using NetAccel.Managed.Runtime;

namespace NetAccel.Managed.Presentation;

public enum ManagedTrayMode
{
    Idle,
    Starting,
    Connected,
    Recovering,
    Faulted,
    Classic,
}

public sealed class ManagedTrayViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IManagedConnectionCoordinator _connection;
    private ManagedTrayMode _mode = ManagedTrayMode.Idle;
    private string _routeName = "智能通道";
    private bool _disposed;

    public ManagedTrayViewModel(IManagedConnectionCoordinator connection)
    {
        _connection = connection;
        _connection.StatusChanged += Connection_StatusChanged;
        ApplyStatus(connection.Status);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ManagedTrayMode Mode
    {
        get => _mode;
        private set
        {
            if (!SetProperty(ref _mode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(Header));
            OnPropertyChanged(nameof(PrimaryActionText));
            OnPropertyChanged(nameof(IsRouteVisible));
            OnPropertyChanged(nameof(IsClassicMode));
            OnPropertyChanged(nameof(ToolTipText));
        }
    }

    public string Header => Mode switch
    {
        ManagedTrayMode.Connected => "NetAccel · 加速中",
        ManagedTrayMode.Starting => "NetAccel · 正在连接",
        ManagedTrayMode.Recovering => "NetAccel · 正在恢复",
        ManagedTrayMode.Faulted => "NetAccel · 连接异常",
        ManagedTrayMode.Classic => "NetAccel · 经典模式",
        _ => "NetAccel · 未加速",
    };

    public string PrimaryActionText => Mode switch
    {
        ManagedTrayMode.Connected => "停止加速",
        ManagedTrayMode.Classic => "打开经典窗口",
        ManagedTrayMode.Faulted => "重新加速",
        _ => "一键加速",
    };

    public string RouteText => $"当前线路：{_routeName}";
    public bool IsRouteVisible => Mode == ManagedTrayMode.Connected;
    public bool IsClassicMode => Mode == ManagedTrayMode.Classic;
    public string ToolTipText => Header;

    public void SetRouteName(string? routeName)
    {
        var safeName = string.IsNullOrWhiteSpace(routeName) ? "智能通道" : routeName.Trim();
        if (string.Equals(_routeName, safeName, StringComparison.Ordinal))
        {
            return;
        }

        _routeName = safeName;
        OnPropertyChanged(nameof(RouteText));
    }

    public void SetClassicMode(bool active)
    {
        if (active)
        {
            Mode = ManagedTrayMode.Classic;
            return;
        }

        ApplyStatus(_connection.Status);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.StatusChanged -= Connection_StatusChanged;
    }

    private void Connection_StatusChanged(ManagedConnectionStatus status)
    {
        if (Mode != ManagedTrayMode.Classic)
        {
            ApplyStatus(status);
        }
    }

    private void ApplyStatus(ManagedConnectionStatus status)
    {
        Mode = status.State switch
        {
            ManagedConnectionState.Starting or ManagedConnectionState.Stopping => ManagedTrayMode.Starting,
            ManagedConnectionState.Connected => ManagedTrayMode.Connected,
            ManagedConnectionState.Faulted when status.FailureKind == ManagedConnectionFailureKind.RestoreFailed
                => ManagedTrayMode.Recovering,
            ManagedConnectionState.Faulted => ManagedTrayMode.Faulted,
            _ => ManagedTrayMode.Idle,
        };
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
