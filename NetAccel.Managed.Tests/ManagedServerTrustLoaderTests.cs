using NetAccel.Managed.Cache;
using Xunit;

namespace NetAccel.Managed.Tests;

public sealed class ManagedServerTrustLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "netaccel-trust-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_UsesInlineKeyBeforeFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "managed-trust"));
        File.WriteAllText(Path.Combine(_root, "managed-trust", "envelope.pem"), "packaged");

        var result = ManagedServerTrustLoader.Load(_root, "inline\\nkey");

        Assert.Equal($"inline{Environment.NewLine}key", result);
    }

    [Fact]
    public void Load_UsesConfiguredPathBeforePackagedDefault()
    {
        Directory.CreateDirectory(Path.Combine(_root, "managed-trust"));
        File.WriteAllText(Path.Combine(_root, "managed-trust", "envelope.pem"), "packaged");
        var configured = Path.Combine(_root, "configured.pem");
        File.WriteAllText(configured, "configured");

        var result = ManagedServerTrustLoader.Load(_root, configuredPath: configured);

        Assert.Equal("configured", result);
    }

    [Fact]
    public void Load_UsesPackagedDefaultAndReturnsNullWhenMissing()
    {
        Assert.Null(ManagedServerTrustLoader.Load(_root));
        Directory.CreateDirectory(Path.Combine(_root, "managed-trust"));
        File.WriteAllText(Path.Combine(_root, "managed-trust", "envelope.pem"), "packaged");

        Assert.Equal("packaged", ManagedServerTrustLoader.Load(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
