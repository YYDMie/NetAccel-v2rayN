using NetAccel.Managed.Api;
using NetAccel.Managed.Auth;
using NetAccel.Managed.Cache;
using NetAccel.Managed.Crypto;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Identity;
using NetAccel.Managed.Instance;
using NetAccel.Managed.Presentation;
using NetAccel.Managed.Runtime;
using NetAccel.Managed.Selection;
using NetAccel.Managed.Session;
using NetAccel.Managed.Settings;
using NetAccel.Managed.Startup;
using NetAccel.Managed.Update;
using NetAccel.Managed.Vault;
using ServiceLib.Common;
using ServiceLib.Handler;
using System.Net.Http;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace v2rayN.Managed.Services;

public sealed class ManagedClientRuntime : IDisposable
{
    private readonly ManagedApiClient _api;
    private readonly WindowsCredentialVault _vault;
    private readonly ConnectionOwnershipCoordinator _ownership;
    private readonly ManagedConnectionCoordinator _connection;
    private readonly ManagedSessionReporter _sessionReporter;
    private readonly ClassicModeLauncher _classicLauncher;
    private bool _disposed;

    private ManagedClientRuntime(
        ManagedApiClient api,
        WindowsCredentialVault vault,
        ConnectionOwnershipCoordinator ownership,
        ManagedConnectionCoordinator connection,
        ManagedSessionReporter sessionReporter,
        ManagedLoginViewModel loginViewModel,
        ManagedHomeViewModel homeViewModel,
        ManagedRoutesViewModel routesViewModel,
        ManagedActivityViewModel activityViewModel,
        ManagedDiagnosticsViewModel diagnosticsViewModel,
        ManagedSettingsViewModel settingsViewModel,
        ManagedTrayViewModel trayViewModel,
        ClassicModeLauncher classicLauncher)
    {
        _api = api;
        _vault = vault;
        _ownership = ownership;
        _connection = connection;
        _sessionReporter = sessionReporter;
        LoginViewModel = loginViewModel;
        HomeViewModel = homeViewModel;
        RoutesViewModel = routesViewModel;
        ActivityViewModel = activityViewModel;
        DiagnosticsViewModel = diagnosticsViewModel;
        SettingsViewModel = settingsViewModel;
        TrayViewModel = trayViewModel;
        _classicLauncher = classicLauncher;
    }

    public ManagedLoginViewModel LoginViewModel { get; }
    public ManagedHomeViewModel HomeViewModel { get; }
    public ManagedRoutesViewModel RoutesViewModel { get; }
    public ManagedActivityViewModel ActivityViewModel { get; }
    public ManagedDiagnosticsViewModel DiagnosticsViewModel { get; }
    public ManagedSettingsViewModel SettingsViewModel { get; }
    public ManagedTrayViewModel TrayViewModel { get; }

