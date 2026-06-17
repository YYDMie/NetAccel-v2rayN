namespace NetAccel.Managed.Runtime;

/// <summary>
/// Result of a classic mode handoff attempt.
/// </summary>
public sealed record ClassicModeHandoffResult
{
    public bool Success { get; init; }
    public string? FailureReason { get; init; }

    public static ClassicModeHandoffResult Succeeded() => new() { Success = true };
    public static ClassicModeHandoffResult Failed(string reason) => new() { Success = false, FailureReason = reason };
}

/// <summary>
/// Manages the safe transition from Managed to Classic mode. Ensures the managed connection
/// is stopped, system proxy/TUN are restored, and the ownership is released before classic
/// mode can acquire control.
/// </summary>
public sealed class ClassicModeLauncher : IAsyncDisposable
{
    private readonly ConnectionOwnershipCoordinator _ownership;
    private readonly ManagedConnectionCoordinator? _managedCoordinator;
    private ConnectionOwnershipLease? _classicLease;

    public ClassicModeLauncher(
        ConnectionOwnershipCoordinator ownership,
        ManagedConnectionCoordinator? managedCoordinator = null)
    {
        _ownership = ownership;
        _managedCoordinator = managedCoordinator;
    }

    /// <summary>
    /// Attempts a safe handoff from Managed to Classic mode.
    /// If Managed currently owns the connection, stops the managed connection and releases ownership.
    /// If Classic already owns, returns success (no-op).
    /// If no one owns, acquires and holds Classic ownership.
    /// </summary>
    public async Task<ClassicModeHandoffResult> TryHandoffToClassicAsync(CancellationToken ct = default)
    {
        // If Classic already owns, nothing to do.
        if (_ownership.CurrentOwner == ConnectionOwner.Classic)
        {
            return ClassicModeHandoffResult.Succeeded();
        }

        // If Managed owns, stop managed connection first.
        if (_ownership.CurrentOwner == ConnectionOwner.Managed)
        {
            if (_managedCoordinator != null)
            {
                var stopResult = await _managedCoordinator.StopAsync(ct);
                if (!stopResult.Success)
                {
                    return ClassicModeHandoffResult.Failed(
                        $"Failed to stop managed connection: {stopResult.Message}");
                }
            }
            else
            {
                // No coordinator available, try direct release.
                await _ownership.ReleaseAsync(ConnectionOwner.Managed, ct);
            }
        }

        // At this point owner should be None. Hold Classic ownership so Managed
        // cannot start while the classic core owns system state.
        var acquireResult = await _ownership.AcquireAsync(ConnectionOwner.Classic, ct);
        if (!acquireResult.Acquired || acquireResult.Lease == null)
        {
            return ClassicModeHandoffResult.Failed(
                $"Failed to acquire classic ownership: {acquireResult.ConflictReason}");
        }

        _classicLease = acquireResult.Lease;

        return ClassicModeHandoffResult.Succeeded();
    }

    /// <summary>
    /// Releases classic ownership when leaving classic mode or during shutdown.
    /// </summary>
    public async Task ReleaseClassicAsync()
    {
        if (_classicLease == null)
        {
            return;
        }

        await _classicLease.DisposeAsync();
        _classicLease = null;
    }

    /// <summary>
    /// Checks whether classic operations are currently allowed (no managed owner active).
    /// This is the callback registered with ManagedConnectionGuard.IsClassicOperationAllowed.
    /// </summary>
    public bool IsClassicOperationAllowed()
    {
        return _ownership.CurrentOwner != ConnectionOwner.Managed;
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseClassicAsync();
    }
}
