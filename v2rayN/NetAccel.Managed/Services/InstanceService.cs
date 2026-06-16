using NetAccel.Managed.Api;
using NetAccel.Managed.Dtos;
using NetAccel.Managed.Identity;
using NetAccel.Managed.Storage;

namespace NetAccel.Managed.Services;

public interface IInstanceService
{
    Task<bool> EnsureRegisteredAsync(CancellationToken ct = default);
    Task<InstanceRegisterResponse?> RegisterAsync(CancellationToken ct = default);
    Task<bool> RotateCredentialsAsync(CancellationToken ct = default);
    Task<HeartbeatResponse?> HeartbeatAsync(CancellationToken ct = default);
    string? InstanceId { get; }
    bool IsRegistered { get; }
}

public sealed class InstanceService : IInstanceService
{
    private readonly IManagedApiClient _apiClient;
    private readonly ICredentialVault _vault;
    private readonly IInstallationIdentityService _installationIdentity;
    private readonly string _clientVersion;
    private readonly Dictionary<string, string> _coreVersions;
    private readonly List<string> _capabilities;

    public InstanceService(
        IManagedApiClient apiClient,
        ICredentialVault vault,
        IInstallationIdentityService installationIdentity,
        string clientVersion,
        Dictionary<string, string> coreVersions,
        List<string> capabilities)
    {
        _apiClient = apiClient;
        _vault = vault;
        _installationIdentity = installationIdentity;
        _clientVersion = clientVersion;
        _coreVersions = coreVersions;
        _capabilities = capabilities;
    }

    public string? InstanceId => _apiClient.CurrentInstanceCredential != null
        ? _vault.GetAsync("instance_id").GetAwaiter().GetResult()
        : null;

    public bool IsRegistered => !string.IsNullOrEmpty(_apiClient.CurrentInstanceCredential);

    public async Task<bool> EnsureRegisteredAsync(CancellationToken ct = default)
    {
        var cred = await _vault.GetAsync("instance_credential", ct);
        var id = await _vault.GetAsync("instance_id", ct);
        if (!string.IsNullOrEmpty(cred) && !string.IsNullOrEmpty(id))
        {
            _apiClient.SetInstanceCredential(cred);
            return true;
        }
        var result = await RegisterAsync(ct);
        return result != null;
    }

    public async Task<InstanceRegisterResponse?> RegisterAsync(CancellationToken ct = default)
    {
        var installationKey = await _installationIdentity.GetOrCreateInstallationKeyAsync(ct);
        var request = new InstanceRegisterRequest
        {
            InstallationKey = installationKey,
            DisplayName = Environment.MachineName,
            ClientProduct = "netaccel-v2rayn-wpf",
            Platform = "windows",
            PlatformVersion = Environment.OSVersion.VersionString,
            Arch = "x86_64",
            ClientVersion = _clientVersion,
            CoreVersions = _coreVersions,
            Capabilities = _capabilities
        };

        var response = await _apiClient.PostAsync<InstanceRegisterResponse>("/api/v1/client/instances/register", request, false, allowRetry: false, ct);
        if (response == null) return null;

        await _vault.SetAsync("instance_id", response.Id, ct);
        await _vault.SetAsync("instance_credential", response.Credential, ct);
        _apiClient.SetInstanceCredential(response.Credential);
        return response;
    }

    public async Task<bool> RotateCredentialsAsync(CancellationToken ct = default)
    {
        var instanceId = await _vault.GetAsync("instance_id", ct);
        if (string.IsNullOrEmpty(instanceId)) return false;

        var response = await _apiClient.PostAsync<InstanceRegisterResponse>($"/api/v1/client/instances/{instanceId}/credentials/rotate", null, true, allowRetry: false, ct);
        if (response == null) return false;

        await _vault.SetAsync("instance_credential", response.Credential, ct);
        _apiClient.SetInstanceCredential(response.Credential);
        return true;
    }

    public async Task<HeartbeatResponse?> HeartbeatAsync(CancellationToken ct = default)
    {
        var instanceId = await _vault.GetAsync("instance_id", ct);
        if (string.IsNullOrEmpty(instanceId)) return null;

        var request = new HeartbeatRequest
        {
            ClientVersion = _clientVersion,
            CoreVersions = _coreVersions,
            Capabilities = _capabilities,
            Status = "online"
        };

        return await _apiClient.PostAsync<HeartbeatResponse>($"/api/v1/client/instances/{instanceId}/heartbeat", request, true, allowRetry: false, ct);
    }
}
