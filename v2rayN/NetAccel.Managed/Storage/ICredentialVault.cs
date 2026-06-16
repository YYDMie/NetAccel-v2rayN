namespace NetAccel.Managed.Storage;

public interface ICredentialVault
{
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
