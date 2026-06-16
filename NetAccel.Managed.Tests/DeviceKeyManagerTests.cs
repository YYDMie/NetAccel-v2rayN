using NetAccel.Managed.Crypto;
using NetAccel.Managed.Vault;
using Xunit;

namespace NetAccel.Managed.Tests;

public class DeviceKeyManagerTests
{
    [Fact]
    public async Task GetOrCreateKeyAsync_GeneratesNewKey_WhenNoneExists()
    {
        var vault = new InMemoryCredentialVault();
        var manager = new DeviceKeyManager(vault);

        var key = await manager.GetOrCreateKeyAsync();

        Assert.NotNull(key);
        Assert.False(string.IsNullOrEmpty(key.KeyId));
        Assert.False(string.IsNullOrEmpty(key.PublicKeyPem));
        Assert.Contains("BEGIN PUBLIC KEY", key.PublicKeyPem);
    }

    [Fact]
    public async Task GetOrCreateKeyAsync_ReturnsSameKey_OnSubsequentCalls()
    {
        var vault = new InMemoryCredentialVault();
        var manager = new DeviceKeyManager(vault);

        var key1 = await manager.GetOrCreateKeyAsync();
        var key2 = await manager.GetOrCreateKeyAsync();

        Assert.Equal(key1.KeyId, key2.KeyId);
        Assert.Equal(key1.PublicKeyPem, key2.PublicKeyPem);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_ReturnsNull_WhenNoKeyExists()
    {
        var vault = new InMemoryCredentialVault();
        var manager = new DeviceKeyManager(vault);

        var key = await manager.GetCurrentKeyAsync();

        Assert.Null(key);
    }

    [Fact]
    public async Task RotateAsync_GeneratesNewKey_OnNextCreate()
    {
        var vault = new InMemoryCredentialVault();
        var manager = new DeviceKeyManager(vault);

        var key1 = await manager.GetOrCreateKeyAsync();
        await manager.RotateAsync();
        var key2 = await manager.GetOrCreateKeyAsync();

        Assert.NotEqual(key1.KeyId, key2.KeyId);
        Assert.NotEqual(key1.PublicKeyPem, key2.PublicKeyPem);
    }

    [Fact]
    public async Task ClearAsync_RemovesKeyEntirely()
    {
        var vault = new InMemoryCredentialVault();
        var manager = new DeviceKeyManager(vault);

        await manager.GetOrCreateKeyAsync();
        await manager.ClearAsync();

        var key = await manager.GetCurrentKeyAsync();
        var privateKey = await manager.GetPrivateKeyPemAsync();

        Assert.Null(key);
        Assert.Null(privateKey);
    }

    [Fact]
    public async Task GetPrivateKeyPemAsync_ReturnsPkcs8Pem()
    {
        var vault = new InMemoryCredentialVault();
        var manager = new DeviceKeyManager(vault);

        await manager.GetOrCreateKeyAsync();
        var pem = await manager.GetPrivateKeyPemAsync();

        Assert.NotNull(pem);
        Assert.Contains("BEGIN PRIVATE KEY", pem);
    }

    [Fact]
    public async Task PublicKeyPem_IsSpkiFormat()
    {
        var vault = new InMemoryCredentialVault();
        var manager = new DeviceKeyManager(vault);

        var key = await manager.GetOrCreateKeyAsync();

        Assert.Contains("BEGIN PUBLIC KEY", key.PublicKeyPem);
    }
}
