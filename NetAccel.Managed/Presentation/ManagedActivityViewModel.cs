using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NetAccel.Managed.Runtime;

namespace NetAccel.Managed.Presentation;

public enum ManagedActivityKind
{
    Connected,
    Stopped,
    Recovered,
    Failed,
}

public sealed record ManagedActivityItem
{
    public required DateTimeOffset OccurredAt { get; init; }
    public required ManagedActivityKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public string TimeText => $"今天 {OccurredAt.LocalDateTime:HH:mm}";
}

public sealed class ManagedActivityViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaxItems = 50;
    private readonly IManagedConnectionCoordinator _connection;
    private readonly TimeProvider _timeProvider;
    private ManagedConnectionStatus _previousStatus;
    private bool _switchInProgress;
    private bool _disposed;

    public ManagedActivityViewModel(
        IManagedConnectionCoordinator connection,
        TimeProvider? timeProvider = null)
    {
        _connection = connection;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _previousStatus = connection.Status;
        _connection.StatusChanged += Connection_StatusChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ManagedActivityItem> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    public string EmptyMessage => "开始或停止加速后，记录会显示在这里。";

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
        if (status.State == ManagedConnectionState.Starting
            && _previousStatus.State == ManagedConnectionState.Connected)
        {
            _switchInProgress = true;
        }

        var item = CreateActivity(_previousStatus, status);
        _previousStatus = status;
        if (item == null)
        {
            return;
        }

        Items.Insert(0, item);
        while (Items.Count > MaxItems)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        OnPropertyChanged(nameof(HasItems));
    }

    private ManagedActivityItem? CreateActivity(
        ManagedConnectionStatus previous,
        ManagedConnectionStatus current)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        if (current.State == ManagedConnectionState.Connected)
        {
            var wasSwitching = _switchInProgress;
            _switchInProgress = false;
            if (current.FailureKind != ManagedConnectionFailureKind.None)
            {
                return new ManagedActivityItem
                {
                    OccurredAt = occurredAt,
                    Kind = ManagedActivityKind.Recovered,
                    Title = "切换未完成",
                    Detail = current.FailureKind == ManagedConnectionFailureKind.Cancelled
                        ? "线路切换已取消，原线路仍在使用。"
                        : "新线路未能连接，已恢复原线路。",
                };
            }

            if (current.IsFallback)
            {
                return new ManagedActivityItem
                {
                    OccurredAt = occurredAt,
                    Kind = ManagedActivityKind.Recovered,
                    Title = "已自动恢复",
                    Detail = "原线路暂不可用，已临时使用备用线路。",
                };
            }

            return new ManagedActivityItem
            {
                OccurredAt = occurredAt,
                Kind = wasSwitching
                    ? ManagedActivityKind.Recovered
                    : ManagedActivityKind.Connected,
                Title = wasSwitching ? "线路已切换" : "已开始加速",
                Detail = wasSwitching
                    ? "新线路已经连接，网络设置保持正常。"
                    : "网络加速已经连接。",
            };
        }

        if (current.State == ManagedConnectionState.Ready
            && previous.State == ManagedConnectionState.Stopping)
        {
            _switchInProgress = false;
            return new ManagedActivityItem
            {
                OccurredAt = occurredAt,
                Kind = ManagedActivityKind.Stopped,
                Title = "已停止加速",
                Detail = "系统网络设置已经恢复。",
            };
        }

        if (current.State == ManagedConnectionState.Faulted)
        {
            _switchInProgress = false;
            return new ManagedActivityItem
            {
                OccurredAt = occurredAt,
                Kind = ManagedActivityKind.Failed,
                Title = "加速未完成",
                Detail = FriendlyFailure(current.FailureKind),
            };
        }

        return null;
    }

    private static string FriendlyFailure(ManagedConnectionFailureKind kind)
        => kind switch
        {
            ManagedConnectionFailureKind.OwnerConflict => "经典模式正在使用系统网络。",
            ManagedConnectionFailureKind.NoAssignment => "当前设备还没有可用线路。",
            ManagedConnectionFailureKind.ProfileNotAssigned => "所选线路已不在当前设备的分配范围内。",
            ManagedConnectionFailureKind.ProfileUnavailable => "线路暂不可用，请稍后重试。",
            ManagedConnectionFailureKind.CapabilityIncompatible => "当前设备暂不支持这条线路。",
            ManagedConnectionFailureKind.PolicyDenied => "当前策略不允许这项连接方式。",
            ManagedConnectionFailureKind.Cancelled => "连接操作已取消。",
            ManagedConnectionFailureKind.RestoreFailed => "网络恢复未完成，请打开诊断。",
            _ => "网络设置没有完成，请重新尝试或打开诊断。",
        };

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
