using System.Text.Json;
using AwesomeAssertions;
using NetAccel.Managed.Domain;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Runtime;
using ServiceLib;
using ServiceLib.Enums;
using ServiceLib.Handler.Builder;
using ServiceLib.Manager;
using ServiceLib.Models.Configs;
using ServiceLib.Services.CoreConfig;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedProfileDomainTests
{
    private static readonly string ContractsFixtureRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "..",
        "NetAccel",
        "contracts",
        "fixtures"));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void ProfileSourcePolicy_ManagedProfilesAreReadOnlyButSelectable()
    {
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Connect).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Select).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Diagnose).Should().BeTrue();

        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Edit).Should().BeFalse();
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Delete).Should().BeFalse();
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Copy).Should().BeFalse();
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Share).Should().BeFalse();
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Export).Should().BeFalse();
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Backup).Should().BeFalse();
    }

    [Fact]
    public void ManagedConfigPayload_VlessFixtureDeserializesToStrongTypes()
    {
        var payload = ReadPayloadFixture("managed-config-payload-vless.json");
        var profile = payload.Profiles.Single();

        payload.GetDnsPolicy()!.NormalDns.Should().Be("https://cloudflare-dns.com/dns-query");
        payload.GetRoutingPolicy()!.Mode.Should().Be("managed_default");
        profile.VlessCredentials!.Uuid.Should().Be("00000000-0000-0000-0000-000000000042");
        profile.VlessRealitySecurity!.Type.Should().Be("reality");
        profile.Hysteria2Credentials.Should().BeNull();
        profile.Hysteria2Security.Should().BeNull();
    }

    [Fact]
    public void ManagedConfigPayload_Hysteria2FixtureDeserializesToStrongTypes()
    {
        var payload = ReadPayloadFixture("managed-config-payload-hysteria2.json");
        var profile = payload.Profiles.Single();

        profile.Hysteria2Credentials!.Password.Should().Be("hysteria2-secret");
        profile.Hysteria2Security!.Type.Should().Be("tls");
        profile.Hysteria2Security.ServerName.Should().Be("jp.example.net");
        profile.VlessCredentials.Should().BeNull();
        profile.VlessRealitySecurity.Should().BeNull();
    }

    [Fact]
    public void ManagedProfileValidator_AcceptsVlessRealityAndUpstreamNodeValidator()
    {
        var profile = ReadPayloadFixture("managed-config-payload-vless.json").Profiles.Single();
        var result = ManagedProfileValidator.Validate(profile);

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));

        var node = ManagedProfileAdapter.ToProfileItem(profile);
        node.ConfigType.Should().Be(EConfigType.VLESS);
        node.CoreType.Should().Be(ECoreType.Xray);
        node.Subid.Should().BeEmpty();
        node.Address.Should().Be("hk.example.net");
        node.Password.Should().Be("00000000-0000-0000-0000-000000000042");
        node.StreamSecurity.Should().Be(Global.StreamSecurityReality);
        NodeValidator.Validate(node, ECoreType.Xray).Success.Should().BeTrue();
    }

    [Fact]
    public void ManagedProfileValidator_AcceptsHysteria2AndUpstreamNodeValidator()
    {
        var profile = ReadPayloadFixture("managed-config-payload-hysteria2.json").Profiles.Single();
        var result = ManagedProfileValidator.Validate(profile);

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));

        var node = ManagedProfileAdapter.ToProfileItem(profile);
        node.ConfigType.Should().Be(EConfigType.Hysteria2);
        node.CoreType.Should().Be(ECoreType.sing_box);
        node.Subid.Should().BeEmpty();
        node.Address.Should().Be("jp.example.net");
        node.Password.Should().Be("hysteria2-secret");
        NodeValidator.Validate(node, ECoreType.sing_box).Success.Should().BeTrue();
    }

    [Fact]
    public void ManagedProfileValidator_RejectsInvalidCredentialsButAllowsAvailabilityState()
    {
        var payload = ReadPayloadFixture("managed-config-payload-vless.json");
        var profile = payload.Profiles.Single();
        profile.Available = false;
        profile.VlessCredentials!.Uuid = "not-a-uuid";

        var result = ManagedProfileValidator.Validate(profile);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain("credentials.uuid is invalid");
    }

    [Fact]
    public void ManagedProfileValidator_AcceptsUnavailableProfileAsAssignmentState()
    {
        var profile = ReadPayloadFixture("managed-config-payload-vless.json").Profiles.Single();
        profile.Available = false;

        var result = ManagedProfileValidator.Validate(profile);

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
    }

    [Fact]
    public void ManagedProfileAdapter_RejectsCoreProtocolMismatch()
    {
        var profile = ReadPayloadFixture("managed-config-payload-vless.json").Profiles.Single();
        profile.CorePreference = "sing_box";

        ManagedProfileAdapter.SelectCore(profile).Should().BeNull();
        ManagedProfileValidator.Validate(profile).Success.Should().BeFalse();
    }

    [Fact]
    public void ManagedProfileValidator_RejectsValuesOutsideSchemaEnums()
    {
        var profile = ReadPayloadFixture("managed-config-payload-vless.json").Profiles.Single();
        profile.CorePreference = "auto";
        profile.VlessCredentials!.Flow = "xtls-rprx-direct";
        profile.VlessRealitySecurity!.Fingerprint = "desktop";

        var result = ManagedProfileValidator.Validate(profile);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain("core_preference is incompatible with protocol");
        result.Errors.Should().Contain("credentials.flow is unsupported");
        result.Errors.Should().Contain("security.fingerprint is unsupported");
    }

    [Fact]
    public void ManagedRuntimeConfigBuilder_UsesOnlyManagedPayloadValues()
    {
        var payload = ReadPayloadFixture("managed-config-payload-vless.json");
        var builder = new ManagedRuntimeConfigBuilder();

        var first = builder.Build(payload, socksPort: 10900, httpPort: 10901);
        var second = builder.Build(payload, socksPort: 10900, httpPort: 10901);

        first.Node.IndexId.Should().Be("managed:plan-42");
        first.Node.Subid.Should().BeEmpty();
        first.NormalDns.Should().Be("https://cloudflare-dns.com/dns-query");
        first.TunDns.Should().Be("https://cloudflare-dns.com/dns-query");
        first.SocksPort.Should().Be(10900);
        first.HttpPort.Should().Be(10901);
        first.ToCoreConfigContext().AllProxiesMap.Should().BeEmpty();
        JsonSerializer.Serialize(first.ToCoreConfigContext().Node)
            .Should().Be(JsonSerializer.Serialize(second.ToCoreConfigContext().Node));
    }

    [Fact]
    public void ManagedRuntimeConfigBuilder_VlessRealityContextGeneratesXrayConfig()
    {
        var payload = ReadPayloadFixture("managed-config-payload-vless.json");
        var runtime = new ManagedRuntimeConfigBuilder().Build(payload);
        var context = runtime.ToCoreConfigContext();
        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue(result.Msg);
        var config = result.Data!.ToString()!;
        config.Should().Contain("hk.example.net");
        config.Should().Contain("vless");
        config.Should().Contain("reality");
    }

    [Fact]
    public void ManagedRuntimeConfigBuilder_Hysteria2ContextGeneratesSingboxConfig()
    {
        var payload = ReadPayloadFixture("managed-config-payload-hysteria2.json");
        var runtime = new ManagedRuntimeConfigBuilder().Build(payload);
        var context = runtime.ToCoreConfigContext();
        BindAppManagerConfig(context.AppConfig);

        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue(result.Msg);
        var config = result.Data!.ToString()!;
        config.Should().Contain("jp.example.net");
        config.Should().Contain("hysteria2");
    }

    [Fact]
    public void ManagedRuntimeConfigBuilder_SkipsUnavailableRecommendedProfile()
    {
        var vless = ReadPayloadFixture("managed-config-payload-vless.json").Profiles.Single();
        var hy2 = ReadPayloadFixture("managed-config-payload-hysteria2.json").Profiles.Single();
        vless.Available = false;
        hy2.Id = "plan-43";
        var payload = new ManagedConfigPayload
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 50,
            SelectionRevision = 12,
            RecommendedProfileId = vless.Id,
            FallbackProfileIds = [hy2.Id],
            Profiles = [vless, hy2],
            RoutingPolicy = new ManagedRoutingPolicy { Mode = "managed_default" },
            DnsPolicy = new ManagedDnsPolicy
            {
                NormalDns = "https://dns.google/dns-query",
                TunDns = "https://dns.google/dns-query",
            },
            ClientPolicy = new ClientPolicy { AllowTun = true },
        };

        var runtime = new ManagedRuntimeConfigBuilder().Build(payload);

        runtime.Node.IndexId.Should().Be("managed:plan-43");
        runtime.RunCoreType.Should().Be(ECoreType.sing_box);
    }

    [Fact]
    public void ManagedRuntimeConfigBuilder_RejectsManualUnavailableProfile()
    {
        var payload = ReadPayloadFixture("managed-config-payload-vless.json");
        payload.Profiles.Single().Available = false;

        var act = () => new ManagedRuntimeConfigBuilder().Build(payload, profileId: "plan-42");

        act.Should().Throw<InvalidOperationException>().WithMessage("*not available*");
    }

    [Fact]
    public void ManagedRuntimeConfigBuilder_ClassicConfigMutationDoesNotChangeGeneratedXrayConfig()
    {
        var payload = ReadPayloadFixture("managed-config-payload-vless.json");
        var builder = new ManagedRuntimeConfigBuilder();

        var first = GenerateXrayConfig(builder.Build(payload));
        BindAppManagerConfig(new Config
        {
            CoreBasicItem = new CoreBasicItem { Loglevel = "debug" },
            Inbound = [new InItem { Protocol = "socks", LocalPort = 29999 }],
        });
        var second = GenerateXrayConfig(builder.Build(payload));

        second.Should().Be(first);
    }

    private static ManagedConfigPayload ReadPayloadFixture(string name)
    {
        var path = Path.Combine(ContractsFixtureRoot, name);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ManagedConfigPayload>(json, JsonOptions)!;
    }

    private static string GenerateXrayConfig(ManagedRuntimeConfig runtime)
    {
        var result = new CoreConfigV2rayService(runtime.ToCoreConfigContext()).GenerateClientConfigContent();
        result.Success.Should().BeTrue(result.Msg);
        return result.Data!.ToString()!;
    }

    private static void BindAppManagerConfig(object config)
    {
        var field = typeof(AppManager).GetField("_config", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(AppManager.Instance, config);
    }
}
