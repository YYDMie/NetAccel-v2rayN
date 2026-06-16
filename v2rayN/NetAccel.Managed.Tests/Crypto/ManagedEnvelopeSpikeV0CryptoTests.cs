using System.Security.Cryptography;
using NetAccel.Managed.Crypto;
using NetAccel.Managed.Dtos;

namespace NetAccel.Managed.Tests.Crypto;

public class ManagedEnvelopeSpikeV0CryptoTests
{
    [Fact]
    public void Decrypt_RoundTrip_Success()
    {
        var accessToken = "my-test-access-token-123";
        var plaintext = "{\"profiles\":[]}";
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        var combined = ciphertext.Concat(tag).ToArray();

        var envelope = new ManagedEnvelopeSpikeV0
        {
            Schema = "managed-envelope/spike-v0",
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 1,
            SelectionRevision = 0,
            AccountId = 1,
            InstanceId = "inst-1",
            IssuedAt = "2026-06-01T00:00:00Z",
            ExpiresAt = "2026-06-02T00:00:00Z",
            Algorithm = "access-token-sha256-aes256gcm",
            Nonce = Convert.ToBase64String(nonce),
            Ciphertext = Convert.ToBase64String(combined)
        };

        var crypto = new ManagedEnvelopeSpikeV0Crypto();
        var result = crypto.Decrypt(envelope, accessToken);
        result.Should().Be(plaintext);
    }

    [Fact]
    public void Decrypt_WrongSchema_ReturnsNull()
    {
        var envelope = new ManagedEnvelopeSpikeV0 { Schema = "managed-envelope/v1" };
        var crypto = new ManagedEnvelopeSpikeV0Crypto();
        var result = crypto.Decrypt(envelope, "token");
        result.Should().BeNull();
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ReturnsNull()
    {
        var accessToken = "my-test-access-token-123";
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var plaintextBytes = Encoding.UTF8.GetBytes("test");
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        var combined = ciphertext.Concat(tag).ToArray();
        combined[0] ^= 0xFF; // tamper

        var envelope = new ManagedEnvelopeSpikeV0
        {
            Schema = "managed-envelope/spike-v0",
            Algorithm = "access-token-sha256-aes256gcm",
            Nonce = Convert.ToBase64String(nonce),
            Ciphertext = Convert.ToBase64String(combined)
        };

        var crypto = new ManagedEnvelopeSpikeV0Crypto();
        var result = crypto.Decrypt(envelope, accessToken);
        result.Should().BeNull();
    }
}
