using System.Security.Cryptography;
using NetAccel.Managed.Vault;

namespace NetAccel.Managed.Identity;

/// <summary>
/// Provides a stable installation key for this Windows user/install.
/// Survives normal app restart. Per-user to avoid cross-user sharing.
/// Not usable as the only proof to recover an existing instance credential.
/// </summary>
public interface IInstallationIdentityService
{
    Task<string> GetOrCreateInstallationKeyAsync();
    Task ResetAsync();
}

public sealed class InstallationIdentityService : IInstallationIdentityService
{
    private readonly ICredentialVault _vault;
    private readonly string? _overrideKey;

    public InstallationIdentityService(ICredentialVault vault, string? overrideKey = null)
    {
        _vault = vault;
        _overrideKey = overrideKey;
    }

    public async Task<string> GetOrCreateInstallationKeyAsync()
    {
        if (!string.IsNullOrEmpty(_overrideKey))
        {
            return _overrideKey;
        }

        var existing = await _vault.RetrieveAsync(CredentialVaultEntry.InstallationKey);
        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        var key = GenerateKey();
        await _vault.StoreAsync(CredentialVaultEntry.InstallationKey, key);
        return key;
    }

    public async Task ResetAsync()
    {
        await _vault.DeleteAsync(CredentialVaultEntry.InstallationKey);
    }

    private static string GenerateKey()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
