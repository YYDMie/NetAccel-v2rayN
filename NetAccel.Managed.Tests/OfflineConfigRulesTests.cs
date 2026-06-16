using NetAccel.Managed.Cache;
using NetAccel.Managed.Dto;
using Xunit;

namespace NetAccel.Managed.Tests;

public class OfflineConfigRulesTests
{
    private static EnvelopeCacheEntry CreateEntry(
        string instanceId = "inst-1",
        string keyId = "key-1",
        DateTimeOffset? expiresAt = null)
    {
        var envelope = new ManagedEnvelopeV1
        {
            Schema = "managed-envelope/v1",
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 42,
            SelectionRevision = 10,
            AccountId = 3,
            InstanceId = instanceId,
            KeyId = keyId,
            IssuedAt = "2026-06-16T00:00:00Z",
            ExpiresAt = (expiresAt ?? DateTimeOffset.Parse("2026-06-16T12:00:00Z")).ToString("yyyy-MM-ddTHH:mm:ssZ"),
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

        return new EnvelopeCacheEntry
        {
            Envelope = envelope,
            Metadata = new EnvelopeCacheMetadata
            {
                AssignmentRevision = 42,
                SelectionRevision = 10,
                InstanceId = instanceId,
                KeyId = keyId,
                ExpiresAt = expiresAt ?? DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
                Schema = "managed-envelope/v1",
                CachedAt = DateTimeOffset.UtcNow,
            },
        };
    }

    [Fact]
    public void Evaluate_NullEntry_Rejects()
    {
        var rules = new OfflineConfigRules();
        var result = rules.Evaluate(null, "inst-1", "key-1");

        Assert.False(result.CanUseOffline);
        Assert.Equal("no cached envelope", result.Reason);
    }

    [Fact]
    public void Evaluate_ValidUnexpiredEntry_Allows()
    {
        var rules = new OfflineConfigRules();
        var entry = CreateEntry(expiresAt: DateTimeOffset.UtcNow.AddHours(1));
        var result = rules.Evaluate(entry, "inst-1", "key-1", DateTimeOffset.UtcNow);

        Assert.True(result.CanUseOffline);
        Assert.False(result.IsExpired);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Evaluate_ExpiredEntry_RejectedAsExpired()
    {
        var rules = new OfflineConfigRules();
        var entry = CreateEntry(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        var result = rules.Evaluate(entry, "inst-1", "key-1", DateTimeOffset.UtcNow);

        Assert.False(result.CanUseOffline);
        Assert.True(result.IsExpired);
        Assert.Equal("cached envelope has expired", result.Reason);
    }

    [Fact]
    public void Evaluate_InstanceMismatch_Rejects()
    {
        var rules = new OfflineConfigRules();
        var entry = CreateEntry(instanceId: "inst-1");
        var result = rules.Evaluate(entry, "inst-2", "key-1", DateTimeOffset.UtcNow);

        Assert.False(result.CanUseOffline);
        Assert.Equal("cached envelope instance_id mismatch", result.Reason);
    }

    [Fact]
    public void Evaluate_KeyIdMismatch_Rejects()
    {
        var rules = new OfflineConfigRules();
        var entry = CreateEntry(keyId: "key-1");
        var result = rules.Evaluate(entry, "inst-1", "key-2", DateTimeOffset.UtcNow);

        Assert.False(result.CanUseOffline);
        Assert.Equal("cached envelope key_id mismatch", result.Reason);
    }

    [Fact]
    public void Evaluate_SpikeV0_Rejects()
    {
        var rules = new OfflineConfigRules();
        var envelope = new ManagedEnvelopeV1
        {
            Schema = "managed-envelope/spike-v0",
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 1,
            SelectionRevision = 0,
            AccountId = 1,
            InstanceId = "inst-1",
            KeyId = "key-1",
            IssuedAt = "2026-06-16T00:00:00Z",
            ExpiresAt = "2026-06-16T12:00:00Z",
            MinClientVersion = "1.0.0",
            Algorithm = "aes128-cbc",
            EphemeralPublicKey = "epub",
            Salt = "salt",
            Nonce = "nonce",
            Ciphertext = "ct",
            SignatureAlgorithm = "ecdsa-p256-sha256",
            SignatureKeyId = "sk",
            Signature = "sig",
        };

        var entry = new EnvelopeCacheEntry
        {
            Envelope = envelope,
            Metadata = new EnvelopeCacheMetadata
            {
                AssignmentRevision = 1,
                SelectionRevision = 0,
                InstanceId = "inst-1",
                KeyId = "key-1",
                ExpiresAt = DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
                Schema = "managed-envelope/spike-v0",
                CachedAt = DateTimeOffset.UtcNow,
            },
        };

        var result = rules.Evaluate(entry, "inst-1", "key-1", DateTimeOffset.UtcNow);
        Assert.False(result.CanUseOffline);
        Assert.Equal("cached envelope is not managed-envelope/v1", result.Reason);
    }

    [Fact]
    public void CanSelectOffline_ProfileExists_ReturnsTrue()
    {
        var rules = new OfflineConfigRules();
        var payload = new ManagedConfigPayload
        {
            Profiles =
            [
                new ManagedProfile { Id = "plan-1", DisplayName = "Test", Available = true },
            ],
        };

        Assert.True(rules.CanSelectOffline(payload, "plan-1"));
    }

    [Fact]
    public void CanSelectOffline_ProfileMissing_ReturnsFalse()
    {
        var rules = new OfflineConfigRules();
        var payload = new ManagedConfigPayload
        {
            Profiles =
            [
                new ManagedProfile { Id = "plan-1", DisplayName = "Test", Available = true },
            ],
        };

        Assert.False(rules.CanSelectOffline(payload, "plan-2"));
    }

    [Fact]
    public void CanSelectOffline_NullPayload_ReturnsFalse()
    {
        var rules = new OfflineConfigRules();
        Assert.False(rules.CanSelectOffline(null, "plan-1"));
    }

    [Fact]
    public void CanSelectOffline_EmptyProfileId_ReturnsFalse()
    {
        var rules = new OfflineConfigRules();
        var payload = new ManagedConfigPayload
        {
            Profiles = [new ManagedProfile { Id = "plan-1" }],
        };

        Assert.False(rules.CanSelectOffline(payload, ""));
    }
}
