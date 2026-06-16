using NetAccel.Managed.Storage;

namespace NetAccel.Managed.Tests.Storage;

public class CredentialVaultTests
{
    [Fact]
    public async Task InMemoryVault_StoresAndRetrieves()
    {
        var vault = new InMemoryCredentialVault();
        await vault.SetAsync("key", "value");
        var result = await vault.GetAsync("key");
        result.Should().Be("value");
    }

    [Fact]
    public async Task InMemoryVault_Delete_RemovesEntry()
    {
        var vault = new InMemoryCredentialVault();
        await vault.SetAsync("key", "value");
        await vault.DeleteAsync("key");
        var result = await vault.GetAsync("key");
        result.Should().BeNull();
    }

    [Fact]
    public async Task InMemoryVault_Exists_ReturnsCorrectState()
    {
        var vault = new InMemoryCredentialVault();
        (await vault.ExistsAsync("missing")).Should().BeFalse();
        await vault.SetAsync("present", "value");
        (await vault.ExistsAsync("present")).Should().BeTrue();
    }
}
