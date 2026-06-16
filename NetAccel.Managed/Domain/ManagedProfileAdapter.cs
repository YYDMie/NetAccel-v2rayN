using NetAccel.Managed.Dto;
using ServiceLib;
using ServiceLib.Enums;
using ServiceLib.Models.Entities;

namespace NetAccel.Managed.Domain;

public static class ManagedProfileAdapter
{
    public static ECoreType? SelectCore(ManagedProfile profile)
    {
        var preference = profile.CorePreference.Trim().ToLowerInvariant();
        return profile.Protocol.Trim().ToLowerInvariant() switch
        {
            "vless" when preference is "xray" => ECoreType.Xray,
            "hysteria2" when preference is "sing_box" => ECoreType.sing_box,
            _ => null,
        };
    }

    public static ProfileItem ToProfileItem(ManagedProfile profile, ECoreType? core = null)
    {
        var runCore = core ?? SelectCore(profile)
            ?? throw new InvalidOperationException("Managed profile protocol/core_preference is incompatible.");

        return profile.Protocol.Trim().ToLowerInvariant() switch
        {
            "vless" => ToVlessReality(profile, runCore),
            "hysteria2" => ToHysteria2(profile, runCore),
            _ => throw new NotSupportedException($"Managed protocol '{profile.Protocol}' is not supported."),
        };
    }

    private static ProfileItem ToVlessReality(ManagedProfile profile, ECoreType core)
    {
        var security = profile.VlessRealitySecurity
            ?? throw new InvalidOperationException("VLESS managed profile requires Reality security.");
        var credentials = profile.VlessCredentials
            ?? throw new InvalidOperationException("VLESS managed profile requires VLESS credentials.");

#pragma warning disable CS0618
        var item = new ProfileItem
        {
            IndexId = $"managed:{profile.Id}",
            ConfigType = EConfigType.VLESS,
            CoreType = core,
            Remarks = profile.DisplayName,
            Address = profile.Endpoint.Host,
            Port = profile.Endpoint.Port,
            Password = credentials.Uuid,
            Network = NormalizeTransport(profile.Transport.Network),
            StreamSecurity = Global.StreamSecurityReality,
            Subid = string.Empty,
            Sni = security.ServerName,
            Fingerprint = security.Fingerprint,
            PublicKey = security.PublicKey,
            ShortId = security.ShortId,
            MuxEnabled = false,
        };
#pragma warning restore CS0618

        item.SetProtocolExtra(item.GetProtocolExtra() with
        {
            Flow = credentials.Flow ?? "xtls-rprx-vision",
            VlessEncryption = "none",
        });
        item.SetTransportExtra(ToTransportExtra(profile.Transport));
        return item;
    }

    private static ProfileItem ToHysteria2(ManagedProfile profile, ECoreType core)
    {
        var security = profile.Hysteria2Security
            ?? throw new InvalidOperationException("Hysteria2 managed profile requires TLS security.");
        var credentials = profile.Hysteria2Credentials
            ?? throw new InvalidOperationException("Hysteria2 managed profile requires Hysteria2 credentials.");

#pragma warning disable CS0618
        var item = new ProfileItem
        {
            IndexId = $"managed:{profile.Id}",
            ConfigType = EConfigType.Hysteria2,
            CoreType = core,
            Remarks = profile.DisplayName,
            Address = profile.Endpoint.Host,
            Port = profile.Endpoint.Port,
            Password = credentials.Password,
            Network = NormalizeTransport(profile.Transport.Network),
            StreamSecurity = Global.StreamSecurity,
            AllowInsecure = security.Insecure ? Global.StringTrue : string.Empty,
            Subid = string.Empty,
            Sni = security.ServerName,
            Alpn = security.Alpn is { Count: > 0 } ? string.Join(",", security.Alpn) : string.Empty,
            MuxEnabled = false,
        };
#pragma warning restore CS0618

        item.SetProtocolExtra(item.GetProtocolExtra() with
        {
            UpMbps = 100,
            DownMbps = 100,
        });
        item.SetTransportExtra(ToTransportExtra(profile.Transport));
        return item;
    }

    private static string NormalizeTransport(string? network)
        => string.IsNullOrWhiteSpace(network) ? nameof(ETransport.raw) : network.Trim();

    private static TransportExtraItem ToTransportExtra(TransportInfo transport)
    {
        return new TransportExtraItem
        {
            Host = transport.Host,
            Path = transport.Path,
            GrpcAuthority = transport.Host,
            GrpcServiceName = transport.ServiceName,
        };
    }
}
