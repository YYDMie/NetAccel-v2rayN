using NetAccel.Managed.Api;
using NetAccel.Managed.Crypto;
using NetAccel.Managed.Dtos;
using NetAccel.Managed.Storage;

namespace NetAccel.Managed.Services;

public sealed class ConfigSyncResult
{
    public ManagedEnvelopeSpikeV0? Envelope { get; init; }
    public string? DecryptedPayload { get; init; }
    public bool IsNotModified { get; init; }
}

public interface IManagedConfigSyncService
{
    Task<ConfigSyncResult?> FetchConfigAsync(int? currentRevision = null, CancellationToken ct = default);
    Task<bool> AckConfigAsync(int revision, string stage, string status, string? errorCode = null, string? errorDetail = null, CancellationToken ct = default);
}

public sealed class ManagedConfigSyncService : IManagedConfigSyncService
{
    private readonly IManagedApiClient _apiClient;
    private readonly ICredentialVault _vault;
    private readonly IManagedEnvelopeSpikeV0Crypto _crypto;
    private readonly string _clientVersion;
    private readonly Dictionary<string, string> _coreVersions;

    public ManagedConfigSyncService(
        IManagedApiClient apiClient,
        ICredentialVault vault,
        IManagedEnvelopeSpikeV0Crypto crypto,
        string clientVersion,
        Dictionary<string, string> coreVersions)
    {
        _apiClient = apiClient;
        _vault = vault;
        _crypto = crypto;
        _clientVersion = clientVersion;
        _coreVersions = coreVersions;
    }

    public async Task<ConfigSyncResult?> FetchConfigAsync(int? currentRevision = null, CancellationToken ct = default)
    {
        var path = "/api/v1/client/managed/config";
        var etag = currentRevision?.ToString();

        var etagResponse = await _apiClient.GetWithETagAsync<ManagedEnvelopeSpikeV0>(path, etag, useInstanceCredential: true, ct);

        if (etagResponse.IsNotModified)
        {
            return new ConfigSyncResult { IsNotModified = true };
        }

        var envelope = etagResponse.Data;
        if (envelope == null)
        {
            return null;
        }

        string? decrypted = null;
        var accessToken = await _vault.GetAsync("access_token", ct);
        if (!string.IsNullOrEmpty(accessToken))
        {
            decrypted = _crypto.Decrypt(envelope, accessToken);
        }

        // Return decrypted payload only as part of the short-lived result.
        // Do NOT store it in a service property, do NOT log it, do NOT persist it.
        return new ConfigSyncResult
        {
            Envelope = envelope,
            DecryptedPayload = decrypted,
            IsNotModified = false
        };
    }

    public async Task<bool> AckConfigAsync(int revision, string stage, string status, string? errorCode = null, string? errorDetail = null, CancellationToken ct = default)
    {
        var ack = new ManagedConfigAck
        {
            Revision = revision,
            Stage = stage,
            Status = status,
            AppliedRevision = stage == "applied" && status == "success" ? revision : null,
            ClientVersion = _clientVersion,
            CoreVersions = _coreVersions,
            ErrorCode = errorCode,
            ErrorDetail = errorDetail,
            ClientAppliedAt = DateTime.UtcNow.ToString("O")
        };

        try
        {
            await _apiClient.PostAsync<object>($"/api/v1/client/managed/config/{revision}/ack", ack, true, allowRetry: false, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
