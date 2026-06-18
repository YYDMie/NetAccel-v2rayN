using System.ComponentModel;
using H.NotifyIcon.Core;
using NetAccel.Managed.Presentation;
using NetAccel.Managed.Runtime;
using ServiceLib.Handler.SysProxy;
using ServiceLib.Manager;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using v2rayN.Managed.Controls;
using v2rayN.Managed.Services;
using v2rayN.Managed.ViewModels;
using v2rayN.Views;

namespace v2rayN.Managed.Views;

public partial class ManagedShellWindow : Window
{
    private readonly ManagedClientRuntime _runtime;
    private bool _startupAttempted;
    private bool _homeInitialized;
    private bool _routesInitialized;
    private bool _diagnosticsInitialized;
    private bool _settingsInitialized;
    private bool _autoConnectAttempted;
    private bool _allowClose;
    private MainWindow? _classicWindow;
    private ManagedTrayMode _lastTrayMode = ManagedTrayMode.Idle;

    private readonly Dictionary<ManagedShellSection, (string Title, string Subtitle)> _sectionCopy = new()
    {
        [ManagedShellSection.Home] = ("首页", "查看状态并开始网络加速。"),
        [ManagedShellSection.Routes] = ("线路", "选择智能模式或管理员已分配的线路。"),
        [ManagedShellSection.Activity] = ("记录", "查看最近的连接和恢复记录。"),
        [ManagedShellSection.Settings] = ("设置", "调整常用偏好、账号和更新选项。"),
        [ManagedShellSection.Diagnostics] = ("诊断", "检查服务、线路、本机网络和加速引擎。"),
    };

    public ManagedShellWindow()
    {
        _runtime = ManagedClientRuntime.Create();
        InitializeComponent();
        LoginView.AttachViewModel(_runtime.LoginViewModel);
        HomeSection.DataContext = _runtime.HomeViewModel;
        RoutesSection.DataContext = _runtime.RoutesViewModel;
        ActivitySection.DataContext = _runtime.ActivityViewModel;
        DiagnosticsSection.DataContext = _runtime.DiagnosticsViewModel;
        SettingsSection.DataContext = _runtime.SettingsViewModel;
        _runtime.LoginViewModel.PropertyChanged += LoginViewModel_PropertyChanged;
        _runtime.HomeViewModel.PropertyChanged += HomeViewModel_PropertyChanged;
        _runtime.TrayViewModel.PropertyChanged += TrayViewModel_PropertyChanged;
        Loaded += ManagedShellWindow_Loaded;
        Closing += ManagedShellWindow_Closing;
        Application.Current.SessionEnding += Current_SessionEnding;
        ManagedClassicModeBridge.ReturnToManagedAsync = ReturnToManagedAsync;
        CurrentVersionStatus.Value = Utils.GetVersionInfo();
        ThreadPool.RegisterWaitForSingleObject(App.ProgramStarted, OnProgramStarted, null, -1, false);

        foreach (var navigation in new[]
                 {
                     HomeNavigation,
                     RoutesNavigation,
                     ActivityNavigation,
                     SettingsNavigation,
                     DiagnosticsNavigation,
                 })
        {
            navigation.Checked += Navigation_Checked;
        }

        ShowSection(ManagedShellSection.Home);
        UpdateShellVisibility();
        UpdateTrayState();
    }

    protected override void OnClosed(EventArgs e)
    {
        _runtime.LoginViewModel.PropertyChanged -= LoginViewModel_PropertyChanged;
        _runtime.HomeViewModel.PropertyChanged -= HomeViewModel_PropertyChanged;
        _runtime.TrayViewModel.PropertyChanged -= TrayViewModel_PropertyChanged;
        Application.Current.SessionEnding -= Current_SessionEnding;
        ManagedClassicModeBridge.ReturnToManagedAsync = null;
        ManagedTray.Dispose();
        _runtime.Dispose();
        base.OnClosed(e);
    }

