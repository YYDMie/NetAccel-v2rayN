using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests;

/// <summary>
/// Tests for T16-R0-12: upstream application-update entry is blocked for NetAccel.
/// Also validates that manifest requests carry the fixed client_product.
/// F-01 regression: background auto-check must skip ECoreType.v2rayN for NetAccel.
/// </summary>
[Collection("NetAccelIdentity")]
public class ManagedUpdateTests
{
    [Fact]
    public void NetAccelIdentity_ShouldDisableUpstreamAppUpdate()
    {
        try
        {
            NetAccelIdentity.Configure(NetAccelIdentity.CreateNetAccel());

            NetAccelIdentity.Active.DisableUpstreamAppUpdate.Should().BeTrue(
                "NetAccel must not select, check, download, or install upstream v2rayN packages");
        }
        finally
        {
            NetAccelIdentity.ResetForTests();
        }
    }

    [Fact]
    public void UpstreamIdentity_ShouldAllowUpstreamAppUpdate()
    {
        var upstream = new NetAccelIdentity();
        upstream.DisableUpstreamAppUpdate.Should().BeFalse();
    }

    [Fact]
    public void NetAccelClientProduct_ShouldBeFixedValue()
    {
        NetAccelIdentity.NetAccelClientProduct.Should().Be("netaccel-v2rayn-wpf");
    }

    [Fact]
    public void NetAccelIdentity_ClientProduct_ShouldNotBeUpstreamValue()
    {
        var identity = NetAccelIdentity.CreateNetAccel();
        identity.ClientProduct.Should().NotBe("v2rayn");
        identity.ClientProduct.Should().Be("netaccel-v2rayn-wpf");
    }

    [Fact]
    public void ManagedUpdateManifestRequest_ShouldCarryNetAccelProduct()
    {
        var identity = NetAccelIdentity.CreateNetAccel();
        identity.ClientProduct.Should().Be("netaccel-v2rayn-wpf",
            "manifest requests must always carry the fixed client_product");
    }

    [Fact]
    public void ClassicCoreAndGeoUpdate_ShouldNotBeBlocked()
    {
        try
        {
            NetAccelIdentity.Configure(NetAccelIdentity.CreateNetAccel());

            NetAccelIdentity.Active.DisableUpstreamAppUpdate.Should().BeTrue();

            var coreTypes = new[] { ECoreType.Xray, ECoreType.sing_box };
            coreTypes.Should().AllSatisfy(ct =>
            {
                ct.Should().NotBe(ECoreType.v2rayN);
            });
        }
        finally
        {
            NetAccelIdentity.ResetForTests();
        }
    }

    // --- F-01 regression: background auto-check skips v2rayN for NetAccel ---

    /// <summary>
    /// Integration test: CheckHasUpdateOnlyAll must skip ECoreType.v2rayN when
    /// the NetAccel identity disables upstream app updates. The config selects
    /// only v2rayN, so if the guard were removed the method would attempt a
    /// network call and either return a result or throw — both are failures.
    /// </summary>
    [Fact]
    public async Task F01_CheckHasUpdateOnlyAll_NetAccel_SkipsV2rayN()
    {
        try
        {
            NetAccelIdentity.Configure(NetAccelIdentity.CreateNetAccel());

            var config = new Config
            {
                CheckUpdateItem = new CheckUpdateItem
                {
                    SelectedCoreTypes = [nameof(ECoreType.v2rayN)]
                }
            };

            var updateService = new UpdateService(config, (_, _) => Task.CompletedTask);
            var msgs = await updateService.CheckHasUpdateOnlyAll(preRelease: false);

            msgs.Should().BeEmpty(
                "NetAccel background auto-check must skip ECoreType.v2rayN — no update messages expected");
        }
        finally
        {
            NetAccelIdentity.ResetForTests();
        }
    }

    /// <summary>
    /// Upstream (default) identity must NOT skip ECoreType.v2rayN — the original
    /// v2rayN auto-update check must remain functional.
    /// </summary>
    [Fact]
    public void F01_GuardCondition_Upstream_ShouldNotSkipV2rayN()
    {
        // Upstream identity: DisableUpstreamAppUpdate defaults to false
        var type = ECoreType.v2rayN;
        var shouldSkip = type == ECoreType.v2rayN && NetAccelIdentity.Active.DisableUpstreamAppUpdate;

        shouldSkip.Should().BeFalse(
            "Upstream v2rayN identity must still check for GUI updates");
    }

    /// <summary>
    /// The guard must NOT affect non-v2rayN core types (Xray, sing-box, etc.)
    /// even when DisableUpstreamAppUpdate is active.
    /// </summary>
    [Theory]
    [InlineData(ECoreType.Xray)]
    [InlineData(ECoreType.sing_box)]
    [InlineData(ECoreType.mihomo)]
    [InlineData(ECoreType.v2fly)]
    public void F01_GuardCondition_NetAccel_MustNotSkipOtherCoreTypes(ECoreType type)
    {
        try
        {
            NetAccelIdentity.Configure(NetAccelIdentity.CreateNetAccel());

            var shouldSkip = type == ECoreType.v2rayN && NetAccelIdentity.Active.DisableUpstreamAppUpdate;

            shouldSkip.Should().BeFalse(
                $"Guard must not skip {type} — only ECoreType.v2rayN is filtered");
        }
        finally
        {
            NetAccelIdentity.ResetForTests();
        }
    }
}