    public static ManagedClientRuntime Create()
    {
        PackagedCoreBootstrapper.Install(AppContext.BaseDirectory, Utils.StartupPath());
        var http = new HttpClient();
        var api = new ManagedApiClient(GetApiBaseUrl(), http);
        var vault = new WindowsCredentialVault();
        static Task LogManagedAsync(string message)
        {
            Logging.SaveLog($"[Managed] {message}");
            return Task.CompletedTask;
        }
        var auth = new AuthService(api, vault, LogManagedAsync);
        var installation = new InstallationIdentityService(vault);
        var instance = new InstanceService(api, vault, auth, LogManagedAsync);
        var selection = new ManagedSelectionService(api, vault, auth);
        var config = new ManagedConfigService(api, vault, auth);
        var deviceKeys = new DeviceKeyManager(vault);
        var cache = new EnvelopeCacheManager(Path.Combine(Utils.StartupPath(), "managed-cache"));
        var offlineRules = new OfflineConfigRules();
        var serverKeys = new StaticServerKeyProvider(ReadServerSigningPublicKey());
        var startup = new ManagedStartupOrchestrator(
            auth,
            installation,
            instance,
            selection,
            config,
            deviceKeys,
            cache,
            offlineRules,
            serverKeys,
            api,
            vault,
            Utils.GetVersionInfo(),
            new Dictionary<string, string>(),
            CreateCapabilities(),
            LogManagedAsync);
        var ownership = new ConnectionOwnershipCoordinator();
        var sessionReporter = new ManagedSessionReporter(
            new ManagedSessionApiTransport(api, instance),
            new FileManagedSessionOutboxStore(
                Path.Combine(Utils.StartupPath(), "managed-runtime", "session-outbox.json")),
            logAsync: message =>
            {
                Logging.SaveLog(message);
                return Task.CompletedTask;
            });
        sessionReporter.StartBackgroundWork();
        var proxyRecoveryStore = new ManagedSystemProxyRecoveryStore(
            Path.Combine(Utils.StartupPath(), "managed-runtime", "system-proxy-recovery.json"));
        var coreRunner = new ServiceLibManagedCoreRunner((show, message) =>
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Logging.SaveLog(message);
            }

            return Task.CompletedTask;
        }, proxyRecoveryStore);
        var connection = new ManagedConnectionCoordinator(
            ownership,
            coreRunner,
            selectionService: selection,
            configService: config,
            sessionReporter: sessionReporter,
            clientVersion: Utils.GetVersionInfo(),
            coreVersions: new Dictionary<string, string>());

        var login = new ManagedLoginViewModel(auth, startup);
        var home = new ManagedHomeViewModel(connection, startup.GetCurrentConfigAsync);
        var routes = new ManagedRoutesViewModel(
            selection,
            connection,
            startup.GetCurrentConfigAsync,
            connection.SwitchAsync,
            home.UpdateSelectionPreference);
        var diagnostics = new ManagedDiagnosticsService(
            connection,
            coreRunner,
            ownership,
            () => login.IsReady,
            startup.GetCurrentConfigAsync,
            async ct =>
            {
                await login.RetryAsync(ct);
                return login.IsReady;
            },
            clientVersion: Utils.GetVersionInfo());
        var classicLauncher = new ClassicModeLauncher(ownership, connection);
        var preferences = new ManagedPreferencesStore();
        var settings = new ManagedSettingsViewModel(
            preferences,
            () => AppManager.Instance.Config.GuiItem.AutoRun,
            async (enabled, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppManager.Instance.Config.GuiItem.AutoRun = enabled;
                if (await ConfigHandler.SaveConfig(AppManager.Instance.Config) != 0)
                {
                    return false;
                }

                return await AutoStartupHandler.UpdateTask(AppManager.Instance.Config);
            },
            ct => auth.GetAccountIdAsync(),
            async ct =>
            {
                if (connection.Status.State is ManagedConnectionState.Connected
                    or ManagedConnectionState.Starting
                    or ManagedConnectionState.Stopping
                    or ManagedConnectionState.Faulted)
                {
                    await connection.StopAsync(ct);
                }

                await login.ReturnToLoginAsync(ct);
            },
            classicLauncher.TryHandoffToClassicAsync,
            coreRunner.RestoreManagedSystemProxyAsync,
            async ct =>
            {
                var masterOrigin = new Uri(GetApiBaseUrl());
                var trust = new DirectoryManagedReleaseTrustStore(
                    Path.Combine(AppContext.BaseDirectory, "release-trust"));
                var updateClient = new ManagedUpdateClient(http, new ManagedReleaseVerifier(trust), masterOrigin);
                var stage = await updateClient.StageLatestAsync(
                    "windows",
                    RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "aarch64" : "x86_64",
                    "stable",
                    Path.Combine(Path.GetTempPath(), "NetAccel", "update-staging"),
                    ct);
                if (!stage.Success || stage.Update == null)
                {
                    return $"更新未安装：{stage.ErrorCode}";
                }

                var installer = new ManagedVersionedInstaller(
                    Path.Combine(Path.GetTempPath(), "NetAccel", "prepared-updates"));
                var prepared = await installer.PrepareAsync(
                    stage.Update,
                    Utils.GetVersionInfo(),
                    DateTimeOffset.UtcNow,
                    TimeSpan.FromMinutes(2),
                    ct);
                if (!prepared.Success || prepared.LaunchDirectory == null)
                {
                    return $"更新未安装：{prepared.ErrorCode}";
                }

                if (connection.Status.State is ManagedConnectionState.Connected
                    or ManagedConnectionState.Starting
                    or ManagedConnectionState.Stopping
                    or ManagedConnectionState.Faulted)
                {
                    await connection.StopAsync(ct);
                }

                var handoff = await new ManagedUpdateHandoff().LaunchAsync(
                    Path.Combine(Utils.StartupPath(), "updater"),
                    prepared.LaunchDirectory,
                    Utils.StartupPath(),
                    stage.Update.Manifest.Version,
                    Process.GetCurrentProcess().Id,
                    ct);
                if (!handoff.Success)
                {
                    return $"更新未安装：{handoff.ErrorCode}";
                }

                _ = Application.Current.Dispatcher.BeginInvoke(Application.Current.Shutdown);
                return "更新已验证，正在安全重启";
            });

        return new ManagedClientRuntime(
            api,
            vault,
            ownership,
            connection,
            sessionReporter,
            login,
            home,
            routes,
            new ManagedActivityViewModel(connection),
            new ManagedDiagnosticsViewModel(diagnostics),
            settings,
            new ManagedTrayViewModel(connection),
            classicLauncher);
    }

    public Task<ClassicModeHandoffResult> EnterClassicModeAsync(CancellationToken ct = default)
        => SettingsViewModel.PrepareClassicModeAsync(ct);

    public Task LeaveClassicModeAsync()
        => _classicLauncher.ReleaseClassicAsync();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RoutesViewModel.Dispose();
        ActivityViewModel.Dispose();
        DiagnosticsViewModel.Dispose();
        TrayViewModel.Dispose();
        HomeViewModel.Dispose();
        LoginViewModel.Dispose();
        Task.Run(async () =>
        {
            await _classicLauncher.DisposeAsync();
            await ManagedExitCleanup.RunExitCleanupAsync(_ownership, _connection);
            await _ownership.DisposeAsync();
            await _sessionReporter.DisposeAsync();
        }).GetAwaiter().GetResult();
        ManagedExitCleanup.Unregister();
        _api.Dispose();
        _vault.Dispose();
    }

    private static string GetApiBaseUrl()
    {
        var configured = Environment.GetEnvironmentVariable("NETACCEL_API_BASE_URL");
        return string.IsNullOrWhiteSpace(configured)
            ? "https://netaccel.jklsp.dynv6.net"
            : configured.Trim();
    }

    private static string? ReadServerSigningPublicKey()
    {
        return ManagedServerTrustLoader.Load(
            AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable("NETACCEL_SERVER_SIGNING_PUBLIC_KEY"),
            Environment.GetEnvironmentVariable("NETACCEL_SERVER_SIGNING_PUBLIC_KEY_PATH"));
    }

    private static ClientCapabilities CreateCapabilities()
    {
        return new ClientCapabilities
        {
            Tun = true,
            SystemProxy = true,
            ProcessRule = true,
            ClashApi = false,
            V2rayStats = true,
            LatencyProbeTcp = true,
            LatencyProbeUdp = true,
            SecureStorage = true,
        };
    }
}
