using System.Security.Cryptography;
using System.Text;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Vault;

namespace NetAccel.Managed.Crypto;

/// <summary>
/// Manages the client instance P-256 ECDH device key lifecycle.
/// Private keys are stored only through the vault abstraction.
/// </summary>
public interface IDeviceKeyManager
{
    /// <summary>
    /// Returns the current device key info, generating and persisting a new key if none exists.
    /// </summary>
    Task<DeviceKeyInfo> GetOrCreateKeyAsync();

    /// <summary>
    /// Returns the current device key info without creating a new key.
    /// </summary>
    Task<DeviceKeyInfo?> GetCurrentKeyAsync();

    /// <summary>
    /// Rotates the device key: revokes the current key by deleting it from the vault.
    /// The next call to <see cref="GetOrCreateKeyAsync"/> will generate a new key.
    /// </summary>
    Task RotateAsync();

    /// <summary>
    /// Clears the device key from the vault entirely.
    /// </summary>
    Task ClearAsync();

    /// <summary>
    /// Exports the private key PEM for crypto operations.
    /// This should be used only by trusted crypto components and never logged or persisted outside the vault.
    /// </summary>
    Task<string?> GetPrivateKeyPemAsync();
}

public sealed class DeviceKeyManager : IDeviceKeyManager
{
    private readonly ICredentialVault _vault;

    public DeviceKeyManager(ICredentialVault vault)
    {
        _vault = vault;
    }

    public async Task<DeviceKeyInfo> GetOrCreateKeyAsync()
    {
        var existing = await GetCurrentKeyAsync();
        if (existing != null)
        {
            return existing;
        }

        var keyId = GenerateKeyId();
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        var privateKeyPem = ecdh.ExportPkcs8PrivateKeyPem();
        var publicKeyPem = ecdh.ExportSubjectPublicKeyInfoPem();

        await _vault.StoreAsync(CredentialVaultEntry.DeviceKeyId, keyId);
        await _vault.StoreAsync(CredentialVaultEntry.DevicePrivateKey, privateKeyPem);

        return new DeviceKeyInfo
        {
            KeyId = keyId,
            PublicKeyPem = publicKeyPem,
        };
    }

    public async Task<DeviceKeyInfo?> GetCurrentKeyAsync()
    {
        var keyId = await _vault.RetrieveAsync(CredentialVaultEntry.DeviceKeyId);
        var privateKeyPem = await _vault.RetrieveAsync(CredentialVaultEntry.DevicePrivateKey);

        if (string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(privateKeyPem))
        {
            return null;
        }

        // Derive public key from private key to ensure consistency
        using var ecdh = ECDiffieHellman.Create();
        ecdh.ImportFromPem(privateKeyPem);
        var publicKeyPem = ecdh.ExportSubjectPublicKeyInfoPem();

        return new DeviceKeyInfo
        {
            KeyId = keyId,
            PublicKeyPem = publicKeyPem,
        };
    }

    public async Task RotateAsync()
    {
        await ClearAsync();
    }

    public async Task ClearAsync()
    {
        await _vault.DeleteAsync(CredentialVaultEntry.DeviceKeyId);
        await _vault.DeleteAsync(CredentialVaultEntry.DevicePrivateKey);
    }

    public async Task<string?> GetPrivateKeyPemAsync()
    {
        return await _vault.RetrieveAsync(CredentialVaultEntry.DevicePrivateKey);
    }

    private static string GenerateKeyId()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
