using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests;

/// <summary>
/// Focused tests for T16-R0-11: application identity isolation.
/// </summary>
[Collection("NetAccelIdentity")]
public class NetAccelIdentityTests
{
    [Fact]
    public void DefaultIdentity_ShouldBeUpstreamV2rayN()
    {
        var upstream = new NetAccelIdentity();

        upstream.AppName.Should().Be("v2rayN");
        upstream.AutoRunBaseName.Should().Be("v2rayNAutoRun");
        upstream.ClientProduct.Should().Be("v2rayn");
        upstream.AppUserModelID.Should().BeEmpty();
        upstream.SingleInstanceName.Should().BeEmpty();
        upstream.UrlProtocol.Should().BeEmpty();
        upstream.UserDataDirectoryName.Should().Be("v2rayN");
        upstream.DisableUpstreamAppUpdate.Should().BeFalse();
    }

    [Fact]
    public void CreateNetAccel_ShouldReturnFixedNetAccelValues()
    {
        var identity = NetAccelIdentity.CreateNetAccel();

        identity.ClientProduct.Should().Be("netaccel-v2rayn-wpf");
        identity.AppName.Should().Be("NetAccel");
        identity.AppUserModelID.Should().Be("NetAccel.v2rayN.WPF");
        identity.SingleInstanceName.Should().Be(@"Local\NetAccel.v2rayN.WPF.SingleInstance");
        identity.AutoRunBaseName.Should().Be("NetAccelAutoRun");
        identity.UrlProtocol.Should().Be("netaccel");
        identity.UserDataDirectoryName.Should().Be("NetAccel\\v2rayN-WPF");
        identity.InstallDirContract.Should().Be(@"%LOCALAPPDATA%\Programs\NetAccel");
        identity.UninstallIdentity.Should().Be("NetAccel.v2rayN.WPF");
        identity.DisableUpstreamAppUpdate.Should().BeTrue();
    }

    [Fact]
    public void Configure_ShouldSetGlobalActive()
    {
        try
        {
            var netAccel = NetAccelIdentity.CreateNetAccel();
            NetAccelIdentity.Configure(netAccel);

            NetAccelIdentity.Active.Should().BeSameAs(netAccel);
            NetAccelIdentity.IsNetAccel.Should().BeTrue();
        }
        finally
        {
            NetAccelIdentity.ResetForTests();
        }
    }

    [Fact]
    public void Configure_WithUpstreamValues_ShouldNotBeNetAccel()
    {
        try
        {
            NetAccelIdentity.Configure(new NetAccelIdentity());

            NetAccelIdentity.IsNetAccel.Should().BeFalse();
        }
        finally
        {
            NetAccelIdentity.ResetForTests();
        }
    }

    [Fact]
    public void NetAccelConstants_ShouldMatchContract()
    {
        NetAccelIdentity.NetAccelClientProduct.Should().Be("netaccel-v2rayn-wpf");
        NetAccelIdentity.NetAccelDisplayName.Should().Be("NetAccel");
        NetAccelIdentity.NetAccelAppUserModelID.Should().Be("NetAccel.v2rayN.WPF");
        NetAccelIdentity.NetAccelSingleInstanceName.Should().Be(@"Local\NetAccel.v2rayN.WPF.SingleInstance");
        NetAccelIdentity.NetAccelAutoRunBaseName.Should().Be("NetAccelAutoRun");
        NetAccelIdentity.NetAccelUrlProtocol.Should().Be("netaccel");
        NetAccelIdentity.NetAccelInstallDirContract.Should().Be(@"%LOCALAPPDATA%\Programs\NetAccel");
        NetAccelIdentity.NetAccelUninstallIdentity.Should().Be("NetAccel.v2rayN.WPF");
    }

    [Fact]
    public void SingleInstanceName_ShouldBeFixedNotHashBased()
    {
        var identity = NetAccelIdentity.CreateNetAccel();

        identity.SingleInstanceName.Should().Contain("NetAccel");
        identity.SingleInstanceName.Should().NotContain("_");
    }

    [Fact]
    public void AutoRunName_ShouldDifferFromUpstream()
    {
        var netAccel = NetAccelIdentity.CreateNetAccel();

        netAccel.AutoRunBaseName.Should().NotBe("v2rayNAutoRun");
        netAccel.AutoRunBaseName.Should().Be("NetAccelAutoRun");
    }

    [Fact]
    public void DataPathSelection_NetAccel_ShouldUseIndependentDirectory()
    {
        var identity = NetAccelIdentity.CreateNetAccel();

        identity.UserDataDirectoryName.Should().Be("NetAccel\\v2rayN-WPF");
        identity.UserDataDirectoryName.Should().NotBe("v2rayN");
    }

    [Fact]
    public void StartupPath_NetAccel_ShouldUseLocalAppDataWithoutLegacySwitch()
    {
        var previous = Environment.GetEnvironmentVariable(Global.LocalAppData);
        try
        {
            Environment.SetEnvironmentVariable(Global.LocalAppData, null);
            NetAccelIdentity.Configure(NetAccelIdentity.CreateNetAccel());

            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NetAccel",
                "v2rayN-WPF");

            Utils.StartupPath().Should().Be(expected);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Global.LocalAppData, previous);
            NetAccelIdentity.ResetForTests();
        }
    }

    [Fact]
    public void DisableUpstreamAppUpdate_ShouldBeTrueForNetAccel()
    {
        var identity = NetAccelIdentity.CreateNetAccel();
        identity.DisableUpstreamAppUpdate.Should().BeTrue();
    }

    [Fact]
    public void DisableUpstreamAppUpdate_ShouldBeFalseForUpstream()
    {
        var identity = new NetAccelIdentity();
        identity.DisableUpstreamAppUpdate.Should().BeFalse();
    }
}
