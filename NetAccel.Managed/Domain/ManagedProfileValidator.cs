using NetAccel.Managed.Dto;
using ServiceLib;
using ServiceLib.Enums;
using ServiceLib.Handler.Builder;

namespace NetAccel.Managed.Domain;

public sealed record ManagedProfileValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool Success => Errors.Count == 0;

    public static ManagedProfileValidationResult Ok() => new([], []);
}

public static class ManagedProfileValidator
{
    private static readonly HashSet<string> SupportedTransports =
    [
        nameof(ETransport.raw),
        nameof(ETransport.ws),
        nameof(ETransport.grpc)
    ];

    private static readonly HashSet<string> SupportedFingerprints =
    [
        "chrome",
        "firefox",
        "safari",
        "edge",
        "ios",
        "android",
        "random",
        "randomized"
    ];

    public static ManagedProfileValidationResult Validate(ManagedProfile profile)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        ValidateCommon(profile, errors);
        var core = ManagedProfileAdapter.SelectCore(profile);
        if (core == null)
        {
            errors.Add("core_preference is incompatible with protocol");
        }

        if (errors.Count == 0)
        {
            var node = ManagedProfileAdapter.ToProfileItem(profile, core!.Value);
            var upstream = NodeValidator.Validate(node, core.Value);
            errors.AddRange(upstream.Errors);
            warnings.AddRange(upstream.Warnings);
        }

        return new ManagedProfileValidationResult(errors, warnings);
    }

    public static ManagedProfileValidationResult ValidatePayload(ManagedConfigPayload payload)
    {
        var errors = new List<string>();
        if (!payload.PayloadSchema.Equals("managed-config/v1", StringComparison.Ordinal))
        {
            errors.Add("payload_schema must be managed-config/v1");
        }

        if (payload.Profiles.Count == 0)
        {
            errors.Add("profiles must not be empty");
        }

        foreach (var profile in payload.Profiles)
        {
            var result = Validate(profile);
            errors.AddRange(result.Errors.Select(e => $"{profile.Id}: {e}"));
        }

        return errors.Count == 0 ? ManagedProfileValidationResult.Ok() : new ManagedProfileValidationResult(errors, []);
    }

    private static void ValidateCommon(ManagedProfile profile, List<string> errors)
    {
        Require(!string.IsNullOrWhiteSpace(profile.Id), "id is required", errors);
        Require(profile.Revision >= 0, "revision must not be negative", errors);
        Require(!string.IsNullOrWhiteSpace(profile.DisplayName), "display_name is required", errors);
        Require(!string.IsNullOrWhiteSpace(profile.Endpoint.Host), "endpoint.host is required", errors);
        Require(profile.Endpoint.Port is > 0 and <= 65535, "endpoint.port is invalid", errors);
        Require(SupportedTransports.Contains(profile.Transport.Network), "transport.network is unsupported", errors);

        switch (profile.Protocol.ToLowerInvariant())
        {
            case "vless":
                ValidateVless(profile, errors);
                break;
            case "hysteria2":
                ValidateHysteria2(profile, errors);
                break;
            default:
                errors.Add("protocol is unsupported");
                break;
        }
    }

    private static void ValidateVless(ManagedProfile profile, List<string> errors)
    {
        Require(profile.VlessCredentials != null, "credentials must be vless credentials", errors);
        Require(Guid.TryParse(profile.VlessCredentials?.Uuid, out _), "credentials.uuid is invalid", errors);
        Require(profile.VlessCredentials?.Flow is null or "" or "xtls-rprx-vision", "credentials.flow is unsupported", errors);
        Require(profile.VlessRealitySecurity != null, "security must be reality", errors);
        Require(profile.VlessRealitySecurity?.Type == "reality", "security.type must be reality", errors);
        Require(!string.IsNullOrWhiteSpace(profile.VlessRealitySecurity?.ServerName), "security.server_name is required", errors);
        Require(!string.IsNullOrWhiteSpace(profile.VlessRealitySecurity?.PublicKey), "security.public_key is required", errors);
        Require(profile.VlessRealitySecurity?.ShortId.Length <= 16, "security.short_id is too long", errors);
        Require(!string.IsNullOrWhiteSpace(profile.VlessRealitySecurity?.Fingerprint), "security.fingerprint is required", errors);
        Require(profile.VlessRealitySecurity?.Fingerprint is string fp && SupportedFingerprints.Contains(fp), "security.fingerprint is unsupported", errors);
    }

    private static void ValidateHysteria2(ManagedProfile profile, List<string> errors)
    {
        Require(profile.Hysteria2Credentials != null, "credentials must be hysteria2 credentials", errors);
        Require(!string.IsNullOrWhiteSpace(profile.Hysteria2Credentials?.Password), "credentials.password is required", errors);
        Require(profile.Hysteria2Security != null, "security must be tls", errors);
        Require(profile.Hysteria2Security?.Type == "tls", "security.type must be tls", errors);
        Require(!string.IsNullOrWhiteSpace(profile.Hysteria2Security?.ServerName), "security.server_name is required", errors);
    }

    private static void Require(bool condition, string message, List<string> errors)
    {
        if (!condition)
        {
            errors.Add(message);
        }
    }
}
