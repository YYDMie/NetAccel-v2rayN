using NetAccel.Managed.Api;
using NetAccel.Managed.Dtos;

namespace NetAccel.Managed.Services;

public interface IManagedSelectionService
{
    Task<ManagedPolicy?> GetPolicyAsync(CancellationToken ct = default);
    Task<ManagedStatus?> GetStatusAsync(CancellationToken ct = default);
    Task<ManagedSelectionResponse?> SetSelectionAsync(string mode, string? profileId, int expectedRevision, CancellationToken ct = default);
}

public sealed class ManagedSelectionService : IManagedSelectionService
{
    private readonly IManagedApiClient _apiClient;

    public ManagedSelectionService(IManagedApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public Task<ManagedPolicy?> GetPolicyAsync(CancellationToken ct = default)
        => _apiClient.GetAsync<ManagedPolicy>("/api/v1/client/managed/policy", true, allowRetry: true, ct);

    public Task<ManagedStatus?> GetStatusAsync(CancellationToken ct = default)
        => _apiClient.GetAsync<ManagedStatus>("/api/v1/client/managed/status", true, allowRetry: true, ct);

    public Task<ManagedSelectionResponse?> SetSelectionAsync(string mode, string? profileId, int expectedRevision, CancellationToken ct = default)
    {
        var request = new ManagedSelectionRequest
        {
            Mode = mode,
            ProfileId = profileId,
            ExpectedSelectionRevision = expectedRevision
        };
        return _apiClient.PutAsync<ManagedSelectionResponse>("/api/v1/client/managed/selection", request, true, allowRetry: false, ct);
    }
}
