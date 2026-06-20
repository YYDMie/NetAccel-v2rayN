using AwesomeAssertions;
using NetAccel.Managed.Runtime;
using ServiceLib.Handler.SysProxy;
using System.Runtime.Versioning;
using System.Text.Json;
using Xunit;

namespace NetAccel.Managed.Tests;

[SupportedOSPlatform("windows")]
public class ManagedSystemProxyRecoveryStoreTests
{
    [Fact]
    public void Recover_RestoresWindowsProxyAndDeletesState()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("NETACCEL_TEST_WINDOWS_PROXY"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var directory = NewTempDirectory();
        var path = Path.Combine(directory, "system-proxy-recovery.json");
        var store = new ManagedSystemProxyRecoveryStore(path);
        var original = ProxySettingWindows.CaptureSnapshot();
        var expected = Snapshot(1, "127.0.0.1:18080", "<local>;example.invalid", null);

        try
        {
            store.Save(expected);
            ProxySettingWindows.RestoreSnapshot(Snapshot(0, "127.0.0.1:19090", "<local>", null));

            store.Recover().Should().BeTrue();

            ProxySettingWindows.CaptureSnapshot().Values.Should().BeEquivalentTo(expected.Values);
            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            ProxySettingWindows.RestoreSnapshot(original);
            store.Clear();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Save_AtomicallyOverwritesPreviousState()
    {
        var directory = NewTempDirectory();
        var path = Path.Combine(directory, "system-proxy-recovery.json");
        var store = new ManagedSystemProxyRecoveryStore(path);

        try
        {
            store.Save(Snapshot(0, "old", null, null));
            store.Save(Snapshot(1, "new", "<local>", null));

            using var state = JsonDocument.Parse(File.ReadAllText(path));
            state.RootElement.GetProperty("Version").GetInt32().Should().Be(1);
            state.RootElement.GetProperty("ProxyEnable").GetInt32().Should().Be(1);
            state.RootElement.GetProperty("ProxyServer").GetString().Should().Be("new");
            state.RootElement.GetProperty("ProxyOverride").GetString().Should().Be("<local>");
            state.RootElement.GetProperty("AutoConfigUrl").ValueKind.Should().Be(JsonValueKind.Null);
            File.Exists(path + ".tmp").Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Clear_RemovesStateAndTemporaryFile()
    {
        var directory = NewTempDirectory();
        var path = Path.Combine(directory, "system-proxy-recovery.json");
        var store = new ManagedSystemProxyRecoveryStore(path);

        try
        {
            File.WriteAllText(path, "{}");
            File.WriteAllText(path + ".tmp", "{}");

            store.Clear();

            File.Exists(path).Should().BeFalse();
            File.Exists(path + ".tmp").Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static ProxySettingWindows.WindowsProxySnapshot Snapshot(
        int? enabled,
        string? server,
        string? proxyOverride,
        string? autoConfigUrl)
        => new(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ProxyEnable"] = enabled,
            ["ProxyServer"] = server,
            ["ProxyOverride"] = proxyOverride,
            ["AutoConfigURL"] = autoConfigUrl,
        });

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netaccel-proxy-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
