using AwesomeAssertions;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Runtime;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ClassicModeLauncherTests
{
    [Fact]
    public async Task TryHandoffToClassic_WhenNoOwner_AcquiresClassic()
    {
        var store = new InMemoryConnectionOwnershipStore();
        await using var ownership = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);
        var launcher = new ClassicModeLauncher(ownership);

        var result = await launcher.TryHandoffToClassicAsync();

        result.Success.Should().BeTrue();
        ownership.CurrentOwner.Should().Be(ConnectionOwner.Classic);

        await launcher.ReleaseClassicAsync();
        ownership.CurrentOwner.Should().Be(ConnectionOwner.None);
    }

    [Fact]
    public async Task TryHandoffToClassic_WhenClassicAlreadyOwns_Succeeds()
    {
        var store = new InMemoryConnectionOwnershipStore();
        await using var ownership = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);
        await ownership.AcquireAsync(ConnectionOwner.Classic);
        var launcher = new ClassicModeLauncher(ownership);

        var result = await launcher.TryHandoffToClassicAsync();

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task TryHandoffToClassic_WhenManagedOwns_StopsManagedAndAcquiresClassic()
    {
        var store = new InMemoryConnectionOwnershipStore();
        await using var ownership = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);
        var runner = new FakeCoreRunnerForHandoff();
        var managedCoordinator = new ManagedConnectionCoordinator(ownership, runner);

        // Start a managed connection
        var startResult = await managedCoordinator.StartAsync(new ManagedConnectionStartRequest
        {
            Payload = CreatePayloadForHandoff(),
        });
        startResult.Success.Should().BeTrue();
        ownership.CurrentOwner.Should().Be(ConnectionOwner.Managed);

        var launcher = new ClassicModeLauncher(ownership, managedCoordinator);
        var result = await launcher.TryHandoffToClassicAsync();

        result.Success.Should().BeTrue();
        runner.StopCount.Should().BeGreaterThan(0);
        managedCoordinator.Status.State.Should().Be(ManagedConnectionState.Ready);
        ownership.CurrentOwner.Should().Be(ConnectionOwner.Classic);

        await launcher.ReleaseClassicAsync();
        ownership.CurrentOwner.Should().Be(ConnectionOwner.None);
    }

    [Fact]
    public async Task TryHandoffToClassic_WhenManagedOwnsButNoCoordinator_ReleasesDirectly()
    {
        var store = new InMemoryConnectionOwnershipStore();
        await using var ownership = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);
        await ownership.AcquireAsync(ConnectionOwner.Managed);
        var launcher = new ClassicModeLauncher(ownership, managedCoordinator: null);

        var result = await launcher.TryHandoffToClassicAsync();

        result.Success.Should().BeTrue();
        ownership.CurrentOwner.Should().Be(ConnectionOwner.Classic);

        await launcher.ReleaseClassicAsync();
        ownership.CurrentOwner.Should().Be(ConnectionOwner.None);
    }

    [Fact]
    public async Task TryHandoffToClassic_HeldLeaseBlocksManagedAcquire()
    {
        var store = new InMemoryConnectionOwnershipStore();
        await using var ownership = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);
        var launcher = new ClassicModeLauncher(ownership);

        var result = await launcher.TryHandoffToClassicAsync();

        result.Success.Should().BeTrue();
        ownership.CurrentOwner.Should().Be(ConnectionOwner.Classic);

        var managedAcquire = await ownership.AcquireAsync(ConnectionOwner.Managed);

        managedAcquire.Acquired.Should().BeFalse();
        managedAcquire.ConflictReason.Should().Contain("owner_already_held:Classic");

        await launcher.ReleaseClassicAsync();
        ownership.CurrentOwner.Should().Be(ConnectionOwner.None);
    }

    [Fact]
    public void IsClassicOperationAllowed_WhenNoOwner_ReturnsTrue()
    {
        var store = new InMemoryConnectionOwnershipStore();
        var ownership = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);
        var launcher = new ClassicModeLauncher(ownership);

        launcher.IsClassicOperationAllowed().Should().BeTrue();
    }

    [Fact]
    public async Task IsClassicOperationAllowed_WhenManagedOwns_ReturnsFalse()
    {
        var store = new InMemoryConnectionOwnershipStore();
        var ownership = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);
        await ownership.AcquireAsync(ConnectionOwner.Managed);
        var launcher = new ClassicModeLauncher(ownership);

        launcher.IsClassicOperationAllowed().Should().BeFalse();
    }

    [Fact]
    public async Task IsClassicOperationAllowed_WhenClassicOwns_ReturnsTrue()
    {
        var store = new InMemoryConnectionOwnershipStore();
        var ownership = new ConnectionOwnershipCoordinator(store, useGlobalMutex: false);
        await ownership.AcquireAsync(ConnectionOwner.Classic);
        var launcher = new ClassicModeLauncher(ownership);

        launcher.IsClassicOperationAllowed().Should().BeTrue();
    }

    #region Helpers

    private static ManagedConfigPayload CreatePayloadForHandoff()
    {
        return new ManagedConfigPayload
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 1,
            SelectionRevision = 1,
            RecommendedProfileId = "plan-a",
            FallbackProfileIds = [],
            Profiles =
            [
                new ManagedProfile
                {
                    Id = "plan-a",
                    Revision = 1,
                    DisplayName = "plan-a",
                    Priority = 100,
                    Recommended = true,
                    Available = true,
                    Protocol = "vless",
                    CorePreference = "xray",
                    Endpoint = new EndpointInfo { Host = "a.example.com", Port = 443 },
                    Transport = new TransportInfo { Network = "raw" },
                    VlessCredentials = new VlessCredentials { Uuid = "00000000-0000-0000-0000-000000000001" },
                    VlessRealitySecurity = new VlessRealitySecurity
                    {
                        Type = "reality",
                        ServerName = "a.example.com",
                        PublicKey = "test-key",
                        ShortId = "01",
                        Fingerprint = "chrome",
                    },
                    Policy = new ProfilePolicy { AllowSystemProxy = true, AllowTun = true },
                },
            ],
            RoutingPolicy = new ManagedRoutingPolicy { Mode = "managed_default" },
            DnsPolicy = new ManagedDnsPolicy
            {
                NormalDns = "https://cloudflare-dns.com/dns-query",
                TunDns = "https://cloudflare-dns.com/dns-query",
            },
            ClientPolicy = new ClientPolicy
            {
                AllowTun = true,
                AllowManualSelection = true,
                AllowAutomaticFailover = true,
            },
        };
    }

    private sealed class FakeCoreRunnerForHandoff : IManagedCoreRunner
    {
        public int StopCount { get; private set; }
        public bool IsRunning => StopCount == 0;

        public Task StartAsync(ManagedRuntimeConfig runtimeConfig, ManagedConnectionMode mode, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default)
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }

    #endregion
}
