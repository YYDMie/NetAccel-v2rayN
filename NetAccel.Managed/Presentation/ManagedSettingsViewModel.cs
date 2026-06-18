using System.ComponentModel;
using System.Runtime.CompilerServices;
using NetAccel.Managed.Runtime;
using NetAccel.Managed.Settings;

namespace NetAccel.Managed.Presentation;

public sealed class ManagedSettingsViewModel : INotifyPropertyChanged
{
    private readonly IManagedPreferencesStore _preferencesStore;
    private readonly Func<bool> _autoRunProvider;
    private readonly Func<bool, CancellationToken, Task<bool>> _autoRunUpdater;
    private readonly Func<CancellationToken, Task<int?>> _accountIdProvider;
    private readonly Func<CancellationToken, Task> _logout;
    private readonly Func<CancellationToken, Task<ClassicModeHandoffResult>> _classicHandoff;
    private readonly Func<CancellationToken, Task> _restoreSystemProxy;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _autoRun;
    private bool _autoConnect;
    private bool _minimizeToTray = true;
    private bool _notificationsEnabled = true;
    private bool _useTun;
    private string _theme = "system";
    private bool _isBusy;
    private string _accountText = "当前账号";
    private string _lastSyncText = "尚未同步";
    private string _summary = "设置会保存在当前 Windows 用户的 NetAccel 数据目录中。";

    public ManagedSettingsViewModel(
        IManagedPreferencesStore preferencesStore,
        Func<bool> autoRunProvider,
        Func<bool, CancellationToken, Task<bool>> autoRunUpdater,
        Func<CancellationToken, Task<int?>> accountIdProvider,
        Func<CancellationToken, Task> logout,
        Func<CancellationToken, Task<ClassicModeHandoffResult>> classicHandoff,
        Func<CancellationToken, Task> restoreSystemProxy)
    {
        _preferencesStore = preferencesStore;
        _autoRunProvider = autoRunProvider;
        _autoRunUpdater = autoRunUpdater;
        _accountIdProvider = accountIdProvider;
        _logout = logout;
        _classicHandoff = classicHandoff;
        _restoreSystemProxy = restoreSystemProxy;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool AutoRun
    {
        get => _autoRun;
        set => SetProperty(ref _autoRun, value);
    }

    public bool AutoConnect
    {
        get => _autoConnect;
        set => SetProperty(ref _autoConnect, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set => SetProperty(ref _notificationsEnabled, value);
    }

    public bool UseTun
    {
        get => _useTun;
        set
        {
            if (SetProperty(ref _useTun, value))
            {
                OnPropertyChanged(nameof(AccelerationScopeText));
            }
        }
    }

    public string AccelerationScopeText => UseTun ? "全局加速" : "智能加速";
    public string Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, NormalizeTheme(value));
    }
    public string AccountText
    {
        get => _accountText;
        private set => SetProperty(ref _accountText, value);
    }

    public string DeviceText => Environment.MachineName;
    public string LastSyncText
    {
        get => _lastSyncText;
        private set => SetProperty(ref _lastSyncText, value);
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
                OnPropertyChanged(nameof(CanAct));
            }
        }
    }

    public bool CanAct => !IsBusy;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var preferences = await _preferencesStore.ReadAsync(ct);
        AutoRun = _autoRunProvider();
        AutoConnect = preferences.AutoConnect;
        MinimizeToTray = preferences.MinimizeToTray;
        NotificationsEnabled = preferences.NotificationsEnabled;
        UseTun = string.Equals(preferences.PreferredNetworkMode, "tun", StringComparison.Ordinal);
        Theme = preferences.Theme;
        var accountId = await _accountIdProvider(ct);
        AccountText = accountId is > 0 ? $"账号 #{accountId}" : "当前账号";
    }

    public async Task RefreshAccountAsync(CancellationToken ct = default)
    {
        var accountId = await _accountIdProvider(ct);
        AccountText = accountId is > 0 ? $"账号 #{accountId}" : "当前账号";
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (!await TryEnterAsync(ct))
        {
            return;
        }

        try
        {
            if (!await _autoRunUpdater(AutoRun, ct))
            {
                Summary = "开机启动设置未能保存，请检查系统权限。";
                return;
            }

            await _preferencesStore.WriteAsync(CreatePreferences(), ct);
            Summary = "设置已保存。";
        }
        catch (OperationCanceledException)
        {
            Summary = "保存已取消。";
        }
        catch
        {
            Summary = "设置暂时无法保存，请稍后重试。";
        }
        finally
        {
            ExitOperation();
        }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (!await TryEnterAsync(ct))
        {
            return;
        }

        try
        {
            await _logout(ct);
            AccountText = "当前账号";
            LastSyncText = "尚未同步";
            Summary = "已退出当前账号。";
        }
        catch (OperationCanceledException)
        {
            Summary = "退出登录已取消。";
        }
        catch
        {
            Summary = "退出登录未完成，请稍后重试。";
        }
        finally
        {
            ExitOperation();
        }
    }

    public async Task<ClassicModeHandoffResult> PrepareClassicModeAsync(CancellationToken ct = default)
    {
        if (!await TryEnterAsync(ct))
        {
            return ClassicModeHandoffResult.Failed("another_settings_operation_is_running");
        }

        try
        {
            var result = await _classicHandoff(ct);
            Summary = result.Success
                ? "已安全切换到经典模式。"
                : "经典模式暂时无法打开，系统网络没有被重复接管。";
            return result;
        }
        catch (OperationCanceledException)
        {
            Summary = "切换经典模式已取消。";
            return ClassicModeHandoffResult.Failed("classic_handoff_cancelled");
        }
        catch
        {
            Summary = "经典模式暂时无法打开，系统网络没有被重复接管。";
            return ClassicModeHandoffResult.Failed("classic_handoff_failed");
        }
        finally
        {
            ExitOperation();
        }
    }

    public async Task RestoreSystemProxyAsync(CancellationToken ct = default)
    {
        if (!await TryEnterAsync(ct))
        {
            return;
        }

        try
        {
            await _restoreSystemProxy(ct);
            Summary = "已恢复托管模式保存的系统代理状态。";
        }
        catch (OperationCanceledException)
        {
            Summary = "系统代理恢复已取消。";
        }
        catch
        {
            Summary = "系统代理暂时无法恢复，请打开诊断。";
        }
        finally
        {
            ExitOperation();
        }
    }

    public void MarkSynchronized(DateTimeOffset synchronizedAt)
    {
        LastSyncText = synchronizedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    public ManagedPreferences CreatePreferences()
        => new()
        {
            AutoConnect = AutoConnect,
            MinimizeToTray = MinimizeToTray,
            NotificationsEnabled = NotificationsEnabled,
            PreferredNetworkMode = UseTun ? "tun" : "system_proxy",
            Theme = Theme,
        };

    private static string NormalizeTheme(string? theme)
        => theme?.Trim().ToLowerInvariant() switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => "system",
        };

    private async Task<bool> TryEnterAsync(CancellationToken ct)
    {
        if (!await _operationGate.WaitAsync(0, ct))
        {
            return false;
        }

        IsBusy = true;
        return true;
    }

    private void ExitOperation()
    {
        IsBusy = false;
        _operationGate.Release();
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
