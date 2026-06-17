namespace ServiceLib.Common;

/// <summary>
/// Guards against mutating or exporting managed (NetAccel server-assigned) profiles
/// through classic v2rayN paths. Managed profiles use IndexId with prefix "managed:"
/// and must never be persisted to SQLite SubItem/ProfileItem, edited, deleted, copied,
/// shared, exported, or backed up through classic handlers.
///
/// Classic local and legacy-subscription profiles retain full original behavior.
/// </summary>
public static class ManagedProfileGuard
{
    public const string ManagedIndexPrefix = "managed:";

    /// <summary>
    /// Returns true if the profile was created by the managed runtime adapter
    /// and should not be mutated or exported through classic paths.
    /// </summary>
    public static bool IsManaged(ProfileItem item)
        => item != null && IsManaged(item.IndexId);

    /// <summary>
    /// Returns true if the IndexId indicates a managed profile.
    /// </summary>
    public static bool IsManaged(string? indexId)
        => !string.IsNullOrEmpty(indexId) && indexId.StartsWith(ManagedIndexPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Throws if any profile in the list is managed. Used by bulk operations
    /// (delete, copy, export) that operate on multiple profiles.
    /// </summary>
    public static void EnsureNoneManaged(IEnumerable<ProfileItem> items, string operation)
    {
        foreach (var item in items)
        {
            if (IsManaged(item))
            {
                throw new InvalidOperationException(
                    $"Operation '{operation}' is not allowed on managed profile '{item.IndexId}'.");
            }
        }
    }

    /// <summary>
    /// Throws if the profile is managed. Used by single-profile operations
    /// (edit, share, export config).
    /// </summary>
    public static void EnsureNotManaged(ProfileItem item, string operation)
    {
        if (IsManaged(item))
        {
            throw new InvalidOperationException(
                $"Operation '{operation}' is not allowed on managed profile '{item.IndexId}'.");
        }
    }
}
