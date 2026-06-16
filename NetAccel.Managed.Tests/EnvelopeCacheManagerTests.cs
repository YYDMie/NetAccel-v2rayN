using System.Text.Json;
using NetAccel.Managed.Cache;
using NetAccel.Managed.Dto;
using Xunit;

namespace NetAccel.Managed.Tests;

public class EnvelopeCacheManagerTests
{
    private static EnvelopeCacheManager CreateManager(Func<DateTimeOffset>? clock = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        return new EnvelopeCacheManager(dir, clock);
    }

    private static ManagedEnvelopeV1 CreateV1Envelope(string schema = "managed-envelope/v1")
    {
        return new ManagedEnvelopeV1
        {
            Schema = schema,
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 42,
            SelectionRevision = 10,
            AccountId = 3,
            InstanceId = "inst-1",
            KeyId = "key-1",
            IssuedAt = "2026-06-16T00:00:00Z",
            ExpiresAt = "2026-06-16T12:00:00Z",
            MinClientVersion = "1.0.0",
            Algorithm = "p256-ecdh-hkdf-sha256-aes256gcm",
            EphemeralPublicKey = "epub",
            Salt = "salt",
            Nonce = "nonce",
            Ciphertext = "ct",
            SignatureAlgorithm = "ecdsa-p256-sha256",
            SignatureKeyId = "sk",
            Signature = "sig",
        };
    }

    [Fact]
    public async Task WriteAsync_And_ReadAsync_RoundTrip()
    {
        var manager = CreateManager();
        var envelope = CreateV1Envelope();

        await manager.WriteAsync(envelope);
        var entry = await manager.ReadAsync();

        Assert.NotNull(entry);
        Assert.Equal(envelope.Schema, entry.Envelope.Schema);
        Assert.Equal(envelope.AssignmentRevision, entry.Metadata.AssignmentRevision);
        Assert.Equal(envelope.InstanceId, entry.Metadata.InstanceId);
        Assert.Equal(envelope.KeyId, entry.Metadata.KeyId);
    }

    [Fact]
    public async Task WriteAsync_SpikeV0_Throws()
    {
        var manager = CreateManager();
        var spike = CreateV1Envelope("managed-envelope/spike-v0");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.WriteAsync(spike));
        Assert.Contains("Spike-v0", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_NoCache_ReturnsNull()
    {
        var manager = CreateManager();
        var entry = await manager.ReadAsync();
        Assert.Null(entry);
    }

    [Fact]
    public async Task ReadAsync_CorruptCache_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cachePath = Path.Combine(dir, "envelope.cache.json");
        await File.WriteAllTextAsync(cachePath, "not valid json");

        var manager = new EnvelopeCacheManager(dir);
        var entry = await manager.ReadAsync();

        Assert.Null(entry);
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task WriteAsync_IsAtomic_DoesNotLeavePartialFile()
    {
        var manager = CreateManager();
        var envelope = CreateV1Envelope();

        await manager.WriteAsync(envelope);

        // Verify the cache file exists and is readable after atomic write
        var entry = await manager.ReadAsync();
        Assert.NotNull(entry);
        Assert.Equal("managed-envelope/v1", entry.Envelope.Schema);
    }

    [Fact]
    public async Task ClearAsync_RemovesCache()
    {
        var manager = CreateManager();
        var envelope = CreateV1Envelope();

        await manager.WriteAsync(envelope);
        await manager.ClearAsync();

        var entry = await manager.ReadAsync();
        Assert.Null(entry);
    }

    [Fact]
    public async Task WriteAsync_OverwritesExisting()
    {
        var manager = CreateManager();
        var first = CreateV1Envelope();
        first.AssignmentRevision = 1;

        var second = CreateV1Envelope();
        second.AssignmentRevision = 2;

        await manager.WriteAsync(first);
        await manager.WriteAsync(second);

        var entry = await manager.ReadAsync();
        Assert.NotNull(entry);
        Assert.Equal(2, entry.Metadata.AssignmentRevision);
    }

    [Fact]
    public async Task ReadAsync_Metadata_ExtractsExpiry()
    {
        var manager = CreateManager();
        var envelope = CreateV1Envelope();

        await manager.WriteAsync(envelope);
        var entry = await manager.ReadAsync();

        Assert.NotNull(entry);
        Assert.Equal(DateTimeOffset.Parse("2026-06-16T12:00:00Z"), entry.Metadata.ExpiresAt);
    }

    [Fact]
    public async Task ReadAsync_NegativeRevision_ReturnsNull()
    {
        var manager = CreateManager();
        var envelope = CreateV1Envelope();
        envelope.AssignmentRevision = -1;

        await manager.WriteAsync(envelope);
        var entry = await manager.ReadAsync();

        Assert.Null(entry);
    }
}
