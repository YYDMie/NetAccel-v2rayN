using ServiceLib.Common;

namespace NetAccel.Managed;

/// <summary>
/// NetAccel managed update service.
/// R0 only constructs and validates a product-scoped manifest request.
/// It must not download or install anything.
/// </summary>
public sealed class ManagedUpdateService
{
    /// <summary>
    /// The fixed client_product used for all NetAccel WPF manifest requests.
    /// </summary>
    public string ClientProduct => NetAccelIdentity.NetAccelClientProduct;

    /// <summary>
    /// Builds manifest request URL query parameters for the given platform/arch/channel.
    /// </summary>
    public ManifestRequest BuildManifestRequest(string platform, string arch, string channel)
    {
        if (string.IsNullOrWhiteSpace(platform))
            throw new ArgumentException("platform is required", nameof(platform));
        if (string.IsNullOrWhiteSpace(arch))
            throw new ArgumentException("arch is required", nameof(arch));
        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("channel is required", nameof(channel));

        var validChannels = new[] { "stable", "beta", "dev" };
        if (!validChannels.Contains(channel.ToLowerInvariant()))
            throw new ArgumentException($"channel must be one of: {string.Join(", ", validChannels)}", nameof(channel));

        return new ManifestRequest
        {
            ClientProduct = ClientProduct,
            Platform = platform.ToLowerInvariant(),
            Arch = arch.ToLowerInvariant(),
            Channel = channel.ToLowerInvariant(),
        };
    }
}

/// <summary>
/// Represents a validated manifest request for a NetAccel client product.
/// </summary>
public sealed record ManifestRequest
{
    public required string ClientProduct { get; init; }
    public required string Platform { get; init; }
    public required string Arch { get; init; }
    public required string Channel { get; init; }

    /// <summary>
    /// Returns the query string for the manifest request.
    /// </summary>
    public string ToQueryString()
        => $"client_product={Uri.EscapeDataString(ClientProduct)}" +
           $"&platform={Uri.EscapeDataString(Platform)}" +
           $"&arch={Uri.EscapeDataString(Arch)}" +
           $"&channel={Uri.EscapeDataString(Channel)}";
}
