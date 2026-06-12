using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using NetAccel.Managed;
using ServiceLib;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Manager;
using ServiceLib.Models.Configs;
using ServiceLib.Models.CoreConfigs;
using ServiceLib.Models.Entities;
using ServiceLib.Services.CoreConfig;
using Xunit;

namespace NetAccel.Managed.Tests;

/// <summary>
/// Tests for T16-R0-13: managed runtime configuration isolation PoC.
/// </summary>
public class ManagedRuntimeConfigTests : IDisposable
{
    public ManagedRuntimeConfigTests()
    {
        BindAppManagerConfig(CreateClassicConfig(10808));
    }

    public void Dispose()
    {
        BindAppManagerConfig(CreateClassicConfig(10808));
    }

    private static void BindAppManagerConfig(Config config)
    {
        var field = typeof(AppManager).GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(AppManager.Instance, config);
    }

    private static Config CreateClassicConfig(int socksPort)
    {
#pragma warning disable CS0618
        return new Config
        {
            CoreBasicItem = new CoreBasicItem { Loglevel = "warning" },
            TunModeItem = new TunModeItem { EnableTun = false, IcmpRouting = "default" },
            KcpItem = new KcpItem(),
            GrpcItem = new GrpcItem(),
            RoutingBasicItem = new RoutingBasicItem { DomainStrategy = Global.AsIs, DomainStrategy4Singbox = string.Empty, RoutingIndexId = string.Empty },
            GuiItem = new GUIItem { EnableStatistics = false, DisplayRealTimeSpeed = false, EnableLog = false },
            MsgUIItem = new MsgUIItem(),
            UiItem = new UIItem { CurrentLanguage = "en", CurrentFontFamily = "sans", MainColumnItem = [], WindowSizeItem = [] },
            ConstItem = new ConstItem(),
            SpeedTestItem = new SpeedTestItem { SpeedPingTestUrl = string.Empty, SpeedTestUrl = string.Empty, SpeedTestTimeout = 10, MixedConcurrencyCount = 1, IPAPIUrl = string.Empty },
            Mux4RayItem = new Mux4RayItem { Concurrency = 8, XudpConcurrency = 16, XudpProxyUDP443 = "reject" },
            Mux4SboxItem = new Mux4SboxItem { Protocol = string.Empty, MaxConnections = 8 },
            HysteriaItem = new HysteriaItem { UpMbps = 100, DownMbps = 100 },
            ClashUIItem = new ClashUIItem { ConnectionsColumnItem = [] },
            SystemProxyItem = new SystemProxyItem { SystemProxyExceptions = string.Empty, SystemProxyAdvancedProtocol = string.Empty },
            WebDavItem = new WebDavItem(),
            CheckUpdateItem = new CheckUpdateItem(),
            Fragment4RayItem = new Fragment4RayItem(),
            Inbound =
            [
                new InItem
                {
                    Protocol = nameof(EInboundProtocol.socks),
                    LocalPort = socksPort,
                    UdpEnabled = true,
                    SniffingEnabled = true,
                    RouteOnly = false,
                    DestOverride = ["http", "tls"],
                }
            ],
            GlobalHotkeys = [],
            CoreTypeItem = [],
            SimpleDNSItem = new SimpleDNSItem
            {
                BootstrapDNS = "8.8.8.8",
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

    private static ProfileItem CreateVlessRealityNode()
    {
#pragma warning disable CS0618
        var node = new ProfileItem
        {
            IndexId = "managed-reality-1",
            ConfigType = EConfigType.VLESS,
            CoreType = ECoreType.Xray,
            Remarks = "Managed VLESS Reality",
            Address = "reality.example.com",
            Port = 443,
            Password = "00000000-0000-0000-0000-000000000001",
            Network = nameof(ETransport.raw),
            StreamSecurity = Global.StreamSecurityReality,
            Subid = string.Empty,
            Sni = "www.example.com",
            Fingerprint = "chrome",
            PublicKey = "aW1wb3J0YW50LXB1YmxpYy1rZXk",
            ShortId = "0123456789abcdef",
        };
        node.SetProtocolExtra(node.GetProtocolExtra() with
        {
            Flow = "xtls-rprx-vision",
            VlessEncryption = "none",
        });
        return node;
#pragma warning restore CS0618
    }

    private static ProfileItem CreateHysteria2Node()
    {
#pragma warning disable CS0618
        var node = new ProfileItem
        {
            IndexId = "managed-hy2-1",
            ConfigType = EConfigType.Hysteria2,
            CoreType = ECoreType.sing_box,
            Remarks = "Managed Hysteria2",
            Address = "hy2.example.com",
            Port = 443,
            Password = "test-password",
            Network = nameof(ETransport.raw),
            StreamSecurity = Global.StreamSecurity,
            Subid = string.Empty,
        };
        node.SetProtocolExtra(node.GetProtocolExtra() with
        {
            UpMbps = 100,
            DownMbps = 100,
        });
        return node;
#pragma warning restore CS0618
    }

    private static ManagedRuntimeConfig CreateXrayRealityConfig()
    {
        return new ManagedRuntimeConfig
        {
            RunCoreType = ECoreType.Xray,
            Node = CreateVlessRealityNode(),
            NormalDns = "https://cloudflare-dns.com/dns-query,8.8.8.8",
            TunDns = "https://cloudflare-dns.com/dns-query",
            RoutingItem = CreateManagedRoutingItem(),
            SocksPort = 10808,
            HttpPort = 10809,
            EnableMux = false,
            EnableTun = false,
            IsWindows = true,
            IsMacOS = false,
        };
    }

    private static ManagedRuntimeConfig CreateSingboxHy2Config()
    {
        return new ManagedRuntimeConfig
        {
            RunCoreType = ECoreType.sing_box,
            Node = CreateHysteria2Node(),
            NormalDns = "https://cloudflare-dns.com/dns-query,8.8.8.8",
            TunDns = "https://cloudflare-dns.com/dns-query",
            RoutingItem = CreateManagedRoutingItem(),
            SocksPort = 10808,
            HttpPort = 10809,
            EnableMux = false,
            EnableTun = false,
            IsWindows = true,
            IsMacOS = false,
        };
    }

    private static RoutingItem CreateManagedRoutingItem()
    {
        return new RoutingItem
        {
            Id = "managed-routing",
            Remarks = "managed-default",
            RuleSet = "[]",
            DomainStrategy = Global.AsIs,
            DomainStrategy4Singbox = string.Empty,
        };
    }

    [Fact]
    public void ManagedRuntimeConfig_XrayReality_ShouldGenerateValidConfig()
    {
        var managed = CreateXrayRealityConfig();
        var context = managed.ToCoreConfigContext();

        context.Node.Should().BeSameAs(managed.Node);
        context.RunCoreType.Should().Be(ECoreType.Xray);
        context.IsTunEnabled.Should().BeFalse();

        var config = GenerateXrayConfig(managed);
        var root = ParseJsonObject(config);

        root.TryGetProperty("inbounds", out var inbounds).Should().BeTrue();
        inbounds.ValueKind.Should().Be(JsonValueKind.Array);
        root.TryGetProperty("outbounds", out var outbounds).Should().BeTrue();
        outbounds.ValueKind.Should().Be(JsonValueKind.Array);
        config.Should().Contain("reality.example.com");
        config.Should().Contain("vless");
    }

    [Fact]
    public void ManagedRuntimeConfig_SingboxHy2_ShouldGenerateValidConfig()
    {
        var managed = CreateSingboxHy2Config();
        var context = managed.ToCoreConfigContext();
        BindAppManagerConfig(context.AppConfig);

        context.RunCoreType.Should().Be(ECoreType.sing_box);

        var service = new CoreConfigSingboxService(context);
        var result = service.GenerateClientConfigContent();

        result.Success.Should().BeTrue(result.Msg);
        var config = result.Data!.ToString()!;
        var root = ParseJsonObject(config);

        root.TryGetProperty("inbounds", out var inbounds).Should().BeTrue();
        inbounds.ValueKind.Should().Be(JsonValueKind.Array);
        root.TryGetProperty("outbounds", out var outbounds).Should().BeTrue();
        outbounds.ValueKind.Should().Be(JsonValueKind.Array);
        config.Should().Contain("hy2.example.com");
        config.Should().Contain("hysteria2");
    }

    [Fact]
    public void ManagedRuntimeConfig_Isolation_ClassicConfigMutationDoesNotAffectManagedXray()
    {
        var managed = CreateXrayRealityConfig();
        var config1 = GenerateXrayConfig(managed);

        BindAppManagerConfig(CreateClassicConfig(20808));

        var config2 = GenerateXrayConfig(managed);

        config2.Should().Be(config1,
            "managed Xray output must not change when classic AppManager Config is mutated");
    }

    [Fact]
    public void ManagedRuntimeConfig_ShouldNotCreateSubItem()
    {
        var managed = CreateXrayRealityConfig();
        var context = managed.ToCoreConfigContext();

        context.Node.Subid.Should().BeEmpty("managed nodes should not reference subscription IDs");
        context.AllProxiesMap.Should().BeEmpty("managed context should not contain locally loaded proxies");
    }

    [Fact]
    public void ManagedRuntimeConfig_DnsShouldComeFromManagedValues()
    {
        var managed = CreateXrayRealityConfig();
        var context = managed.ToCoreConfigContext();

        context.RawDnsItem.Should().NotBeNull();
        context.RawDnsItem!.NormalDNS.Should().Be(managed.NormalDns);
        context.RawDnsItem.TunDNS.Should().Be(managed.TunDns);
    }

    [Fact]
    public void ManagedUpdateService_ShouldAlwaysCarryNetAccelProduct()
    {
        var svc = new ManagedUpdateService();
        var request = svc.BuildManifestRequest("windows", "x86_64", "stable");

        request.ClientProduct.Should().Be("netaccel-v2rayn-wpf");
        request.Platform.Should().Be("windows");
        request.Arch.Should().Be("x86_64");
        request.Channel.Should().Be("stable");
    }

    [Fact]
    public void ManagedUpdateService_ShouldRejectInvalidChannel()
    {
        var svc = new ManagedUpdateService();
        var act = () => svc.BuildManifestRequest("windows", "x86_64", "invalid");
        act.Should().Throw<ArgumentException>().WithMessage("*channel*");
    }

    [Fact]
    public void ManagedUpdateService_QueryString_ShouldContainClientProduct()
    {
        var svc = new ManagedUpdateService();
        var request = svc.BuildManifestRequest("windows", "x86_64", "stable");
        var qs = request.ToQueryString();

        qs.Should().Contain("client_product=netaccel-v2rayn-wpf");
        qs.Should().Contain("platform=windows");
        qs.Should().Contain("arch=x86_64");
        qs.Should().Contain("channel=stable");
    }

    [Fact]
    public void ManagedUpdateService_MustNotDownloadOrInstall()
    {
        var svc = new ManagedUpdateService();
        var methods = svc.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);

        var downloadMethods = methods.Where(m =>
            m.Name.Contains("Download", StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains("Install", StringComparison.OrdinalIgnoreCase))
            .ToList();

        downloadMethods.Should().BeEmpty("R0 ManagedUpdateService must not download or install");
    }

    [Fact]
    public void ManagedAssembly_ShouldNotReferenceWpf()
    {
        var assembly = typeof(ManagedRuntimeConfig).Assembly;
        var refs = assembly.GetReferencedAssemblies().Select(r => r.Name).ToList();

        refs.Should().NotContain(name => name!.Contains("PresentationFramework"),
            "NetAccel.Managed must not reference WPF");
        refs.Should().NotContain(name => name!.Contains("PresentationCore"),
            "NetAccel.Managed must not reference WPF");
        refs.Should().NotContain(name => name!.Contains("WindowsBase"),
            "NetAccel.Managed must not reference WPF");
    }

    [Fact]
    public void ManagedRuntimeConfig_Source_ShouldNotReferenceClassicStateLoaders()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "NetAccel.Managed",
            "ManagedRuntimeConfig.cs"));
        var source = File.ReadAllText(sourcePath);

        source.Should().NotContain("AppManager");
        source.Should().NotContain("SQLiteHelper");
        source.Should().NotContain("ConfigHandler");
        source.Should().NotContain("CoreConfigContextBuilder");
        source.Should().NotContain("AddBatchServers");
        source.Should().NotContain("SubItem");
    }

    private static string GenerateXrayConfig(ManagedRuntimeConfig managed)
    {
        var context = managed.ToCoreConfigContext();
        var service = new CoreConfigV2rayService(context);
        var result = service.GenerateClientConfigContent();

        result.Success.Should().BeTrue(result.Msg);
        return result.Data!.ToString()!;
    }

    private static JsonElement ParseJsonObject(string config)
    {
        using var json = JsonDocument.Parse(config);
        return json.RootElement.Clone();
    }
}
