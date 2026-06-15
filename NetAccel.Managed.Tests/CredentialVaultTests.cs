using NetAccel.Managed.Vault;
using Xunit;

namespace NetAccel.Managed.Tests;

public class CredentialVaultTests
{
    [Fact]
    public async Task InMemoryVault_CRUD()
    {
        var vault = new InMemoryCredentialVault();
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "token123");
        var retrieved = await vault.RetrieveAsync(CredentialVaultEntry.AccessToken);
        Assert.Equal("token123", retrieved);

        await vault.DeleteAsync(CredentialVaultEntry.AccessToken);
        var deleted = await vault.RetrieveAsync(CredentialVaultEntry.AccessToken);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task InMemoryVault_Overwrite()
    {
        var vault = new InMemoryCredentialVault();
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "old");
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "new");
        var retrieved = await vault.RetrieveAsync(CredentialVaultEntry.RefreshToken);
        Assert.Equal("new", retrieved);
    }

    [Fact]
    public async Task InMemoryVault_MissingEntry_ReturnsNull()
    {
        var vault = new InMemoryCredentialVault();
        var missing = await vault.RetrieveAsync(CredentialVaultEntry.InstanceCredential);
        Assert.Null(missing);
    }

    [Fact]
    public async Task InMemoryVault_EntriesAreIsolated()
    {
        var vault = new InMemoryCredentialVault();
        await vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");

        Assert.Equal("at", await vault.RetrieveAsync(CredentialVaultEntry.AccessToken));
        Assert.Equal("rt", await vault.RetrieveAsync(CredentialVaultEntry.RefreshToken));
    }
}
