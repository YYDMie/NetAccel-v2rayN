using NetAccel.Managed.Dto;

namespace NetAccel.Managed.Cache;

/// <summary>
/// Result of evaluating whether a cached config may be used offline.
/// </summary>
public sealed record OfflineConfigResult(
    bool CanUseOffline,
    bool IsExpired,
    string? Reason)
{
    public static OfflineConfigResult Allowed() => new(true, false, null);
    public static OfflineConfigResult Rejected(string reason) => new(false, false, reason);
    public static OfflineConfigResult Expired(string reason) => new(false, true, reason);
}

/// <summary>
/// Evaluates offline cache usability for managed envelopes.
/// </summary>
public interface IOfflineConfigRules
{
    /// <summary>
    /// Evaluates whether the cached envelope may be used when the server is unreachable.
    /// </summary>
    OfflineConfigResult Evaluate(
        EnvelopeCacheEntry? entry,
        string expectedInstanceId,
        string expectedKeyId,
        DateTimeOffset? now = null);

    /// <summary>
    /// Checks whether a selection change to <paramref name="profileId"/>
    /// is allowed while offline, given the decrypted payload.
    /// The profile must exist in the cached authorized set.
    /// </summary>
    bool CanSelectOffline(ManagedConfigPayload payload, string profileId);
}

public sealed class OfflineConfigRules : IOfflineConfigRules
{
    public OfflineConfigResult Evaluate(
        EnvelopeCacheEntry? entry,
        string expectedInstanceId,
        string expectedKeyId,
        DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;

        if (entry == null)
        {
            return OfflineConfigResult.Rejected("no cached envelope");
        }

        if (!EnvelopeCacheManager.IsV1Envelope(entry.Envelope))
        {
            return OfflineConfigResult.Rejected("cached envelope is not managed-envelope/v1");
        }

        if (!string.Equals(entry.Metadata.InstanceId, expectedInstanceId, StringComparison.Ordinal))
        {
            return OfflineConfigResult.Rejected("cached envelope instance_id mismatch");
        }

        if (!string.Equals(entry.Metadata.KeyId, expectedKeyId, StringComparison.Ordinal))
        {
            return OfflineConfigResult.Rejected("cached envelope key_id mismatch");
        }

        if (entry.Metadata.ExpiresAt <= clock)
        {
            return OfflineConfigResult.Expired("cached envelope has expired");
        }

        return OfflineConfigResult.Allowed();
    }

    public bool CanSelectOffline(ManagedConfigPayload payload, string profileId)
    {
        if (payload == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        var profile = payload.Profiles.FirstOrDefault(p =>
            string.Equals(p.Id, profileId, StringComparison.Ordinal));

        if (profile == null)
        {
            return false;
        }

        // Selection changes while offline may be marked pending only if
        // the selected profile exists in the cached authorized set.
        return true;
    }
}
