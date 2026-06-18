using System.ComponentModel;
using System.Runtime.CompilerServices;
using NetAccel.Managed.Runtime;

namespace NetAccel.Managed.Presentation;

public sealed class ManagedDiagnosticsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IManagedDiagnosticsService _diagnostics;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private ManagedDiagnosticCheck _serviceConnection = Pending("服务连接");
    private ManagedDiagnosticCheck _currentRoute = Pending("当前线路");
    private ManagedDiagnosticCheck _localNetwork = Pending("本机网络");
    private ManagedDiagnosticCheck _accelerationEngine = Pending("加速引擎");
    private string _summary = "点击重新检查以查看当前状态。";
    private bool _isBusy;
    private bool _classicModeOwnsConnection;
    private bool _disposed;

    public ManagedDiagnosticsViewModel(IManagedDiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ManagedDiagnosticCheck ServiceConnection
    {
        get => _serviceConnection;
        private set => SetProperty(ref _serviceConnection, value);
    }

    public ManagedDiagnosticCheck CurrentRoute
    {
        get => _currentRoute;
        private set => SetProperty(ref _currentRoute, value);
    }

    public ManagedDiagnosticCheck LocalNetwork
    {
        get => _localNetwork;
        private set => SetProperty(ref _localNetwork, value);
    }

    public ManagedDiagnosticCheck AccelerationEngine
    {
        get => _accelerationEngine;
        private set => SetProperty(ref _accelerationEngine, value);
    }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRepair));
            }
        }
    }

    public bool CanRepair => !IsBusy && !_classicModeOwnsConnection;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await _operationGate.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            IsBusy = true;
            var snapshot = await _diagnostics.CheckAsync(ct);
            ApplySnapshot(snapshot);
            Summary = snapshot.NeedsRepair
                ? "发现可安全修复的托管网络状态。"
                : snapshot.ClassicModeOwnsConnection
                    ? "经典模式正在运行；诊断页不会修改经典数据或连接。"
                    : "四项检查已完成。";
        }
        catch (OperationCanceledException)
        {
            Summary = "检查已取消。";
        }
        catch
        {
            Summary = "检查未完成，请稍后重试。";
        }
        finally
        {
            IsBusy = false;
            _operationGate.Release();
        }
    }

    public async Task RepairAsync(CancellationToken ct = default)
    {
        if (!await _operationGate.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            IsBusy = true;
            var result = await _diagnostics.RepairAsync(ct);
            ApplySnapshot(result.Snapshot);
            Summary = result.BlockedByClassicMode
                ? "经典模式正在使用系统网络，未执行任何自动修复。"
                : string.Join(" ", result.Steps.Select(step => step.Message));
        }
        catch (OperationCanceledException)
        {
            Summary = "修复已取消。";
        }
        catch
        {
            Summary = "修复未完成；账号、本地节点和经典订阅未被修改。";
        }
        finally
        {
            IsBusy = false;
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
        _operationGate.Dispose();
    }

    private void ApplySnapshot(ManagedDiagnosticsSnapshot snapshot)
    {
        ServiceConnection = snapshot.ServiceConnection;
        CurrentRoute = snapshot.CurrentRoute;
        LocalNetwork = snapshot.LocalNetwork;
        AccelerationEngine = snapshot.AccelerationEngine;
        _classicModeOwnsConnection = snapshot.ClassicModeOwnsConnection;
        OnPropertyChanged(nameof(CanRepair));
    }

    private static ManagedDiagnosticCheck Pending(string title)
        => new()
        {
            Title = title,
            Status = "待检查",
            Detail = "尚未读取当前状态。",
            Condition = ManagedDiagnosticCondition.Warning,
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