    private void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string sectionName }
            && Enum.TryParse<ManagedShellSection>(sectionName, out var section))
        {
            ShowSection(section);
        }
    }

    private void ShowSection(ManagedShellSection section)
    {
        var copy = _sectionCopy[section];
        PageTitle.Text = copy.Title;
        PageSubtitle.Text = copy.Subtitle;
        HomeSection.Visibility = section == ManagedShellSection.Home ? Visibility.Visible : Visibility.Collapsed;
        RoutesSection.Visibility = section == ManagedShellSection.Routes ? Visibility.Visible : Visibility.Collapsed;
        ActivitySection.Visibility = section == ManagedShellSection.Activity ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsSection.Visibility = section == ManagedShellSection.Diagnostics ? Visibility.Visible : Visibility.Collapsed;
        SettingsSection.Visibility = section == ManagedShellSection.Settings ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderSection.Visibility = section is ManagedShellSection.Home
            or ManagedShellSection.Routes
            or ManagedShellSection.Activity
            or ManagedShellSection.Diagnostics
            or ManagedShellSection.Settings
            ? Visibility.Collapsed
            : Visibility.Visible;
        PlaceholderSection.Title = copy.Title;

        if (section == ManagedShellSection.Routes && _runtime.LoginViewModel.IsReady && !_routesInitialized)
        {
            _routesInitialized = true;
            _ = RefreshRoutesAsync();
        }

        if (section == ManagedShellSection.Diagnostics
            && _runtime.LoginViewModel.IsReady
            && !_diagnosticsInitialized)
        {
            _diagnosticsInitialized = true;
            _ = RefreshDiagnosticsAsync();
        }

        if (section == ManagedShellSection.Settings && !_settingsInitialized)
        {
            _ = EnsureSettingsInitializedAsync();
        }
    }

    private async void ManagedShellWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startupAttempted)
        {
            return;
        }

        _startupAttempted = true;
        await EnsureSettingsInitializedAsync();
        await _runtime.LoginViewModel.InitializeAsync();
    }

    private void LoginViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ManagedLoginViewModel.Stage) or nameof(ManagedLoginViewModel.IsReady))
        {
            if (Dispatcher.CheckAccess())
            {
                UpdateShellVisibility();
            }
            else
            {
                Dispatcher.InvokeAsync(UpdateShellVisibility);
            }
        }
    }

    private void HomeViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ManagedHomeViewModel.Stage) or nameof(ManagedHomeViewModel.OrbState))
        {
            if (Dispatcher.CheckAccess())
            {
                UpdateHomeState();
            }
            else
            {
                Dispatcher.InvokeAsync(UpdateHomeState);
            }
        }

        if (e.PropertyName == nameof(ManagedHomeViewModel.RouteName))
        {
            _runtime.TrayViewModel.SetRouteName(_runtime.HomeViewModel.RouteName);
        }
    }

    private void OnProgramStarted(object? state, bool timedOut)
    {
        Dispatcher.InvokeAsync(ShowActiveWindow);
    }

    private void TrayViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            UpdateTrayState();
        }
        else
        {
            Dispatcher.InvokeAsync(UpdateTrayState);
        }
    }

    private async void ConnectionOrb_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _runtime.HomeViewModel.ToggleConnectionAsync();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Managed home action failed", ex);
        }
    }

    private void SystemProxyMode_Click(object sender, RoutedEventArgs e)
    {
        _runtime.HomeViewModel.NetworkMode = ManagedConnectionMode.SystemProxy;
    }

    private void TunMode_Click(object sender, RoutedEventArgs e)
    {
        _runtime.HomeViewModel.NetworkMode = ManagedConnectionMode.Tun;
    }

    private async void AutomaticRoute_Click(object sender, RoutedEventArgs e)
    {
        await RunRouteActionAsync(() => _runtime.RoutesViewModel.SelectAutomaticAsync());
    }

    private async void RouteBubble_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RouteBubble { Tag: string profileId })
        {
            await RunRouteActionAsync(() => _runtime.RoutesViewModel.SelectRouteAsync(profileId));
        }
    }

    private async void RefreshRoutes_Click(object sender, RoutedEventArgs e)
    {
        await RunRouteActionAsync(() => _runtime.RoutesViewModel.RefreshAsync());
    }

    private void OpenDetailedLogs_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsNavigation.IsChecked = true;
        DiagnosticDetails.Visibility = Visibility.Visible;
    }

    private async void RefreshDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDiagnosticsAsync();
    }

    private async void RepairDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _runtime.DiagnosticsViewModel.RepairAsync();
            UpdateHomeState();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Managed diagnostics repair failed", ex);
        }
    }

    private void ToggleDiagnosticDetails_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticDetails.Visibility = DiagnosticDetails.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".zip",
            FileName = $"NetAccel-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            Filter = "ZIP 诊断包 (*.zip)|*.zip",
            OverwritePrompt = true,
            Title = "导出脱敏诊断包",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await _runtime.DiagnosticsViewModel.ExportAsync(dialog.FileName);
        DiagnosticDetails.Visibility = Visibility.Visible;
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        await _runtime.SettingsViewModel.SaveAsync();
        _runtime.HomeViewModel.NetworkMode = _runtime.SettingsViewModel.UseTun
            ? ManagedConnectionMode.Tun
            : ManagedConnectionMode.SystemProxy;
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        await _runtime.SettingsViewModel.LogoutAsync();
    }

    private async void OpenClassicMode_Click(object sender, RoutedEventArgs e)
    {
        await OpenClassicModeAsync();
    }

    private async void RestoreSystemProxy_Click(object sender, RoutedEventArgs e)
    {
        await _runtime.SettingsViewModel.RestoreSystemProxyAsync();
    }

    private void OpenDataDirectory_Click(object sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart(Utils.StartupPath());
    }

    private void ManagedTray_DoubleClick(object sender, RoutedEventArgs e)
    {
        ShowActiveWindow();
    }

    private async void TrayPrimaryAction_Click(object sender, RoutedEventArgs e)
    {
        if (_runtime.TrayViewModel.IsClassicMode)
        {
            ShowClassicWindow();
            return;
        }

        await _runtime.HomeViewModel.ToggleConnectionAsync();
    }

    private void TrayOpenMain_Click(object sender, RoutedEventArgs e)
    {
        ShowActiveWindow();
    }

    private void TrayDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        ShowManagedSection(ManagedShellSection.Diagnostics);
        _ = RefreshDiagnosticsAsync();
    }

    private void TraySettings_Click(object sender, RoutedEventArgs e)
    {
        ShowManagedSection(ManagedShellSection.Settings);
    }

    private async void TrayStopClassic_Click(object sender, RoutedEventArgs e)
    {
        await StopClassicConnectionAsync();
    }

    private async void TrayReturnManaged_Click(object sender, RoutedEventArgs e)
    {
        await ReturnToManagedAsync();
    }

    private async void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        ManagedTray.Dispose();
        await AppManager.Instance.AppExitAsync(false);
        Application.Current.Shutdown();
    }

    private void UpdateShellVisibility()
    {
        var isReady = _runtime.LoginViewModel.IsReady;
        LoginView.Visibility = isReady ? Visibility.Collapsed : Visibility.Visible;
        ShellContent.Visibility = isReady ? Visibility.Visible : Visibility.Collapsed;
        ShellStatus.Text = isReady ? "设备已就绪" : "需要登录";
        ShellStatus.Tone = isReady ? StatusTone.Success : StatusTone.Info;

        if (isReady)
        {
            _runtime.SettingsViewModel.MarkSynchronized(DateTimeOffset.Now);
            _ = _runtime.SettingsViewModel.RefreshAccountAsync();
            if (!_homeInitialized)
            {
                _homeInitialized = true;
                _ = RefreshHomeAndAutoConnectAsync();
            }

            if (!_routesInitialized)
            {
                _routesInitialized = true;
                _ = RefreshRoutesAsync();
            }
        }
        else
        {
            _homeInitialized = false;
            _routesInitialized = false;
            _diagnosticsInitialized = false;
            _autoConnectAttempted = false;
        }
    }

    private async Task RefreshHomeAndAutoConnectAsync()
    {
        await _runtime.HomeViewModel.RefreshAsync();
        _runtime.HomeViewModel.NetworkMode = _runtime.SettingsViewModel.UseTun
            ? ManagedConnectionMode.Tun
            : ManagedConnectionMode.SystemProxy;
        if (!_autoConnectAttempted
            && _runtime.SettingsViewModel.AutoConnect
            && _runtime.HomeViewModel.Stage == ManagedHomeStage.Idle)
        {
            _autoConnectAttempted = true;
            await _runtime.HomeViewModel.ToggleConnectionAsync();
        }
        UpdateHomeState();
    }

    private async Task RefreshRoutesAsync()
    {
        await RunRouteActionAsync(() => _runtime.RoutesViewModel.RefreshAsync());
    }

    private async Task RefreshDiagnosticsAsync()
    {
        try
        {
            await _runtime.DiagnosticsViewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Managed diagnostics refresh failed", ex);
        }
    }

    private static async Task RunRouteActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Managed routes action failed", ex);
        }
    }

    private void UpdateHomeState()
    {
        ConnectionOrb.State = Enum.TryParse<ConnectOrbState>(_runtime.HomeViewModel.OrbState, out var state)
            ? state
            : ConnectOrbState.Idle;

        ShellStatus.Text = _runtime.HomeViewModel.StatusText;
        ShellStatus.Tone = _runtime.HomeViewModel.Stage switch
        {
            ManagedHomeStage.Connected => StatusTone.Success,
            ManagedHomeStage.Faulted => StatusTone.Danger,
            ManagedHomeStage.NeedsPermission or ManagedHomeStage.ClassicRunning => StatusTone.Warning,
            _ => StatusTone.Info,
        };
        _runtime.TrayViewModel.SetRouteName(_runtime.HomeViewModel.RouteName);
    }

    private async Task EnsureSettingsInitializedAsync()
    {
        if (_settingsInitialized)
        {
            return;
        }

        _settingsInitialized = true;
        await _runtime.SettingsViewModel.InitializeAsync();
        _runtime.HomeViewModel.NetworkMode = _runtime.SettingsViewModel.UseTun
            ? ManagedConnectionMode.Tun
            : ManagedConnectionMode.SystemProxy;
    }

    private async Task OpenClassicModeAsync()
    {
        var result = await _runtime.EnterClassicModeAsync();
        if (!result.Success)
        {
            return;
        }

        _runtime.TrayViewModel.SetClassicMode(true);
        ShowClassicWindow();
        Hide();
    }

    private void ShowClassicWindow()
    {
        _classicWindow ??= new MainWindow();
        Application.Current.MainWindow = _classicWindow;
        _classicWindow.ShowHideWindow(true);
    }

    private async Task ReturnToManagedAsync()
    {
        await StopClassicConnectionAsync();
        await _runtime.LeaveClassicModeAsync();
        _runtime.TrayViewModel.SetClassicMode(false);
        _classicWindow?.ShowHideWindow(false);
        Application.Current.MainWindow = this;
        ShowManagedSection(ManagedShellSection.Home);
        await RefreshHomeAndAutoConnectAsync();
    }

    private static async Task StopClassicConnectionAsync()
    {
        var config = AppManager.Instance.Config;
        await CoreManager.Instance.CoreStop();
        await SysProxyHandler.UpdateSysProxy(config, true);
        await AppManager.Instance.RemoveTunDeviceAsync();
    }

    private void ShowManagedSection(ManagedShellSection section)
    {
        Application.Current.MainWindow = this;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        switch (section)
        {
            case ManagedShellSection.Diagnostics:
                DiagnosticsNavigation.IsChecked = true;
                break;
            case ManagedShellSection.Settings:
                SettingsNavigation.IsChecked = true;
                break;
            default:
                HomeNavigation.IsChecked = true;
                break;
        }
    }

    private void ShowActiveWindow()
    {
        if (_runtime.TrayViewModel.IsClassicMode)
        {
            ShowClassicWindow();
        }
        else
        {
            ShowManagedSection(ManagedShellSection.Home);
        }
    }

    private void UpdateTrayState()
    {
        var tray = _runtime.TrayViewModel;
        TrayHeader.Header = tray.Header;
        TrayPrimaryAction.Header = tray.PrimaryActionText;
        TrayCurrentRoute.Header = tray.RouteText;
        TrayCurrentRoute.Visibility = tray.IsRouteVisible ? Visibility.Visible : Visibility.Collapsed;
        ManagedTray.ToolTipText = tray.ToolTipText;

        var classic = tray.IsClassicMode;
        TrayDiagnostics.Visibility = classic ? Visibility.Collapsed : Visibility.Visible;
        TraySettings.Visibility = classic ? Visibility.Collapsed : Visibility.Visible;
        TrayStopClassic.Visibility = classic ? Visibility.Visible : Visibility.Collapsed;
        TrayReturnManaged.Visibility = classic ? Visibility.Visible : Visibility.Collapsed;

        var iconIndex = tray.Mode switch
        {
            ManagedTrayMode.Connected => 2,
            ManagedTrayMode.Faulted => 3,
            ManagedTrayMode.Starting or ManagedTrayMode.Recovering or ManagedTrayMode.Classic => 4,
            _ => 1,
        };
        ManagedTray.IconSource = BitmapFrame.Create(
            new Uri($"pack://application:,,,/Resources/NotifyIcon{iconIndex}.ico", UriKind.Absolute));

        if (_settingsInitialized
            && _runtime.SettingsViewModel.NotificationsEnabled
            && tray.Mode != _lastTrayMode)
        {
            if (tray.Mode == ManagedTrayMode.Connected)
            {
                ManagedTray.ShowNotification("NetAccel", "网络加速已开启。", NotificationIcon.Info);
            }
            else if (tray.Mode == ManagedTrayMode.Faulted)
            {
                ManagedTray.ShowNotification("NetAccel", "网络加速未能完成，请打开诊断。", NotificationIcon.Error);
            }
        }

        _lastTrayMode = tray.Mode;
    }

    private async void ManagedShellWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_runtime.SettingsViewModel.MinimizeToTray)
        {
            Hide();
            return;
        }

        _allowClose = true;
        ManagedTray.Dispose();
        await AppManager.Instance.AppExitAsync(false);
        Application.Current.Shutdown();
    }

    private async void Current_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        _allowClose = true;
        ManagedTray.Dispose();
        await AppManager.Instance.AppExitAsync(false);
    }
}
