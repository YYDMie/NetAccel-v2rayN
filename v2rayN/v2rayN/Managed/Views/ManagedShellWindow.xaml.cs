using System.ComponentModel;
using NetAccel.Managed.Presentation;
using NetAccel.Managed.Runtime;
using System.Windows.Controls;
using v2rayN.Managed.Controls;
using v2rayN.Managed.Services;
using v2rayN.Managed.ViewModels;

namespace v2rayN.Managed.Views;

public partial class ManagedShellWindow : Window
{
    private readonly ManagedClientRuntime _runtime;
    private bool _startupAttempted;
    private bool _homeInitialized;
    private bool _routesInitialized;
    private bool _diagnosticsInitialized;

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
        _runtime.LoginViewModel.PropertyChanged += LoginViewModel_PropertyChanged;
        _runtime.HomeViewModel.PropertyChanged += HomeViewModel_PropertyChanged;
        Loaded += ManagedShellWindow_Loaded;

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
    }

    protected override void OnClosed(EventArgs e)
    {
        _runtime.LoginViewModel.PropertyChanged -= LoginViewModel_PropertyChanged;
        _runtime.HomeViewModel.PropertyChanged -= HomeViewModel_PropertyChanged;
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
        PlaceholderSection.Visibility = section is ManagedShellSection.Home
            or ManagedShellSection.Routes
            or ManagedShellSection.Activity
            or ManagedShellSection.Diagnostics
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
    }

    private async void ManagedShellWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startupAttempted)
        {
            return;
        }

        _startupAttempted = true;
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

    private void UpdateShellVisibility()
    {
        var isReady = _runtime.LoginViewModel.IsReady;
        LoginView.Visibility = isReady ? Visibility.Collapsed : Visibility.Visible;
        ShellContent.Visibility = isReady ? Visibility.Visible : Visibility.Collapsed;
        ShellStatus.Text = isReady ? "设备已就绪" : "需要登录";
        ShellStatus.Tone = isReady ? StatusTone.Success : StatusTone.Info;

        if (isReady)
        {
            if (!_homeInitialized)
            {
                _homeInitialized = true;
                _ = RefreshHomeAsync();
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
        }
    }

    private async Task RefreshHomeAsync()
    {
        await _runtime.HomeViewModel.RefreshAsync();
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
    }
}
