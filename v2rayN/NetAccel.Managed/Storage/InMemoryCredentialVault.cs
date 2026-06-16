namespace NetAccel.Managed.Storage;

public sealed class InMemoryCredentialVault : ICredentialVault
{
    private readonly Dictionary<string, string> _store = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { _store[key] = value; }
        finally { _lock.Release(); }
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return _store.TryGetValue(key, out var v) ? v : null; }
        finally { _lock.Release(); }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { _store.Remove(key); }
        finally { _lock.Release(); }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return _store.ContainsKey(key); }
        finally { _lock.Release(); }
    }
}
