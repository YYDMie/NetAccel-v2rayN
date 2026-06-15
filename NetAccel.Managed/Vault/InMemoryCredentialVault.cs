namespace NetAccel.Managed.Vault;

/// <summary>
/// In-memory vault for unit tests. Values are lost when the process exits.
/// </summary>
public sealed class InMemoryCredentialVault : ICredentialVault
{
    private readonly Dictionary<CredentialVaultEntry, string> _store = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task StoreAsync(CredentialVaultEntry entry, string value)
    {
        await _lock.WaitAsync();
        try
        {
            _store[entry] = value;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> RetrieveAsync(CredentialVaultEntry entry)
    {
        await _lock.WaitAsync();
        try
        {
            return _store.TryGetValue(entry, out var value) ? value : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(CredentialVaultEntry entry)
    {
        await _lock.WaitAsync();
        try
        {
            _store.Remove(entry);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Clear()
    {
        _lock.Wait();
        try
        {
            _store.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }
}
