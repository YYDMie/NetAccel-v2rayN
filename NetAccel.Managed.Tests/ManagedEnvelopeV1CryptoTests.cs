using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetAccel.Managed.Crypto;
using NetAccel.Managed.Dto;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedEnvelopeV1CryptoTests
{
    private static (string DevicePrivateKeyPem, string DevicePublicKeyPem, string ServerPrivateKeyPem, string ServerPublicKeyPem) GenerateKeys()
    {
        using var deviceEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var devicePriv = deviceEcdh.ExportPkcs8PrivateKeyPem();
        var devicePub = deviceEcdh.ExportSubjectPublicKeyInfoPem();

        using var serverEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var serverPriv = serverEcdsa.ExportPkcs8PrivateKeyPem();
        var serverPub = serverEcdsa.ExportSubjectPublicKeyInfoPem();

        return (devicePriv, devicePub, serverPriv, serverPub);
    }

    private static string BuildPayload() =>
        JsonSerializer.Serialize(new ManagedConfigPayload
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 42,
            SelectionRevision = 10,
            RecommendedProfileId = "plan-42",
            FallbackProfileIds = ["plan-43"],
            Profiles = [],
            RoutingPolicy = new { mode = "managed_default" },
            DnsPolicy = new { mode = "system_default" },
            ClientPolicy = new ClientPolicy { AllowClassicMode = false, AllowTun = true },
        });

    [Fact]
    public void DecryptAndVerify_ValidEnvelope_ReturnsPayload()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1");

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "inst-1", 3, "device-key-1");

        Assert.NotNull(result);
        Assert.Equal("managed-config/v1", result.PayloadSchema);
        Assert.Equal(42, result.AssignmentRevision);
    }

    [Fact]
    public void DecryptAndVerify_WrongInstance_ReturnsNull()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1");

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "inst-2", 3, "device-key-1");

        Assert.Null(result);
    }

    [Fact]
    public void DecryptAndVerify_WrongAccount_ReturnsNull()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1");

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "inst-1", 99, "device-key-1");

        Assert.Null(result);
    }

    [Fact]
    public void DecryptAndVerify_WrongKeyId_ReturnsNull()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1");

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "inst-1", 3, "device-key-2");

        Assert.Null(result);
    }

    [Fact]
    public void DecryptAndVerify_ExpiredEnvelope_ReturnsNull()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var issued = DateTimeOffset.UtcNow.AddHours(-2);
        var expires = issued.AddMinutes(15);
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1",
            issuedAt: issued, expiresAt: expires);

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "inst-1", 3, "device-key-1");

        Assert.Null(result);
    }

    [Fact]
    public void DecryptAndVerify_UnexpiredEnvelope_ReturnsPayload()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var issued = DateTimeOffset.UtcNow.AddMinutes(-5);
        var expires = issued.AddMinutes(30);
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1",
            issuedAt: issued, expiresAt: expires);

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "inst-1", 3, "device-key-1");

        Assert.NotNull(result);
    }

    [Fact]
    public void DecryptAndVerify_WrongAlgorithm_ReturnsNull()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1");
        envelope.Algorithm = "aes128-cbc";

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "inst-1", 3, "device-key-1");

        Assert.Null(result);
    }

    [Fact]
    public void DecryptAndVerify_WrongSignatureAlgorithm_ReturnsNull()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1");
        envelope.SignatureAlgorithm = "rsa-sha256";

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "inst-1", 3, "device-key-1");

        Assert.Null(result);
    }

    [Fact]
    public void DecryptAndVerify_TamperedCiphertext_ReturnsNull()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1");

        var bytes = Convert.FromBase64String(envelope.Ciphertext);
        bytes[^1] ^= 0xFF;
        envelope.Ciphertext = Convert.ToBase64String(bytes);

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "inst-1", 3, "device-key-1");

        Assert.Null(result);
    }

    [Fact]
    public void DecryptAndVerify_TamperedSignature_ReturnsNull()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1");

        var sigBytes = Convert.FromBase64String(envelope.Signature);
        sigBytes[^1] ^= 0xFF;
        envelope.Signature = Convert.ToBase64String(sigBytes);

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "inst-1", 3, "device-key-1");

        Assert.Null(result);
    }

    [Fact]
    public void DecryptAndVerify_WrongServerPublicKey_ReturnsNull()
    {
        var (devicePriv, devicePub, serverPriv, _) = GenerateKeys();
        var (_, _, _, otherServerPub) = GenerateKeys();
        var payload = BuildPayload();
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1");

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, otherServerPub, "inst-1", 3, "device-key-1");

        Assert.Null(result);
    }

    [Fact]
    public void DecryptAndVerify_WrongDevicePrivateKey_ReturnsNull()
    {
        var (_, devicePub, serverPriv, serverPub) = GenerateKeys();
        var (otherDevicePriv, _, _, _) = GenerateKeys();
        var payload = BuildPayload();
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1");

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, otherDevicePriv, serverPub, "inst-1", 3, "device-key-1");

        Assert.Null(result);
    }

    [Fact]
    public void DecryptAndVerify_BadSchema_ReturnsNull()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1");
        envelope.Schema = "managed-envelope/spike-v0";

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "inst-1", 3, "device-key-1");

        Assert.Null(result);
    }

    [Fact]
    public void DecryptAndVerify_NegativeRevision_ReturnsNull()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1");
        envelope.AssignmentRevision = -1;

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "inst-1", 3, "device-key-1");

        Assert.Null(result);
    }

    [Fact]
    public void BuildAadPreimage_IsDeterministic()
    {
        var envelope = new ManagedEnvelopeV1
        {
            Schema = "managed-envelope/v1",
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 1,
            SelectionRevision = 2,
            AccountId = 3,
            InstanceId = "inst",
            KeyId = "key",
            IssuedAt = "2026-06-16T00:00:00Z",
            ExpiresAt = "2026-06-16T00:15:00Z",
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

        var p1 = ManagedEnvelopeV1Crypto.BuildAadPreimage(envelope);
        var p2 = ManagedEnvelopeV1Crypto.BuildAadPreimage(envelope);

        Assert.Equal(p1, p2);
    }

    [Fact]
    public void BuildAadPreimage_MatchesExpectedFormat()
    {
        var envelope = new ManagedEnvelopeV1
        {
            Schema = "a",
            PayloadSchema = "b",
            AssignmentRevision = 1,
            SelectionRevision = 0,
            AccountId = 1,
            InstanceId = "i",
            KeyId = "k",
            IssuedAt = "t1",
            ExpiresAt = "t2",
            MinClientVersion = "v",
            Algorithm = "alg",
            EphemeralPublicKey = "ep",
            Salt = "s",
            Nonce = "n",
            Ciphertext = "ct",
        };

        var preimage = ManagedEnvelopeV1Crypto.BuildAadPreimage(envelope);
        var expected = "1:a\n1:b\n1:1\n1:0\n1:1\n1:i\n1:k\n2:t1\n2:t2\n1:v\n3:alg\n2:ep\n1:s\n1:n\n2:ct\n";
        Assert.Equal(expected, Encoding.UTF8.GetString(preimage));
    }

    [Fact]
    public void DecryptAndVerify_SignaturePreimageMismatch_ReturnsNull()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1");

        // Mutate a field that is part of the preimage but not the ciphertext
        envelope.InstanceId = "tampered";

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "tampered", 3, "device-key-1");

        // Even though instance_id now matches the expected value,
        // the signature was computed over the original instance_id,
        // so verification should fail.
        // Actually, since we changed envelope.InstanceId, the preimage changed,
        // but the signature didn't. And we pass "tampered" as expectedInstanceId
        // so account/key match. The signature should fail.
        Assert.Null(result);
    }

    [Fact]
    public void EncryptForTest_ProducesValidEnvelope()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1");

        Assert.Equal("managed-envelope/v1", envelope.Schema);
        Assert.Equal("managed-config/v1", envelope.PayloadSchema);
        Assert.Equal("p256-ecdh-hkdf-sha256-aes256gcm", envelope.Algorithm);
        Assert.Equal("ecdsa-p256-sha256", envelope.SignatureAlgorithm);
        Assert.False(string.IsNullOrEmpty(envelope.Signature));
        Assert.False(string.IsNullOrEmpty(envelope.EphemeralPublicKey));
        Assert.False(string.IsNullOrEmpty(envelope.Salt));
        Assert.False(string.IsNullOrEmpty(envelope.Nonce));
        Assert.False(string.IsNullOrEmpty(envelope.Ciphertext));
    }

    [Fact]
    public void DecryptAndVerify_RoundTrip_WithFixedClock()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var now = DateTimeOffset.Parse("2026-06-16T12:00:00Z");
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1",
            issuedAt: now, expiresAt: now.AddMinutes(15));

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "inst-1", 3, "device-key-1",
            clock: () => now.AddMinutes(5));

        Assert.NotNull(result);
    }

    [Fact]
    public void DecryptAndVerify_ClockExactlyAtExpiry_ReturnsNull()
    {
        var (devicePriv, devicePub, serverPriv, serverPub) = GenerateKeys();
        var payload = BuildPayload();
        var now = DateTimeOffset.Parse("2026-06-16T12:00:00Z");
        var envelope = ManagedEnvelopeV1Crypto.EncryptForTest(
            payload, devicePub, serverPriv, "server-key-1", 3, "inst-1", "device-key-1",
            issuedAt: now, expiresAt: now.AddMinutes(15));

        var result = ManagedEnvelopeV1Crypto.DecryptAndVerify(
            envelope, devicePriv, serverPub, "inst-1", 3, "device-key-1",
            clock: () => now.AddMinutes(15));

        Assert.Null(result);
    }
}
