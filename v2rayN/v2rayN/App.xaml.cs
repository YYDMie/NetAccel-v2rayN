using NetAccel.Managed.Runtime;

namespace v2rayN;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static EventWaitHandle ProgramStarted;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    /// <summary>
    /// Open only one process
    /// </summary>
    /// <param name="e"></param>
    protected override void OnStartup(StartupEventArgs e)
    {
        // ── NetAccel identity: configure before anything else ──────
        NetAccelIdentity.Configure(NetAccelIdentity.CreateNetAccel());

        // Use the fixed single-instance name (not an exe-path hash)
        var instanceName = NetAccelIdentity.Active.SingleInstanceName;

        var rebootas = e.Args.Any(t => t == Global.RebootAs);
        ProgramStarted = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName, out var bCreatedNew);
        if (!rebootas && !bCreatedNew)
        {
            ProgramStarted.Set();
            Environment.Exit(0);
            return;
        }

        // Set Windows AppUserModelID before main window / tray is created
        SetAppUserModelId(NetAccelIdentity.Active.AppUserModelID);

        if (!AppManager.Instance.InitApp())
        {
            UI.Show($"Loading GUI configuration file is abnormal,please restart the application{Environment.NewLine}加载GUI配置文件异常,请重启应用");
            Environment.Exit(0);
            return;
        }

        try
        {
            var proxyRecoveryStore = new ManagedSystemProxyRecoveryStore(
                Path.Combine(Utils.StartupPath(), "managed-runtime", "system-proxy-recovery.json"));
            if (proxyRecoveryStore.Recover())
            {
                Logging.SaveLog("[Managed] Recovered system proxy after an interrupted session.");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("[Managed] System proxy recovery failed", ex);
        }

        AppManager.Instance.InitComponents();

        RxAppBuilder.CreateReactiveUIBuilder()
            .WithWpf()
            .BuildApp();

        base.OnStartup(e);

        var managedWindow = new Managed.Views.ManagedShellWindow();
        MainWindow = managedWindow;
        managedWindow.Show();

        var healthMarker = GetUpdateHealthMarker(e.Args);
        if (!string.IsNullOrWhiteSpace(healthMarker))
        {
            try
            {
                var markerPath = Path.GetFullPath(healthMarker);
                Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
                File.WriteAllText(markerPath, DateTimeOffset.UtcNow.ToString("O"));
            }
            catch (Exception ex)
            {
                Logging.SaveLog("Update health marker failed", ex);
            }
        }
    }

    private static string? GetArgumentValue(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i + 1 < args.Count; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static string? GetUpdateHealthMarker(IReadOnlyList<string> args)
    {
        var value = GetArgumentValue(args, "--netaccel-update-health-marker");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NetAccel", "updater"))
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(value);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logging.SaveLog("App_DispatcherUnhandledException", e.Exception);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject != null)
        {
            Logging.SaveLog("CurrentDomain_UnhandledException", (Exception)e.ExceptionObject);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logging.SaveLog("TaskScheduler_UnobservedTaskException", e.Exception);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logging.SaveLog("OnExit");
        base.OnExit(e);
        Process.GetCurrentProcess().Kill();
    }

    /// <summary>
    /// Sets the Windows explicit AppUserModelID for the current process.
    /// This must be called before the main window or tray icon is created.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void SetAppUserModelId(string appUserModelId)
    {
        if (!Utils.IsWindows() || string.IsNullOrEmpty(appUserModelId))
        {
            return;
        }
        try
        {
            WindowsIdentityHelper.SetCurrentProcessExplicitAppUserModelID(appUserModelId);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SetAppUserModelId failed", ex);
        }
    }
}
