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
using NetAccel.Managed.Settings;
using NetAccel.Managed.Startup;
using NetAccel.Managed.Vault;
using ServiceLib.Common;
using ServiceLib.Handler;
using System.Net.Http;

namespace v2rayN.Managed.Services;

public sealed class ManagedClientRuntime : IDisposable
{
    private readonly ManagedApiClient _api;
    private readonly WindowsCredentialVault _vault;
    private readonly ConnectionOwnershipCoordinator _ownership;
    private readonly ManagedConnectionCoordinator _connection;
    private readonly ClassicModeLauncher _classicLauncher;
    private bool _disposed;

    private ManagedClientRuntime(
        ManagedApiClient api,
        WindowsCredentialVault vault,
        ConnectionOwnershipCoordinator ownership,
        ManagedConnectionCoordinator connection,
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
        var http = new HttpClient();
        var api = new ManagedApiClient(GetApiBaseUrl(), http);
        var vault = new WindowsCredentialVault();
        var auth = new AuthService(api, vault);
        var installation = new InstallationIdentityService(vault);
        var instance = new InstanceService(api, vault, auth);
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
            CreateCapabilities());
        var ownership = new ConnectionOwnershipCoordinator();
        var coreRunner = new ServiceLibManagedCoreRunner((show, message) =>
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Logging.SaveLog(message);
            }

            return Task.CompletedTask;
        });
        var connection = new ManagedConnectionCoordinator(
            ownership,
            coreRunner,
            selectionService: selection,
            configService: config,
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
            coreRunner.RestoreManagedSystemProxyAsync);

        return new ManagedClientRuntime(
            api,
            vault,
            ownership,
            connection,
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
        var inline = Environment.GetEnvironmentVariable("NETACCEL_SERVER_SIGNING_PUBLIC_KEY");
        if (!string.IsNullOrWhiteSpace(inline))
        {
            return inline.Replace("\\n", Environment.NewLine, StringComparison.Ordinal);
        }

        var path = Environment.GetEnvironmentVariable("NETACCEL_SERVER_SIGNING_PUBLIC_KEY_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
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
