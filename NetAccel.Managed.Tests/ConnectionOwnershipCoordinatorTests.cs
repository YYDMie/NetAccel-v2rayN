using NetAccel.Managed.Runtime;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ConnectionOwnershipCoordinatorTests
{
    [Fact]
    public async Task AcquireAsync_AllowsOnlyOneOwnerAtATime()
    {
        var store = new InMemoryConnectionOwnershipStore();
        await using var coordinator = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);

        var first = await coordinator.AcquireAsync(ConnectionOwner.Managed);
        var second = await coordinator.AcquireAsync(ConnectionOwner.Classic);

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
        Assert.Equal(ConnectionOwner.Managed, coordinator.CurrentOwner);

        await first.Lease!.DisposeAsync();
        Assert.Equal(ConnectionOwner.None, coordinator.CurrentOwner);
    }

    [Fact]
    public async Task AcquireAsync_ConcurrentManagedAcquireOnlyGrantsSingleLease()
    {
        var store = new InMemoryConnectionOwnershipStore();
        await using var coordinator = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => coordinator.AcquireAsync(ConnectionOwner.Managed)));

        Assert.Equal(1, results.Count(r => r.Acquired));
        Assert.Equal(7, results.Count(r => !r.Acquired));

        await results.Single(r => r.Acquired).Lease!.DisposeAsync();
        Assert.Equal(ConnectionOwner.None, coordinator.CurrentOwner);
    }

    [Fact]
    public async Task AcquireAsync_RecoversStaleSnapshotWhenProcessIsGone()
    {
        var store = new InMemoryConnectionOwnershipStore();
        await store.WriteAsync(new ConnectionOwnershipSnapshot
        {
            Owner = ConnectionOwner.Classic,
            ProcessId = 999999,
            MachineName = Environment.MachineName,
            AcquiredAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });
        await using var coordinator = new ConnectionOwnershipCoordinator(
            store,
            isProcessRunning: _ => false,
            useGlobalMutex: false);

        var result = await coordinator.AcquireAsync(ConnectionOwner.Managed);

        Assert.True(result.Acquired);
        Assert.Equal(ConnectionOwner.Managed, coordinator.CurrentOwner);
        var snapshot = await store.ReadAsync();
        Assert.Equal(ConnectionOwner.Managed, snapshot?.Owner);
    }

    [Fact]
    public async Task AcquireAsync_RejectsLiveSnapshot()
    {
        var store = new InMemoryConnectionOwnershipStore();
        await store.WriteAsync(new ConnectionOwnershipSnapshot
        {
            Owner = ConnectionOwner.Classic,
            ProcessId = 1234,
            MachineName = Environment.MachineName,
            AcquiredAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await using var coordinator = new ConnectionOwnershipCoordinator(
            store,
            isProcessRunning: _ => true,
            useGlobalMutex: false);

        var result = await coordinator.AcquireAsync(ConnectionOwner.Managed);

        Assert.False(result.Acquired);
        Assert.Contains("owner_snapshot_live", result.ConflictReason);
        Assert.Equal(ConnectionOwner.Classic, (await store.ReadAsync())?.Owner);
    }

    [Fact]
    public async Task ReleaseAsync_RejectsWrongOwner()
    {
        var store = new InMemoryConnectionOwnershipStore();
        await using var coordinator = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);
        var lease = (await coordinator.AcquireAsync(ConnectionOwner.Managed)).Lease!;

        var released = await coordinator.ReleaseAsync(ConnectionOwner.Classic);

        Assert.False(released);
        Assert.Equal(ConnectionOwner.Managed, coordinator.CurrentOwner);
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_ClassicOwnerBlocksManagedAcquire()
    {
        var store = new InMemoryConnectionOwnershipStore();
        await using var coordinator = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);

        var classic = await coordinator.AcquireAsync(ConnectionOwner.Classic);
        Assert.True(classic.Acquired);
        Assert.Equal(ConnectionOwner.Classic, coordinator.CurrentOwner);

        var managed = await coordinator.AcquireAsync(ConnectionOwner.Managed);
        Assert.False(managed.Acquired);
        Assert.Contains("owner_already_held", managed.ConflictReason);
        Assert.Equal(ConnectionOwner.Classic, coordinator.CurrentOwner);

        await classic.Lease!.DisposeAsync();
        Assert.Equal(ConnectionOwner.None, coordinator.CurrentOwner);
    }
}
