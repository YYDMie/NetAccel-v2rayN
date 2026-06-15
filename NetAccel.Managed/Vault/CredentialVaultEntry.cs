namespace NetAccel.Managed.Vault;

/// <summary>
/// Well-known credential entry types stored in the vault.
/// </summary>
public enum CredentialVaultEntry
{
    AccessToken,
    RefreshToken,
    InstanceCredential,
    InstanceMetadata,
    InstallationKey,
}

/// <summary>
/// Interface for secure credential storage.
/// Implementations must not store values in plain text files, SQLite, or logs.
/// </summary>
public interface ICredentialVault
{
    Task StoreAsync(CredentialVaultEntry entry, string value);
    Task<string?> RetrieveAsync(CredentialVaultEntry entry);
    Task DeleteAsync(CredentialVaultEntry entry);
}
