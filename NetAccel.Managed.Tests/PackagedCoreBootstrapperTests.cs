using NetAccel.Managed.Runtime;
using Xunit;

namespace NetAccel.Managed.Tests;

public sealed class PackagedCoreBootstrapperTests
{
    [Fact]
    public void Install_CopiesPinnedCoresIntoRuntimeDataDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "netaccel-core-bootstrap", Guid.NewGuid().ToString("N"));
        var package = Path.Combine(root, "package");
        var data = Path.Combine(root, "data");
        try
        {
            WriteCore(package, "xray", "xray.exe", "xray-v1");
            WriteCore(package, "sing_box", "sing-box.exe", "sing-box-v1");

            PackagedCoreBootstrapper.Install(package, data);

            Assert.Equal("xray-v1", File.ReadAllText(Path.Combine(data, "bin", "xray", "xray.exe")));
            Assert.Equal("sing-box-v1", File.ReadAllText(Path.Combine(data, "bin", "sing_box", "sing-box.exe")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Install_MissingPackagedCoreFailsClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), "netaccel-core-bootstrap", Guid.NewGuid().ToString("N"));
        try
        {
            WriteCore(root, "xray", "xray.exe", "xray-v1");
            Assert.Throws<FileNotFoundException>(() => PackagedCoreBootstrapper.Install(root, Path.Combine(root, "data")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteCore(string root, string directory, string fileName, string content)
    {
        var path = Path.Combine(root, "bin", directory);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, fileName), content);
    }
}
