using ServiceLib;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Models.Configs;
using ServiceLib.Models.CoreConfigs;
using ServiceLib.Models.Entities;

namespace NetAccel.Managed;

/// <summary>
/// Immutable in-memory snapshot of managed runtime configuration.
/// R0 builds a core configuration context from server-managed inputs only.
/// </summary>
public sealed record ManagedRuntimeConfig
{
    public required ECoreType RunCoreType { get; init; }

    public required ProfileItem Node { get; init; }

    public required string NormalDns { get; init; }
    public required string TunDns { get; init; }

    public RoutingItem? RoutingItem { get; init; }

    public required int SocksPort { get; init; }
    public required int HttpPort { get; init; }

    public bool EnableMux { get; init; }

    public bool EnableTun { get; init; }
    public int TunMtu { get; init; } = 1280;
    public string TunStack { get; init; } = "gvisor";

    public bool IsWindows { get; init; }
    public bool IsMacOS { get; init; }

    /// <summary>
    /// Builds a <see cref="CoreConfigContext"/> from this managed snapshot.
    /// </summary>
    public CoreConfigContext ToCoreConfigContext()
    {
        var config = CreateManagedAppConfig();
        var dnsItem = new DNSItem
        {
            CoreType = RunCoreType,
            NormalDNS = NormalDns,
            TunDNS = TunDns,
        };

        return new CoreConfigContext
        {
            Node = Node,
            RunCoreType = RunCoreType,
            RoutingItem = RoutingItem,
            RawDnsItem = dnsItem,
            SimpleDnsItem = config.SimpleDNSItem,
            AllProxiesMap = new Dictionary<string, ProfileItem>(),
            AppConfig = config,
            FullConfigTemplate = null,
            ServerTestItemMap = new Dictionary<string, string>(),
            IsTunEnabled = EnableTun,
            ProtectDomainList = new HashSet<string>(),
            IsWindows = IsWindows,
            IsMacOS = IsMacOS,
        };
    }

    private Config CreateManagedAppConfig()
    {
#pragma warning disable CS0618
        return new Config
        {
            CoreBasicItem = new CoreBasicItem { Loglevel = "warning" },
            TunModeItem = new TunModeItem
            {
                EnableTun = EnableTun,
                Mtu = TunMtu,
                Stack = TunStack,
                IcmpRouting = "default",
            },
            KcpItem = new KcpItem(),
            GrpcItem = new GrpcItem(),
            RoutingBasicItem = new RoutingBasicItem
            {
                DomainStrategy = Global.AsIs,
                DomainStrategy4Singbox = string.Empty,
                RoutingIndexId = string.Empty,
            },
            GuiItem = new GUIItem { EnableStatistics = false, DisplayRealTimeSpeed = false, EnableLog = false },
            MsgUIItem = new MsgUIItem(),
            UiItem = new UIItem
            {
                CurrentLanguage = "en",
                CurrentFontFamily = "sans",
                MainColumnItem = [],
                WindowSizeItem = [],
            },
            ConstItem = new ConstItem(),
            SpeedTestItem = new SpeedTestItem
            {
                SpeedPingTestUrl = Global.SpeedPingTestUrls.FirstOrDefault() ?? string.Empty,
                SpeedTestUrl = Global.SpeedTestUrls.FirstOrDefault() ?? string.Empty,
                SpeedTestTimeout = 10,
                MixedConcurrencyCount = 1,
                IPAPIUrl = string.Empty,
            },
            Mux4RayItem = new Mux4RayItem { Concurrency = EnableMux ? 8 : -1, XudpConcurrency = 16, XudpProxyUDP443 = "reject" },
            Mux4SboxItem = new Mux4SboxItem { Protocol = Global.SingboxMuxs.FirstOrDefault() ?? string.Empty, MaxConnections = EnableMux ? 8 : 0 },
            HysteriaItem = new HysteriaItem { UpMbps = 100, DownMbps = 100 },
            ClashUIItem = new ClashUIItem { ConnectionsColumnItem = [] },
            SystemProxyItem = new SystemProxyItem { SystemProxyExceptions = string.Empty, SystemProxyAdvancedProtocol = string.Empty },
            WebDavItem = new WebDavItem(),
            CheckUpdateItem = new CheckUpdateItem(),
            Fragment4RayItem = new Fragment4RayItem { Packets = "tlshello", Length = "100-200", Interval = "10-20" },
            Inbound =
            [
                new InItem
                {
                    Protocol = nameof(EInboundProtocol.socks),
                    LocalPort = SocksPort,
                    UdpEnabled = true,
                    SniffingEnabled = true,
                    RouteOnly = false,
                    DestOverride = ["http", "tls"],
                }
            ],
            GlobalHotkeys = [],
            CoreTypeItem =
            [
                new CoreTypeItem { ConfigType = Node.ConfigType, CoreType = RunCoreType }
            ],
            SimpleDNSItem = new SimpleDNSItem
            {
                BootstrapDNS = Global.DomainPureIPDNSAddress.FirstOrDefault() ?? NormalDns,
                ServeStale = false,
                ParallelQuery = false,
                Strategy4Freedom = Global.AsIs,
                Strategy4Proxy = Global.AsIs,
            },
            IndexId = string.Empty,
            SubIndexId = string.Empty,
        };
#pragma warning restore CS0618
    }
}
