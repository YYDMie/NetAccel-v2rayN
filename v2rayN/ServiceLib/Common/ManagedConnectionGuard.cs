namespace ServiceLib.Common;

/// <summary>
/// Provides a bridge between ServiceLib (which cannot reference NetAccel.Managed) and the
/// managed connection layer. The managed layer registers callbacks at startup; ServiceLib
/// consumers check the guard before performing operations that would conflict with an
/// active managed connection.
/// </summary>
public static class ManagedConnectionGuard
{
    /// <summary>
    /// Returns true if the managed layer considers classic operations (Reload, core start,
    /// system proxy change, TUN toggle) safe. When the managed connection owns the core,
    /// this returns false to prevent classic hotkey/tray/Reload from starting a second core.
    /// Returns true by default (no managed layer registered = classic mode only).
    /// </summary>
    public static Func<bool>? IsClassicOperationAllowed { get; set; }

    /// <summary>
    /// Called by AppManager.AppExitAsync and BackupAndRestoreViewModel.LocalRestore to allow
    /// the managed layer to perform cleanup (stop managed connection, release ownership,
    /// clean TUN, remove managed files from restored backup). Must be idempotent and must
    /// not respect caller cancellation tokens.
    /// </summary>
    public static Func<Task>? OnExitCleanupAsync { get; set; }

    /// <summary>
    /// Called by BackupAndRestoreViewModel after a restore extraction completes, before the
    /// app restarts. The managed layer can clean managed-specific files from the restored
    /// guiConfigs directory and remove managed entries from the restored SQLite database.
    /// </summary>
    public static Func<string, Task>? OnPostRestoreCleanupAsync { get; set; }

    /// <summary>
    /// Checks whether classic core operations are currently allowed.
    /// </summary>
    public static bool CanPerformClassicOperation()
        => IsClassicOperationAllowed?.Invoke() ?? true;

    /// <summary>
    /// Runs managed exit cleanup if registered. Swallows all exceptions to ensure the
    /// rest of the shutdown sequence continues.
    /// </summary>
    public static async Task RunExitCleanupAsync()
    {
        try
        {
            if (OnExitCleanupAsync != null)
            {
                await OnExitCleanupAsync();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ManagedConnectionGuard exit cleanup error", ex);
        }
    }

    /// <summary>
    /// Runs post-restore cleanup if registered. Swallows all exceptions to ensure the
    /// restore sequence continues even if managed cleanup fails.
    /// </summary>
    public static async Task RunPostRestoreCleanupAsync(string configDir)
    {
        try
        {
            if (OnPostRestoreCleanupAsync != null)
            {
                await OnPostRestoreCleanupAsync(configDir);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ManagedConnectionGuard post-restore cleanup error", ex);
        }
    }
}
