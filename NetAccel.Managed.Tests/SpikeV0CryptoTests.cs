using System.Text.Json;
using NetAccel.Managed.Crypto;
using NetAccel.Managed.Dto;
using Xunit;

namespace NetAccel.Managed.Tests;

public class SpikeV0CryptoTests
{
    [Fact]
    public void Decrypt_ValidEnvelope_ReturnsPayload()
    {
        var payload = new ManagedConfigPayload
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 42,
            SelectionRevision = 10,
            RecommendedProfileId = "plan-42",
            FallbackProfileIds = ["plan-43"],
            Profiles = [],
            RoutingPolicy = new { },
            DnsPolicy = new { },
            ClientPolicy = new ClientPolicy { AllowClassicMode = false, AllowTun = true, AllowLocalProxy = false, AllowManualSelection = true, AllowAutomaticFailover = true, OfflineGraceSeconds = 3600 },
        };
        var json = JsonSerializer.Serialize(payload);
        var accessToken = "test-access-token-123";
        var (nonce, ciphertext) = SpikeV0EnvelopeCrypto.EncryptForTest(json, accessToken);

        var envelope = new ManagedEnvelopeSpikeV0
        {
            Schema = "managed-envelope/spike-v0",
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 42,
            SelectionRevision = 10,
            AccountId = 3,
            InstanceId = "550e8400-e29b-41d4-a716-446655440002",
            IssuedAt = "2026-06-12T14:00:00Z",
            ExpiresAt = "2026-06-12T14:15:00Z",
            Algorithm = "access-token-sha256-aes256gcm",
            Nonce = nonce,
            Ciphertext = ciphertext,
        };

        var decrypted = SpikeV0EnvelopeCrypto.Decrypt(envelope, accessToken);
        Assert.NotNull(decrypted);
        Assert.Equal(42, decrypted.AssignmentRevision);
        Assert.Equal("managed-config/v1", decrypted.PayloadSchema);
    }

    [Fact]
    public void Decrypt_WrongToken_ReturnsNull()
    {
        var payload = new ManagedConfigPayload { PayloadSchema = "managed-config/v1", AssignmentRevision = 1, SelectionRevision = 0, RecommendedProfileId = null, FallbackProfileIds = [], Profiles = [] };
        var json = JsonSerializer.Serialize(payload);
        var (nonce, ciphertext) = SpikeV0EnvelopeCrypto.EncryptForTest(json, "correct-token");

        var envelope = new ManagedEnvelopeSpikeV0
        {
            Schema = "managed-envelope/spike-v0",
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 1,
            SelectionRevision = 0,
            AccountId = 1,
            InstanceId = "i1",
            IssuedAt = "2026-06-12T14:00:00Z",
            ExpiresAt = "2026-06-12T14:15:00Z",
            Algorithm = "access-token-sha256-aes256gcm",
            Nonce = nonce,
            Ciphertext = ciphertext,
        };

        var decrypted = SpikeV0EnvelopeCrypto.Decrypt(envelope, "wrong-token");
        Assert.Null(decrypted);
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ReturnsNull()
    {
        var payload = new ManagedConfigPayload { PayloadSchema = "managed-config/v1", AssignmentRevision = 1, SelectionRevision = 0, RecommendedProfileId = null, FallbackProfileIds = [], Profiles = [] };
        var json = JsonSerializer.Serialize(payload);
        var accessToken = "token";
        var (nonce, ciphertext) = SpikeV0EnvelopeCrypto.EncryptForTest(json, accessToken);

        // Tamper with last byte
        var bytes = Convert.FromBase64String(ciphertext);
        bytes[^1] ^= 0xFF;
        var tamperedCiphertext = Convert.ToBase64String(bytes);

        var envelope = new ManagedEnvelopeSpikeV0
        {
            Schema = "managed-envelope/spike-v0",
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 1,
            SelectionRevision = 0,
            AccountId = 1,
            InstanceId = "i1",
            IssuedAt = "2026-06-12T14:00:00Z",
            ExpiresAt = "2026-06-12T14:15:00Z",
            Algorithm = "access-token-sha256-aes256gcm",
            Nonce = nonce,
            Ciphertext = tamperedCiphertext,
        };

        var decrypted = SpikeV0EnvelopeCrypto.Decrypt(envelope, accessToken);
        Assert.Null(decrypted);
    }

    [Fact]
    public void Decrypt_UnsupportedAlgorithm_Throws()
    {
        var envelope = new ManagedEnvelopeSpikeV0
        {
            Algorithm = "unknown",
            Nonce = "abc",
            Ciphertext = "def",
        };
        Assert.Throws<NotSupportedException>(() => SpikeV0EnvelopeCrypto.Decrypt(envelope, "token"));
    }

    [Fact]
    public void Decrypt_InvalidNonceLength_ReturnsNull()
    {
        var envelope = new ManagedEnvelopeSpikeV0
        {
            Schema = "managed-envelope/spike-v0",
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 1,
            SelectionRevision = 0,
            AccountId = 1,
            InstanceId = "i1",
            IssuedAt = "2026-06-12T14:00:00Z",
            ExpiresAt = "2026-06-12T14:15:00Z",
            Algorithm = "access-token-sha256-aes256gcm",
            Nonce = Convert.ToBase64String(new byte[8]), // wrong length
            Ciphertext = Convert.ToBase64String(new byte[32]),
        };

        var decrypted = SpikeV0EnvelopeCrypto.Decrypt(envelope, "token");
        Assert.Null(decrypted);
    }
}
