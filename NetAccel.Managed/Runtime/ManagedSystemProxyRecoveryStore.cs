using ServiceLib.Handler.SysProxy;
using System.Runtime.Versioning;
using System.Text.Json;

namespace NetAccel.Managed.Runtime;

[SupportedOSPlatform("windows")]
public interface IManagedSystemProxyRecoveryStore
{
    void Save(ProxySettingWindows.WindowsProxySnapshot snapshot);
    bool Recover();
    void Clear();
}

[SupportedOSPlatform("windows")]
public sealed class ManagedSystemProxyRecoveryStore : IManagedSystemProxyRecoveryStore
{
    private const int CurrentVersion = 1;
    private readonly string _path;

    public ManagedSystemProxyRecoveryStore(string path)
    {
        _path = Path.GetFullPath(path);
    }

    public void Save(ProxySettingWindows.WindowsProxySnapshot snapshot)
    {
        var state = RecoveryState.FromSnapshot(snapshot);
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Proxy recovery path has no parent directory.");
        Directory.CreateDirectory(directory);

        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(state));
        File.Move(tempPath, _path, true);
    }

    public bool Recover()
    {
        if (!File.Exists(_path))
        {
            return false;
        }

        var state = JsonSerializer.Deserialize<RecoveryState>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("Managed proxy recovery state is empty.");
        if (state.Version != CurrentVersion)
        {
            throw new InvalidDataException("Managed proxy recovery state version is unsupported.");
        }

        ProxySettingWindows.RestoreSnapshot(state.ToSnapshot());
        Clear();
        return true;
    }

    public void Clear()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
    }

    internal sealed record RecoveryState
    {
        public int Version { get; init; } = CurrentVersion;
        public int? ProxyEnable { get; init; }
        public string? ProxyServer { get; init; }
        public string? ProxyOverride { get; init; }
        public string? AutoConfigUrl { get; init; }

        public static RecoveryState FromSnapshot(ProxySettingWindows.WindowsProxySnapshot snapshot)
            => new()
            {
                ProxyEnable = ReadInt(snapshot, "ProxyEnable"),
                ProxyServer = ReadString(snapshot, "ProxyServer"),
                ProxyOverride = ReadString(snapshot, "ProxyOverride"),
                AutoConfigUrl = ReadString(snapshot, "AutoConfigURL"),
            };

        public ProxySettingWindows.WindowsProxySnapshot ToSnapshot()
            => new(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["ProxyEnable"] = ProxyEnable,
                ["ProxyServer"] = ProxyServer,
                ["ProxyOverride"] = ProxyOverride,
                ["AutoConfigURL"] = AutoConfigUrl,
            });

        private static int? ReadInt(ProxySettingWindows.WindowsProxySnapshot snapshot, string name)
            => snapshot.Values.TryGetValue(name, out var value) && value != null
                ? Convert.ToInt32(value)
                : null;

        private static string? ReadString(ProxySettingWindows.WindowsProxySnapshot snapshot, string name)
            => snapshot.Values.TryGetValue(name, out var value)
                ? value?.ToString()
                : null;
    }
}
