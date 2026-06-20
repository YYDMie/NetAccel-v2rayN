using System.Diagnostics;
using System.Text.Json;

namespace NetAccel.Managed.Runtime;

public enum ConnectionOwner
{
    None = 0,
    Managed = 1,
    Classic = 2,
}

public sealed record ConnectionOwnershipSnapshot
{
    public ConnectionOwner Owner { get; init; }
    public int ProcessId { get; init; }
    public string MachineName { get; init; } = Environment.MachineName;
    public DateTimeOffset AcquiredAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ConnectionOwnershipAcquireResult
{
    public bool Acquired { get; init; }
    public ConnectionOwner Owner { get; init; }
    public string? ConflictReason { get; init; }
    public ConnectionOwnershipLease? Lease { get; init; }
}

public interface IConnectionOwnershipStore
{
    Task<ConnectionOwnershipSnapshot?> ReadAsync(CancellationToken ct = default);
    Task WriteAsync(ConnectionOwnershipSnapshot snapshot, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

public sealed class FileConnectionOwnershipStore : IConnectionOwnershipStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _snapshotPath;

    public FileConnectionOwnershipStore(string? snapshotPath = null)
    {
        _snapshotPath = snapshotPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetAccel",
            "managed-connection-owner.json");
    }

    public async Task<ConnectionOwnershipSnapshot?> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_snapshotPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_snapshotPath);
            return await JsonSerializer.DeserializeAsync<ConnectionOwnershipSnapshot>(stream, JsonOptions, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task WriteAsync(ConnectionOwnershipSnapshot snapshot, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(_snapshotPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _snapshotPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, ct);
        }

        if (File.Exists(_snapshotPath))
        {
            File.Replace(tempPath, _snapshotPath, null);
        }
        else
        {
            File.Move(tempPath, _snapshotPath);
        }
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(_snapshotPath))
        {
            File.Delete(_snapshotPath);
        }

        return Task.CompletedTask;
    }
}

public sealed class InMemoryConnectionOwnershipStore : IConnectionOwnershipStore
{
    private ConnectionOwnershipSnapshot? _snapshot;

    public Task<ConnectionOwnershipSnapshot?> ReadAsync(CancellationToken ct = default)
        => Task.FromResult(_snapshot);

    public Task WriteAsync(ConnectionOwnershipSnapshot snapshot, CancellationToken ct = default)
    {
        _snapshot = snapshot;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        _snapshot = null;
        return Task.CompletedTask;
    }
}

public sealed class ConnectionOwnershipCoordinator : IAsyncDisposable
{
    public const string DefaultMutexName = "NetAccel.Managed.ConnectionOwnership";

    private readonly IConnectionOwnershipStore _store;
    private readonly Func<int, bool> _isProcessRunning;
    private readonly Func<DateTimeOffset> _now;
    private readonly bool _useGlobalMutex;
    private readonly string _mutexName;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ConnectionOwner _currentOwner = ConnectionOwner.None;

    public ConnectionOwnershipCoordinator(
        IConnectionOwnershipStore? store = null,
        string mutexName = DefaultMutexName,
        Func<int, bool>? isProcessRunning = null,
        bool useGlobalMutex = true,
        Func<DateTimeOffset>? now = null)
    {
        _store = store ?? new FileConnectionOwnershipStore();
        _isProcessRunning = isProcessRunning ?? IsProcessRunning;
        _useGlobalMutex = useGlobalMutex;
        _mutexName = mutexName;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public ConnectionOwner CurrentOwner => _currentOwner;

    public async Task<ConnectionOwnershipSnapshot?> InspectAsync(CancellationToken ct = default)
    {
        var snapshot = await _store.ReadAsync(ct);
        return snapshot is { Owner: not ConnectionOwner.None } && IsSnapshotLive(snapshot)
            ? snapshot
            : null;
    }

    public async Task<ConnectionOwnershipAcquireResult> AcquireAsync(ConnectionOwner owner, CancellationToken ct = default)
    {
        if (owner == ConnectionOwner.None)
        {
            return Failed(owner, "owner_none_invalid");
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_currentOwner != ConnectionOwner.None)
            {
                return Failed(owner, $"owner_already_held:{_currentOwner}");
            }

            var transaction = await RunOwnershipTransactionAsync(async () =>
            {
                var snapshot = await _store.ReadAsync(ct);
                if (snapshot is { Owner: not ConnectionOwner.None } && IsSnapshotLive(snapshot))
                {
                    return Failed(owner, $"owner_snapshot_live:{snapshot.Owner}");
                }

                var now = _now();
                var newSnapshot = new ConnectionOwnershipSnapshot
                {
                    Owner = owner,
                    ProcessId = Environment.ProcessId,
                    MachineName = Environment.MachineName,
                    AcquiredAt = now,
                    UpdatedAt = now,
                };

                await _store.WriteAsync(newSnapshot, ct);
                return new ConnectionOwnershipAcquireResult
                {
                    Acquired = true,
                    Owner = owner,
                };
            }, ct);
            if (!transaction.LockAcquired)
            {
                return Failed(owner, "owner_mutex_busy");
            }
            if (!transaction.Result.Acquired)
            {
                return transaction.Result;
            }

            _currentOwner = owner;

            return new ConnectionOwnershipAcquireResult
            {
                Acquired = true,
                Owner = owner,
                Lease = new ConnectionOwnershipLease(this, owner),
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ReleaseAsync(ConnectionOwner owner, CancellationToken ct = default)
    {
        if (owner == ConnectionOwner.None)
        {
            return false;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_currentOwner != owner)
            {
                return false;
            }

            var transaction = await RunOwnershipTransactionAsync(async () =>
            {
                await _store.ClearAsync(ct);
                return true;
            }, ct);
            if (!transaction.LockAcquired || !transaction.Result)
            {
                return false;
            }

            _currentOwner = ConnectionOwner.None;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_currentOwner != ConnectionOwner.None)
        {
            await ReleaseAsync(_currentOwner);
        }
        _gate.Dispose();
    }

    private static ConnectionOwnershipAcquireResult Failed(ConnectionOwner owner, string reason)
        => new() { Acquired = false, Owner = owner, ConflictReason = reason };

    // Windows mutexes are thread-affine. Keep acquisition, snapshot I/O and
    // release on one worker; the live-process snapshot carries the long lease.
    private async Task<(bool LockAcquired, T Result)> RunOwnershipTransactionAsync<T>(
        Func<Task<T>> operation,
        CancellationToken ct)
    {
        if (!_useGlobalMutex)
        {
            return (true, await operation());
        }

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var mutex = new Mutex(false, _mutexName);
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                return (false, default(T)!);
            }

            try
            {
                return (true, operation().GetAwaiter().GetResult());
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }, ct);
    }

    private bool IsSnapshotLive(ConnectionOwnershipSnapshot snapshot)
    {
        if (snapshot.ProcessId == Environment.ProcessId)
        {
            return true;
        }

        return snapshot.MachineName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            && _isProcessRunning(snapshot.ProcessId);
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class ConnectionOwnershipLease : IAsyncDisposable
{
    private readonly ConnectionOwnershipCoordinator _coordinator;
    private readonly ConnectionOwner _owner;
    private bool _disposed;

    internal ConnectionOwnershipLease(ConnectionOwnershipCoordinator coordinator, ConnectionOwner owner)
    {
        _coordinator = coordinator;
        _owner = owner;
    }

    public ConnectionOwner Owner => _owner;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _coordinator.ReleaseAsync(_owner);
        _disposed = true;
    }
}
