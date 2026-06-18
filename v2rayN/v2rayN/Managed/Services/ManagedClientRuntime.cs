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
using NetAccel.Managed.Startup;
using NetAccel.Managed.Vault;
using ServiceLib.Common;
using System.Net.Http;

namespace v2rayN.Managed.Services;

public sealed class ManagedClientRuntime : IDisposable
{
    private readonly ManagedApiClient _api;
    private readonly WindowsCredentialVault _vault;
    private readonly ConnectionOwnershipCoordinator _ownership;
    private readonly ManagedConnectionCoordinator _connection;
    private bool _disposed;

    private ManagedClientRuntime(
        ManagedApiClient api,
        WindowsCredentialVault vault,
        ConnectionOwnershipCoordinator ownership,
        ManagedConnectionCoordinator connection,
        ManagedLoginViewModel loginViewModel,
        ManagedHomeViewModel homeViewModel,
        ManagedRoutesViewModel routesViewModel)
    {
        _api = api;
        _vault = vault;
        _ownership = ownership;
        _connection = connection;
        LoginViewModel = loginViewModel;
        HomeViewModel = homeViewModel;
        RoutesViewModel = routesViewModel;
    }

    public ManagedLoginViewModel LoginViewModel { get; }
    public ManagedHomeViewModel HomeViewModel { get; }
    public ManagedRoutesViewModel RoutesViewModel { get; }

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
        var connection = new ManagedConnectionCoordinator(
            ownership,
            new ServiceLibManagedCoreRunner((show, message) =>
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    Logging.SaveLog(message);
                }

                return Task.CompletedTask;
            }),
            selectionService: selection,
            configService: config,
            clientVersion: Utils.GetVersionInfo(),
            coreVersions: new Dictionary<string, string>());

        var home = new ManagedHomeViewModel(connection, startup.GetCurrentConfigAsync);
        var routes = new ManagedRoutesViewModel(
            selection,
            connection,
            startup.GetCurrentConfigAsync,
            connection.SwitchAsync,
            home.UpdateSelectionPreference);

        return new ManagedClientRuntime(
            api,
            vault,
            ownership,
            connection,
            new ManagedLoginViewModel(auth, startup),
            home,
            routes);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RoutesViewModel.Dispose();
        HomeViewModel.Dispose();
        LoginViewModel.Dispose();
        Task.Run(async () =>
        {
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
