using NetAccel.Managed.Api;
using NetAccel.Managed.Instance;

namespace NetAccel.Managed.Session;

public interface IManagedSessionTransport
{
    Task<string> GetInstanceIdAsync();
    Task<ManagedConnectionSession> CreateAsync(ManagedSessionCreateRequest request, CancellationToken ct = default);
    Task ActivateAsync(string sessionId, CancellationToken ct = default);
    Task HeartbeatAsync(string sessionId, ManagedSessionHeartbeatRequest request, CancellationToken ct = default);
    Task CloseAsync(string sessionId, ManagedSessionCloseRequest request, CancellationToken ct = default);
}

public sealed class ManagedSessionIdentityUnavailableException : InvalidOperationException
{
    public ManagedSessionIdentityUnavailableException()
        : base("Managed instance identity is not available.")
    {
    }
}

public sealed class ManagedSessionApiTransport : IManagedSessionTransport
{
    private readonly ManagedApiClient _api;
    private readonly IInstanceService _instanceService;

    public ManagedSessionApiTransport(ManagedApiClient api, IInstanceService instanceService)
    {
        _api = api;
        _instanceService = instanceService;
    }

    public async Task<string> GetInstanceIdAsync()
    {
        var instanceId = await _instanceService.GetInstanceIdAsync();
        return !string.IsNullOrWhiteSpace(instanceId)
            ? instanceId
            : throw new ManagedSessionIdentityUnavailableException();
    }

    public async Task<ManagedConnectionSession> CreateAsync(
        ManagedSessionCreateRequest request,
        CancellationToken ct = default)
    {
        var identity = await GetIdentityAsync();
        var body = request with { InstanceId = identity.InstanceId };
        return await _api.PostAsync<ManagedConnectionSession>(
            "/client/runtime/sessions",
            body,
            instanceCredential: identity.Credential,
            ct: ct);
    }

    public async Task ActivateAsync(string sessionId, CancellationToken ct = default)
    {
        var identity = await GetIdentityAsync();
        await _api.PostAsync<ManagedConnectionSession>(
            $"/client/runtime/sessions/{sessionId}/activate",
            null,
            instanceCredential: identity.Credential,
            ct: ct);
    }

    public async Task HeartbeatAsync(
        string sessionId,
        ManagedSessionHeartbeatRequest request,
        CancellationToken ct = default)
    {
        var identity = await GetIdentityAsync();
        await _api.PostAsync<object>(
            $"/client/runtime/sessions/{sessionId}/heartbeat",
            request,
            instanceCredential: identity.Credential,
            ct: ct);
    }

    public async Task CloseAsync(
        string sessionId,
        ManagedSessionCloseRequest request,
        CancellationToken ct = default)
    {
        var identity = await GetIdentityAsync();
        await _api.PostAsync<ManagedConnectionSession>(
            $"/client/runtime/sessions/{sessionId}/close",
            request,
            instanceCredential: identity.Credential,
            ct: ct);
    }

    private async Task<(string InstanceId, string Credential)> GetIdentityAsync()
    {
        var instanceId = await _instanceService.GetInstanceIdAsync();
        var credential = await _instanceService.GetInstanceCredentialAsync();
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(credential))
        {
            throw new ManagedSessionIdentityUnavailableException();
        }

        return (instanceId, credential);
    }
}
