namespace ServiceLib.Common;

/// <summary>
/// Application identity abstraction for NetAccel.
/// Upstream v2rayN consumers that never call <see cref="Configure"/> keep the original
/// v2rayN defaults. The WPF NetAccel host calls <see cref="Configure"/> once before
/// paths, logging, storage, or UI initialize.
/// </summary>
public sealed class NetAccelIdentity
{
    public const string NetAccelClientProduct = "netaccel-v2rayn-wpf";
    public const string NetAccelDisplayName = "NetAccel";
    public const string NetAccelAppUserModelID = "NetAccel.v2rayN.WPF";
    public const string NetAccelSingleInstanceName = @"Local\NetAccel.v2rayN.WPF.SingleInstance";
    public const string NetAccelAutoRunBaseName = "NetAccelAutoRun";
    public const string NetAccelUrlProtocol = "netaccel";
    public const string NetAccelInstallDirContract = @"%LOCALAPPDATA%\Programs\NetAccel";
    public const string NetAccelUninstallIdentity = "NetAccel.v2rayN.WPF";

    private static readonly Lazy<NetAccelIdentity> _defaultUpstream = new(() => new NetAccelIdentity());
    private static NetAccelIdentity? _active;

    /// <summary>
    /// The active identity. Defaults to upstream v2rayN values until <see cref="Configure"/> is called.
    /// </summary>
    public static NetAccelIdentity Active => _active ?? _defaultUpstream.Value;

    /// <summary>
    /// True when the active identity is the fixed NetAccel product identity.
    /// </summary>
    public static bool IsNetAccel => string.Equals(
        Active.ClientProduct,
        NetAccelClientProduct,
        StringComparison.Ordinal);

    public string ClientProduct { get; init; } = "v2rayn";
    public string AppName { get; init; } = "v2rayN";
    public string AppUserModelID { get; init; } = string.Empty;
    public string SingleInstanceName { get; init; } = string.Empty;
    public string AutoRunBaseName { get; init; } = "v2rayNAutoRun";
    public string UrlProtocol { get; init; } = string.Empty;
    public string UserDataDirectoryName { get; init; } = "v2rayN";
    public string InstallDirContract { get; init; } = string.Empty;
    public string UninstallIdentity { get; init; } = string.Empty;

    /// <summary>
    /// Whether to disable upstream v2rayN GUI application-update (ECoreType.v2rayN).
    /// </summary>
    public bool DisableUpstreamAppUpdate { get; init; }

    /// <summary>
    /// Configure the global active identity. Must be called before startup-sensitive code reads <see cref="Active"/>.
    /// </summary>
    public static void Configure(NetAccelIdentity identity)
    {
        _active = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    /// <summary>
    /// Resets the process-wide identity for tests that exercise startup selection.
    /// </summary>
    public static void ResetForTests()
    {
        _active = null;
    }

    /// <summary>
    /// Creates a fully-populated NetAccel identity.
    /// </summary>
    public static NetAccelIdentity CreateNetAccel()
    {
        return new NetAccelIdentity
        {
            ClientProduct = NetAccelClientProduct,
            AppName = NetAccelDisplayName,
            AppUserModelID = NetAccelAppUserModelID,
            SingleInstanceName = NetAccelSingleInstanceName,
            AutoRunBaseName = NetAccelAutoRunBaseName,
            UrlProtocol = NetAccelUrlProtocol,
            UserDataDirectoryName = "NetAccel\\v2rayN-WPF",
            InstallDirContract = NetAccelInstallDirContract,
            UninstallIdentity = NetAccelUninstallIdentity,
            DisableUpstreamAppUpdate = true,
        };
    }
}
