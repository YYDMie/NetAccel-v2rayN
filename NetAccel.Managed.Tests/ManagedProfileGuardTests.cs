using AwesomeAssertions;
using NetAccel.Managed.Domain;
using ServiceLib.Common;
using ServiceLib.Models.Entities;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedProfileGuardTests
{
    #region IsManaged detection

    [Fact]
    public void IsManaged_ReturnsTrueForManagedPrefix()
    {
        var item = new ProfileItem { IndexId = "managed:plan-42" };
        ManagedProfileGuard.IsManaged(item).Should().BeTrue();
    }

    [Fact]
    public void IsManaged_ReturnsTrueForManagedIndexIdString()
    {
        ManagedProfileGuard.IsManaged("managed:plan-a").Should().BeTrue();
    }

    [Fact]
    public void IsManaged_ReturnsFalseForLocalIndexId()
    {
        var item = new ProfileItem { IndexId = "abc123-guid" };
        ManagedProfileGuard.IsManaged(item).Should().BeFalse();
    }

    [Fact]
    public void IsManaged_ReturnsFalseForNullItem()
    {
        ManagedProfileGuard.IsManaged((ProfileItem?)null!).Should().BeFalse();
    }

    [Fact]
    public void IsManaged_ReturnsFalseForNullString()
    {
        ManagedProfileGuard.IsManaged((string?)null).Should().BeFalse();
    }

    [Fact]
    public void IsManaged_ReturnsFalseForEmptyString()
    {
        ManagedProfileGuard.IsManaged("").Should().BeFalse();
    }

    [Fact]
    public void IsManaged_ReturnsFalseForEmptyIndexId()
    {
        var item = new ProfileItem { IndexId = "" };
        ManagedProfileGuard.IsManaged(item).Should().BeFalse();
    }

    [Fact]
    public void IsManaged_IsCaseSensitive()
    {
        ManagedProfileGuard.IsManaged("Managed:plan-a").Should().BeFalse();
        ManagedProfileGuard.IsManaged("MANAGED:plan-a").Should().BeFalse();
    }

    [Fact]
    public void IsManaged_DoesNotMatchPartialPrefix()
    {
        ManagedProfileGuard.IsManaged("managed-sub:plan-a").Should().BeFalse();
    }

    #endregion

    #region EnsureNotManaged

    [Fact]
    public void EnsureNotManaged_ThrowsForManagedProfile()
    {
        var item = new ProfileItem { IndexId = "managed:plan-42" };

        var act = () => ManagedProfileGuard.EnsureNotManaged(item, "Edit");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Edit*managed*managed:plan-42*");
    }

    [Fact]
    public void EnsureNotManaged_DoesNotThrowForLocalProfile()
    {
        var item = new ProfileItem { IndexId = "local-guid-123" };

        var act = () => ManagedProfileGuard.EnsureNotManaged(item, "Edit");
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureNotManaged_DoesNotThrowForLegacySubscriptionProfile()
    {
        var item = new ProfileItem { IndexId = "sub-guid-456", IsSub = true, Subid = "sub-1" };

        var act = () => ManagedProfileGuard.EnsureNotManaged(item, "Edit");
        act.Should().NotThrow();
    }

    #endregion

    #region EnsureNoneManaged

    [Fact]
    public void EnsureNoneManaged_ThrowsIfAnyItemIsManaged()
    {
        var items = new List<ProfileItem>
        {
            new() { IndexId = "local-guid-1" },
            new() { IndexId = "managed:plan-b" },
            new() { IndexId = "local-guid-2" },
        };

        var act = () => ManagedProfileGuard.EnsureNoneManaged(items, "RemoveServers");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RemoveServers*managed*managed:plan-b*");
    }

    [Fact]
    public void EnsureNoneManaged_DoesNotThrowForAllLocal()
    {
        var items = new List<ProfileItem>
        {
            new() { IndexId = "local-guid-1" },
            new() { IndexId = "local-guid-2" },
        };

        var act = () => ManagedProfileGuard.EnsureNoneManaged(items, "RemoveServers");
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureNoneManaged_DoesNotThrowForEmptyList()
    {
        var items = new List<ProfileItem>();

        var act = () => ManagedProfileGuard.EnsureNoneManaged(items, "RemoveServers");
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureNoneManaged_ReportsFirstManagedItem()
    {
        var items = new List<ProfileItem>
        {
            new() { IndexId = "managed:plan-a" },
            new() { IndexId = "managed:plan-b" },
        };

        var act = () => ManagedProfileGuard.EnsureNoneManaged(items, "Export");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Export*managed:plan-a*");
    }

    #endregion

    #region ProfileSourcePolicy consistency

    [Fact]
    public void ProfileSourcePolicy_ManagedBlocksEditDeleteCopyShareExport()
    {
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Edit).Should().BeFalse();
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Delete).Should().BeFalse();
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Copy).Should().BeFalse();
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Share).Should().BeFalse();
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Export).Should().BeFalse();
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Backup).Should().BeFalse();
    }

    [Fact]
    public void ProfileSourcePolicy_ManagedAllowsConnectSelectDiagnose()
    {
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Connect).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Select).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.Managed, ProfileOperation.Diagnose).Should().BeTrue();
    }

    [Fact]
    public void ProfileSourcePolicy_LocalAllowsAllOperations()
    {
        ProfileSourcePolicy.Can(ProfileSource.Local, ProfileOperation.Connect).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.Local, ProfileOperation.Select).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.Local, ProfileOperation.Edit).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.Local, ProfileOperation.Delete).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.Local, ProfileOperation.Copy).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.Local, ProfileOperation.Share).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.Local, ProfileOperation.Export).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.Local, ProfileOperation.Backup).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.Local, ProfileOperation.Diagnose).Should().BeTrue();
    }

    [Fact]
    public void ProfileSourcePolicy_LegacySubscriptionAllowsAllExceptShare()
    {
        ProfileSourcePolicy.Can(ProfileSource.LegacySubscription, ProfileOperation.Connect).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.LegacySubscription, ProfileOperation.Select).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.LegacySubscription, ProfileOperation.Edit).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.LegacySubscription, ProfileOperation.Delete).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.LegacySubscription, ProfileOperation.Copy).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.LegacySubscription, ProfileOperation.Export).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.LegacySubscription, ProfileOperation.Backup).Should().BeTrue();
        ProfileSourcePolicy.Can(ProfileSource.LegacySubscription, ProfileOperation.Diagnose).Should().BeTrue();

        ProfileSourcePolicy.Can(ProfileSource.LegacySubscription, ProfileOperation.Share).Should().BeFalse();
    }

    #endregion

    #region ManagedIndexPrefix constant

    [Fact]
    public void ManagedIndexPrefix_MatchesAdapterConvention()
    {
        ManagedProfileGuard.ManagedIndexPrefix.Should().Be("managed:");
    }

    #endregion
}
