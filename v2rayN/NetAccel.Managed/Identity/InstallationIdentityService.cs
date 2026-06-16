using NetAccel.Managed.Storage;

namespace NetAccel.Managed.Identity;

public interface IInstallationIdentityService
{
    Task<string> GetOrCreateInstallationKeyAsync(CancellationToken ct = default);
    Task ResetAsync(CancellationToken ct = default);
}

public sealed class InstallationIdentityService : IInstallationIdentityService
{
    private readonly ICredentialVault _vault;
    private const string Key = "installation_key";

    public InstallationIdentityService(ICredentialVault vault)
    {
        _vault = vault;
    }

    public async Task<string> GetOrCreateInstallationKeyAsync(CancellationToken ct = default)
    {
        var existing = await _vault.GetAsync(Key, ct);
        if (!string.IsNullOrEmpty(existing)) return existing;

        var newKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        await _vault.SetAsync(Key, newKey, ct);
        return newKey;
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        await _vault.DeleteAsync(Key, ct);
    }
}
