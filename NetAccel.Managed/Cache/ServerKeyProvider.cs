namespace NetAccel.Managed.Cache;

/// <summary>
/// Provides the server public key PEM for envelope signature verification.
/// </summary>
public interface IServerKeyProvider
{
    Task<string?> GetServerPublicKeyPemAsync();
}

/// <summary>
/// Static server key provider for testing and environments where the key is known at startup.
/// </summary>
public sealed class StaticServerKeyProvider : IServerKeyProvider
{
    private readonly string? _publicKeyPem;

    public StaticServerKeyProvider(string? publicKeyPem)
    {
        _publicKeyPem = publicKeyPem;
    }

    public Task<string?> GetServerPublicKeyPemAsync()
    {
        return Task.FromResult(_publicKeyPem);
    }
}
