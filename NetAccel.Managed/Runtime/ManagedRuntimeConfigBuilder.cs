using NetAccel.Managed.Domain;
using NetAccel.Managed.Dto;
using ServiceLib;
using ServiceLib.Models.Configs;
using ServiceLib.Models.Entities;

namespace NetAccel.Managed.Runtime;

public sealed class ManagedRuntimeConfigBuilder
{
    public ManagedRuntimeConfig Build(
        ManagedConfigPayload payload,
        string? profileId = null,
        int socksPort = 10808,
        int httpPort = 10809,
        bool isWindows = true,
        bool isMacOS = false)
    {
        var validation = ManagedProfileValidator.ValidatePayload(payload);
        if (!validation.Success)
        {
            throw new InvalidOperationException(string.Join("; ", validation.Errors));
        }

        var selectedProfile = SelectProfile(payload, profileId);
        var core = ManagedProfileAdapter.SelectCore(selectedProfile)
            ?? throw new InvalidOperationException("Selected profile has no compatible core.");

        var dnsPolicy = payload.GetDnsPolicy();
        var routingPolicy = payload.GetRoutingPolicy();

        return new ManagedRuntimeConfig
        {
            RunCoreType = core,
            Node = ManagedProfileAdapter.ToProfileItem(selectedProfile, core),
            NormalDns = dnsPolicy?.NormalDns ?? "https://cloudflare-dns.com/dns-query",
            TunDns = dnsPolicy?.TunDns ?? dnsPolicy?.NormalDns ?? "https://cloudflare-dns.com/dns-query",
            RoutingItem = new RoutingItem
            {
                Id = "managed-routing",
                Remarks = routingPolicy?.Mode ?? "managed_default",
                RuleSet = "[]",
                DomainStrategy = Global.AsIs,
                DomainStrategy4Singbox = string.Empty,
            },
            SocksPort = socksPort,
            HttpPort = httpPort,
            EnableMux = false,
            EnableTun = payload.ClientPolicy?.AllowTun == true && selectedProfile.Policy.AllowTun,
            IsWindows = isWindows,
            IsMacOS = isMacOS,
        };
    }

    private static ManagedProfile SelectProfile(ManagedConfigPayload payload, string? profileId)
    {
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            var requested = payload.Profiles.FirstOrDefault(p => p.Id == profileId)
                ?? throw new InvalidOperationException("Requested managed profile is not assigned.");
            if (!requested.Available)
            {
                throw new InvalidOperationException("Requested managed profile is not available.");
            }
            return requested;
        }

        ManagedProfile? profile = null;
        if (!string.IsNullOrWhiteSpace(payload.RecommendedProfileId))
        {
            profile = payload.Profiles.FirstOrDefault(p => p.Id == payload.RecommendedProfileId && p.Available);
        }

        profile ??= payload.FallbackProfileIds
            .Select(id => payload.Profiles.FirstOrDefault(p => p.Id == id && p.Available))
            .FirstOrDefault(p => p != null);

        profile ??= payload.Profiles
            .Where(p => p.Available)
            .OrderByDescending(p => p.Recommended)
            .ThenByDescending(p => p.Priority)
            .FirstOrDefault();

        return profile ?? throw new InvalidOperationException("No managed profile is available for runtime.");
    }
}
