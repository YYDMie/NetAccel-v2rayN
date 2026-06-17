using ServiceLib.Common;
using ServiceLib.Manager;

namespace NetAccel.Managed.Runtime;

/// <summary>
/// Registers managed-layer cleanup callbacks with ServiceLib's ManagedConnectionGuard.
/// Handles:
/// - Exit cleanup: stop managed connection, release ownership, clean TUN
/// - Post-restore cleanup: remove managed files from restored backup, strip managed entries from DB
/// - Classic operation guard: block classic Reload/proxy/TUN when managed owns the connection
/// </summary>
public static class ManagedExitCleanup
{
    /// <summary>
    /// Files in guiConfigs/ that are managed-specific and must not be backed up or restored.
    /// </summary>
    private static readonly HashSet<string> ManagedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "managed-connection-owner.json",
        "managed-envelope-cache.json",
        "managed-runtime-snapshot.json",
    };

    /// <summary>
    /// Prefixes for files that should be excluded from backup.
    /// </summary>
    private static readonly string[] ManagedFilePrefixes = ["managed-", "netaccel-credential-"];

    /// <summary>
    /// Registers all managed cleanup callbacks with ManagedConnectionGuard.
    /// Called once at startup after the managed layer is initialized.
    /// </summary>
    public static void Register(
        ConnectionOwnershipCoordinator ownership,
        ManagedConnectionCoordinator? managedCoordinator)
    {
        var launcher = new ClassicModeLauncher(ownership, managedCoordinator);

        ManagedConnectionGuard.IsClassicOperationAllowed = launcher.IsClassicOperationAllowed;
        ManagedConnectionGuard.OnExitCleanupAsync = () => RunExitCleanupAsync(ownership, managedCoordinator);
        ManagedConnectionGuard.OnPostRestoreCleanupAsync = RunPostRestoreCleanupAsync;
    }

    /// <summary>
    /// Idempotent exit cleanup. Stops managed connection, releases ownership, cleans TUN.
    /// Must not respect caller cancellation tokens.
    /// </summary>
    public static async Task RunExitCleanupAsync(
        ConnectionOwnershipCoordinator ownership,
        ManagedConnectionCoordinator? managedCoordinator)
    {
        // Stop managed connection if active
        if (managedCoordinator != null && managedCoordinator.Status.State != ManagedConnectionState.Ready)
        {
            try
            {
                await managedCoordinator.StopAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logging.SaveLog("ManagedExitCleanup: managed stop error", ex);
            }
        }

        // Release ownership if held
        if (ownership.CurrentOwner != ConnectionOwner.None)
        {
            try
            {
                await ownership.ReleaseAsync(ownership.CurrentOwner, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logging.SaveLog("ManagedExitCleanup: ownership release error", ex);
            }
        }

        // Clean TUN devices
        try
        {
            await AppManager.Instance.RemoveTunDeviceAsync();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ManagedExitCleanup: TUN cleanup error", ex);
        }
    }

    /// <summary>
    /// Post-restore cleanup. Removes managed-specific files from the restored guiConfigs
    /// directory and strips managed entries from the restored SQLite database.
    /// </summary>
    internal static async Task RunPostRestoreCleanupAsync(string configDir)
    {
        // Remove managed-specific files from restored config directory
        RemoveManagedFiles(configDir);

        // Remove managed entries from restored SQLite database
        await RemoveManagedDatabaseEntries(configDir);
    }

    /// <summary>
    /// Removes managed-specific files from the config directory.
    /// </summary>
    public static void RemoveManagedFiles(string configDir)
    {
        try
        {
            if (!Directory.Exists(configDir))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(configDir))
            {
                var fileName = Path.GetFileName(file);
                if (ManagedFileNames.Contains(fileName))
                {
                    File.Delete(file);
                    Logging.SaveLog($"ManagedExitCleanup: removed managed file {fileName}");
                    continue;
                }

                foreach (var prefix in ManagedFilePrefixes)
                {
                    if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(file);
                        Logging.SaveLog($"ManagedExitCleanup: removed managed file {fileName}");
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ManagedExitCleanup: file cleanup error", ex);
        }
    }

    /// <summary>
    /// Removes managed:* ProfileItem entries and managed SubItem entries from the SQLite
    /// database in the restored config directory. Uses direct SQL to avoid needing the
    /// full ORM to be initialized.
    /// </summary>
    public static async Task RemoveManagedDatabaseEntries(string configDir)
    {
        try
        {
            var dbPath = Path.Combine(configDir, "guiNConfig.db");
            if (!File.Exists(dbPath))
            {
                return;
            }

            // Use a direct SQLite connection to clean managed entries from the restored database.
            // SQLiteAsyncConnection does not implement IDisposable, so we don't use 'using'.
            var connection = new SQLite.SQLiteAsyncConnection(dbPath);

            // Remove managed:* ProfileItem entries
            var deletedProfiles = await connection.ExecuteAsync(
                "DELETE FROM ProfileItem WHERE IndexId LIKE 'managed:%'");

            // Remove managed SubItem entries (SubItem.Id with managed prefix)
            var deletedSubs = await connection.ExecuteAsync(
                "DELETE FROM SubItem WHERE Id LIKE 'managed:%'");

            if (deletedProfiles > 0 || deletedSubs > 0)
            {
                Logging.SaveLog(
                    $"ManagedExitCleanup: removed {deletedProfiles} managed ProfileItems, " +
                    $"{deletedSubs} managed SubItems from restored database");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ManagedExitCleanup: database cleanup error", ex);
        }
    }

    /// <summary>
    /// Returns true if a file should be excluded from backup.
    /// Used by BackupAndRestoreViewModel to filter managed files.
    /// </summary>
    public static bool ShouldExcludeFromBackup(string fileName)
    {
        if (ManagedFileNames.Contains(fileName))
        {
            return true;
        }

        foreach (var prefix in ManagedFilePrefixes)
        {
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
