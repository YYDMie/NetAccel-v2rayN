using NetAccel.Managed.Identity;
using NetAccel.Managed.Vault;
using Xunit;

namespace NetAccel.Managed.Tests;

public class InstallationIdentityTests
{
    [Fact]
    public async Task GetOrCreate_ReturnsStableKey()
    {
        var vault = new InMemoryCredentialVault();
        var svc = new InstallationIdentityService(vault);
        var key1 = await svc.GetOrCreateInstallationKeyAsync();
        var key2 = await svc.GetOrCreateInstallationKeyAsync();

        Assert.Equal(key1, key2);
        Assert.False(string.IsNullOrEmpty(key1));
    }

    [Fact]
    public async Task GetOrCreate_WithOverrideKey_ReturnsOverride()
    {
        var vault = new InMemoryCredentialVault();
        var svc = new InstallationIdentityService(vault, overrideKey: "fixed");
        var key = await svc.GetOrCreateInstallationKeyAsync();
        Assert.Equal("fixed", key);
    }

    [Fact]
    public async Task Reset_CreatesNewKey()
    {
        var vault = new InMemoryCredentialVault();
        var svc = new InstallationIdentityService(vault);
        var key1 = await svc.GetOrCreateInstallationKeyAsync();
        await svc.ResetAsync();
        var key2 = await svc.GetOrCreateInstallationKeyAsync();

        Assert.NotEqual(key1, key2);
    }
}
