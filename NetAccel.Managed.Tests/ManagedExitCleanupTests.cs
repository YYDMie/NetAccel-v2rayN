using AwesomeAssertions;
using NetAccel.Managed.Runtime;
using ServiceLib.Common;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedExitCleanupTests
{
    #region ShouldExcludeFromBackup

    [Theory]
    [InlineData("managed-connection-owner.json", true)]
    [InlineData("managed-envelope-cache.json", true)]
    [InlineData("managed-runtime-snapshot.json", true)]
    [InlineData("managed-config-v1.json", true)]
    [InlineData("netaccel-credential-abc.json", true)]
    [InlineData("guiNConfig.db", false)]
    [InlineData("config.json", false)]
    [InlineData("routing.json", false)]
    [InlineData("", false)]
    public void ShouldExcludeFromBackup_ReturnsCorrectResult(string fileName, bool expected)
    {
        ManagedExitCleanup.ShouldExcludeFromBackup(fileName).Should().Be(expected);
    }

    #endregion

    #region RemoveManagedFiles

    [Fact]
    public void RemoveManagedFiles_RemovesManagedFilesFromDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_managed_cleanup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            // Create managed files
            File.WriteAllText(Path.Combine(dir, "managed-connection-owner.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "managed-envelope-cache.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "netaccel-credential-test.json"), "{}");

            // Create non-managed files
            File.WriteAllText(Path.Combine(dir, "config.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "guiNConfig.db"), "fake");

            ManagedExitCleanup.RemoveManagedFiles(dir);

            // Managed files should be removed
            File.Exists(Path.Combine(dir, "managed-connection-owner.json")).Should().BeFalse();
            File.Exists(Path.Combine(dir, "managed-envelope-cache.json")).Should().BeFalse();
            File.Exists(Path.Combine(dir, "netaccel-credential-test.json")).Should().BeFalse();

            // Non-managed files should remain
            File.Exists(Path.Combine(dir, "config.json")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "guiNConfig.db")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RemoveManagedFiles_HandlesNonExistentDirectory()
    {
        var act = () => ManagedExitCleanup.RemoveManagedFiles(Path.Combine(Path.GetTempPath(), "nonexistent"));
        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveManagedFiles_HandlesEmptyDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            var act = () => ManagedExitCleanup.RemoveManagedFiles(dir);
            act.Should().NotThrow();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    #endregion

    #region Exit cleanup idempotency

    [Fact]
    public async Task RunExitCleanupAsync_IsIdempotent()
    {
        var store = new InMemoryConnectionOwnershipStore();
        await using var ownership = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);
        await ownership.AcquireAsync(ConnectionOwner.Managed);

        // Run cleanup twice
        await ManagedExitCleanup.RunExitCleanupAsync(ownership, null);
        await ManagedExitCleanup.RunExitCleanupAsync(ownership, null);

        ownership.CurrentOwner.Should().Be(ConnectionOwner.None);
    }

    [Fact]
    public async Task RunExitCleanupAsync_WhenNoOwner_DoesNotThrow()
    {
        var store = new InMemoryConnectionOwnershipStore();
        await using var ownership = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);

        var exception = await Record.ExceptionAsync(() => ManagedExitCleanup.RunExitCleanupAsync(ownership, null));
        exception.Should().BeNull();
    }

    #endregion

    #region ManagedConnectionGuard callbacks

    [Fact]
    public async Task ManagedConnectionGuard_RunExitCleanupAsync_CallsRegisteredCallback()
    {
        var called = false;
        ManagedConnectionGuard.OnExitCleanupAsync = () => { called = true; return Task.CompletedTask; };

        await ManagedConnectionGuard.RunExitCleanupAsync();

        called.Should().BeTrue();
        ManagedConnectionGuard.OnExitCleanupAsync = null;
    }

    [Fact]
    public async Task ManagedConnectionGuard_RunExitCleanupAsync_WhenNoCallback_DoesNotThrow()
    {
        ManagedConnectionGuard.OnExitCleanupAsync = null;

        var exception = await Record.ExceptionAsync(() => ManagedConnectionGuard.RunExitCleanupAsync());
        exception.Should().BeNull();
    }

    [Fact]
    public async Task ManagedConnectionGuard_RunExitCleanupAsync_SwallowsCallbackException()
    {
        ManagedConnectionGuard.OnExitCleanupAsync = () => throw new InvalidOperationException("test");

        var exception = await Record.ExceptionAsync(() => ManagedConnectionGuard.RunExitCleanupAsync());
        exception.Should().BeNull();
        ManagedConnectionGuard.OnExitCleanupAsync = null;
    }

    [Fact]
    public void ManagedConnectionGuard_CanPerformClassicOperation_DefaultsToTrue()
    {
        ManagedConnectionGuard.IsClassicOperationAllowed = null;
        ManagedConnectionGuard.CanPerformClassicOperation().Should().BeTrue();
    }

    [Fact]
    public void ManagedConnectionGuard_CanPerformClassicOperation_UsesRegisteredCallback()
    {
        ManagedConnectionGuard.IsClassicOperationAllowed = () => false;
        ManagedConnectionGuard.CanPerformClassicOperation().Should().BeFalse();
        ManagedConnectionGuard.IsClassicOperationAllowed = null;
    }

    [Fact]
    public async Task ManagedConnectionCoordinator_RegistersClassicOperationGuard()
    {
        ManagedConnectionGuard.IsClassicOperationAllowed = null;
        ManagedConnectionGuard.OnExitCleanupAsync = null;
        ManagedConnectionGuard.OnPostRestoreCleanupAsync = null;

        var store = new InMemoryConnectionOwnershipStore();
        await using var ownership = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);

        _ = new ManagedConnectionCoordinator(ownership, new NoopCoreRunner());

        ManagedConnectionGuard.CanPerformClassicOperation().Should().BeTrue();

        await ownership.AcquireAsync(ConnectionOwner.Managed);

        ManagedConnectionGuard.CanPerformClassicOperation().Should().BeFalse();

        ManagedConnectionGuard.IsClassicOperationAllowed = null;
        ManagedConnectionGuard.OnExitCleanupAsync = null;
        ManagedConnectionGuard.OnPostRestoreCleanupAsync = null;
    }

    [Fact]
    public async Task ManagedConnectionGuard_RunPostRestoreCleanupAsync_CallsRegisteredCallback()
    {
        string? receivedDir = null;
        ManagedConnectionGuard.OnPostRestoreCleanupAsync = dir => { receivedDir = dir; return Task.CompletedTask; };

        await ManagedConnectionGuard.RunPostRestoreCleanupAsync("/test/dir");

        receivedDir.Should().Be("/test/dir");
        ManagedConnectionGuard.OnPostRestoreCleanupAsync = null;
    }

    [Fact]
    public async Task ManagedConnectionGuard_RunPostRestoreCleanupAsync_SwallowsCallbackException()
    {
        ManagedConnectionGuard.OnPostRestoreCleanupAsync = _ => throw new InvalidOperationException("test");

        var exception = await Record.ExceptionAsync(() => ManagedConnectionGuard.RunPostRestoreCleanupAsync("/test"));
        exception.Should().BeNull();
        ManagedConnectionGuard.OnPostRestoreCleanupAsync = null;
    }

    #endregion

    private sealed class NoopCoreRunner : IManagedCoreRunner
    {
        public bool IsRunning => false;

        public Task StartAsync(ManagedRuntimeConfig runtimeConfig, ManagedConnectionMode mode, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
