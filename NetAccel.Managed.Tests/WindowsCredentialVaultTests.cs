using NetAccel.Managed.Vault;
using Xunit;

namespace NetAccel.Managed.Tests;

/// <summary>
/// Windows Credential Manager tests.
/// Real CM tests are opt-in via NETACCEL_TEST_CM=1 because they require Windows and write to the real Credential Manager.
/// Run with: dotnet test --filter "FullyQualifiedName~WindowsCredentialVaultTests" -e NETACCEL_TEST_CM=1
/// </summary>
public class WindowsCredentialVaultTests : IDisposable
{
    private readonly bool _runRealTests;
    private readonly WindowsCredentialVault _vault;

    public WindowsCredentialVaultTests()
    {
        _runRealTests = Environment.GetEnvironmentVariable("NETACCEL_TEST_CM") == "1";
        _vault = new WindowsCredentialVault("NetAccel/Tests/WP03");
    }

    public void Dispose()
    {
        if (_runRealTests)
        {
            // Best-effort cleanup
            try
            {
                _vault.DeleteAsync(CredentialVaultEntry.AccessToken).Wait();
                _vault.DeleteAsync(CredentialVaultEntry.RefreshToken).Wait();
                _vault.DeleteAsync(CredentialVaultEntry.InstanceCredential).Wait();
            }
            catch { }
        }
        _vault.Dispose();
    }

    [Fact]
    public async Task RealCm_CRUD()
    {
        if (!_runRealTests) return;

        await _vault.StoreAsync(CredentialVaultEntry.AccessToken, "test-access-token-123");
        var retrieved = await _vault.RetrieveAsync(CredentialVaultEntry.AccessToken);
        Assert.Equal("test-access-token-123", retrieved);

        await _vault.DeleteAsync(CredentialVaultEntry.AccessToken);
        var deleted = await _vault.RetrieveAsync(CredentialVaultEntry.AccessToken);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task RealCm_Overwrite()
    {
        if (!_runRealTests) return;

        await _vault.StoreAsync(CredentialVaultEntry.RefreshToken, "old-rt");
        await _vault.StoreAsync(CredentialVaultEntry.RefreshToken, "new-rt");
        var retrieved = await _vault.RetrieveAsync(CredentialVaultEntry.RefreshToken);
        Assert.Equal("new-rt", retrieved);
    }

    [Fact]
    public async Task RealCm_MissingEntry_ReturnsNull()
    {
        if (!_runRealTests) return;

        var missing = await _vault.RetrieveAsync(CredentialVaultEntry.InstanceCredential);
        Assert.Null(missing);
    }

    [Fact]
    public async Task RealCm_EntriesAreIsolated()
    {
        if (!_runRealTests) return;

        await _vault.StoreAsync(CredentialVaultEntry.AccessToken, "at");
        await _vault.StoreAsync(CredentialVaultEntry.RefreshToken, "rt");
        await _vault.StoreAsync(CredentialVaultEntry.InstanceCredential, "ic");

        Assert.Equal("at", await _vault.RetrieveAsync(CredentialVaultEntry.AccessToken));
        Assert.Equal("rt", await _vault.RetrieveAsync(CredentialVaultEntry.RefreshToken));
        Assert.Equal("ic", await _vault.RetrieveAsync(CredentialVaultEntry.InstanceCredential));
    }
}
