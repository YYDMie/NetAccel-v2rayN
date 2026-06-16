using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetAccel.Managed.Dto;

namespace NetAccel.Managed.Crypto;

/// <summary>
/// Stable managed-envelope/v1 decryption and verification.
/// Replaces spike-v0 for any disk cache or offline use.
/// </summary>
public static class ManagedEnvelopeV1Crypto
{
    private const string ExpectedSchema = "managed-envelope/v1";
    private const string ExpectedPayloadSchema = "managed-config/v1";
    private const string ExpectedAlgorithm = "p256-ecdh-hkdf-sha256-aes256gcm";
    private const string ExpectedSignatureAlgorithm = "ecdsa-p256-sha256";
    private const string HkdfInfo = "netaccel-managed-envelope-v1";
    private const int NonceLength = 12;
    private const int TagLength = 16;

    /// <summary>
    /// Decrypts and verifies a managed-envelope/v1.
    /// Returns null if validation, signature verification, or decryption fails.
    /// Does not persist plaintext.
    /// </summary>
    public static ManagedConfigPayload? DecryptAndVerify(
        ManagedEnvelopeV1 envelope,
        string devicePrivateKeyPem,
        string serverPublicKeyPem,
        string expectedInstanceId,
        int expectedAccountId,
        string expectedKeyId,
        Func<DateTimeOffset>? clock = null)
    {
        if (envelope == null) throw new ArgumentNullException(nameof(envelope));
        if (string.IsNullOrEmpty(devicePrivateKeyPem)) throw new ArgumentException("Device private key is required", nameof(devicePrivateKeyPem));
        if (string.IsNullOrEmpty(serverPublicKeyPem)) throw new ArgumentException("Server public key is required", nameof(serverPublicKeyPem));

        var now = clock?.Invoke() ?? DateTimeOffset.UtcNow;

        // 1. Schema and algorithm validation
        if (envelope.Schema != ExpectedSchema)
            return null;
        if (envelope.PayloadSchema != ExpectedPayloadSchema)
            return null;
        if (envelope.Algorithm != ExpectedAlgorithm)
            return null;
        if (envelope.SignatureAlgorithm != ExpectedSignatureAlgorithm)
            return null;

        // 2. Audience binding
        if (envelope.AccountId != expectedAccountId)
            return null;
        if (envelope.InstanceId != expectedInstanceId)
            return null;
        if (envelope.KeyId != expectedKeyId)
            return null;

        // 3. Expiry
        if (!DateTimeOffset.TryParse(envelope.ExpiresAt, out var expiresAt))
            return null;
        if (expiresAt <= now)
            return null;

        // 4. Revision sanity
        if (envelope.AssignmentRevision < 0 || envelope.SelectionRevision < 0)
            return null;

        // 5. Signature verification
        var preimage = BuildAadPreimage(envelope);
        if (!VerifySignature(serverPublicKeyPem, preimage, envelope.Signature))
            return null;

        // 6. ECDH + HKDF + AES-GCM decryption
        try
        {
            var plaintext = DecryptPayload(envelope, devicePrivateKeyPem);
            if (plaintext == null)
                return null;

            return JsonSerializer.Deserialize<ManagedConfigPayload>(plaintext, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the deterministic AAD preimage for envelope v1 signing.
    /// Must match Go crypto.BuildAADPreimage exactly.
    /// </summary>
    public static byte[] BuildAadPreimage(ManagedEnvelopeV1 envelope)
    {
        if (envelope == null) throw new ArgumentNullException(nameof(envelope));

        var fields = new[]
        {
            envelope.Schema,
            envelope.PayloadSchema,
            envelope.AssignmentRevision.ToString(),
            envelope.SelectionRevision.ToString(),
            envelope.AccountId.ToString(),
            envelope.InstanceId,
            envelope.KeyId,
            envelope.IssuedAt,
            envelope.ExpiresAt,
            envelope.MinClientVersion,
            envelope.Algorithm,
            envelope.EphemeralPublicKey,
            envelope.Salt,
            envelope.Nonce,
            envelope.Ciphertext,
        };

        using var stream = new MemoryStream();
        foreach (var field in fields)
        {
            var value = Encoding.UTF8.GetBytes(field ?? string.Empty);
            var prefix = Encoding.UTF8.GetBytes($"{value.Length}:");
            stream.Write(prefix);
            stream.Write(value);
            stream.WriteByte((byte)'\n');
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Encrypts and signs a payload for test interop (Go/.NET fixture round-trip).
    /// Returns a complete <see cref="ManagedEnvelopeV1"/>.
    /// </summary>
    public static ManagedEnvelopeV1 EncryptForTest(
        string jsonPayload,
        string devicePublicKeyPem,
        string serverSigningKeyPem,
        string signatureKeyId,
        int accountId,
        string instanceId,
        string keyId,
        int assignmentRevision = 1,
        int selectionRevision = 0,
        DateTimeOffset? issuedAt = null,
        DateTimeOffset? expiresAt = null)
    {
        // Generate ephemeral key pair
        using var ephemeralEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        // Import device public key
        using var deviceEcdh = ECDiffieHellman.Create();
        deviceEcdh.ImportFromPem(devicePublicKeyPem);

        // Derive shared secret
        var sharedSecret = ephemeralEcdh.DeriveKeyMaterial(deviceEcdh.PublicKey);

        // Generate salt
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);

        // Derive AES key
        var aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, salt, Encoding.UTF8.GetBytes(HkdfInfo));

        // Encrypt
        var plaintext = Encoding.UTF8.GetBytes(jsonPayload);
        var nonce = new byte[NonceLength];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];

        using var aes = new AesGcm(aesKey, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Combine nonce || ciphertext || tag
        var combined = new byte[NonceLength + plaintext.Length + TagLength];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceLength);
        Buffer.BlockCopy(ciphertext, 0, combined, NonceLength, plaintext.Length);
        Buffer.BlockCopy(tag, 0, combined, NonceLength + plaintext.Length, TagLength);

        var ephemeralPublicKeyPem = ephemeralEcdh.ExportSubjectPublicKeyInfoPem();
        var saltB64 = Convert.ToBase64String(salt);
        var nonceB64 = Convert.ToBase64String(nonce);
        var ciphertextB64 = Convert.ToBase64String(combined);

        var issueTime = issuedAt ?? DateTimeOffset.UtcNow;
        var expiryTime = expiresAt ?? issueTime.AddMinutes(15);

        var envelope = new ManagedEnvelopeV1
        {
            Schema = ExpectedSchema,
            PayloadSchema = ExpectedPayloadSchema,
            AssignmentRevision = assignmentRevision,
            SelectionRevision = selectionRevision,
            AccountId = accountId,
            InstanceId = instanceId,
            KeyId = keyId,
            IssuedAt = issueTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ExpiresAt = expiryTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            MinClientVersion = "1.0.0",
            Algorithm = ExpectedAlgorithm,
            EphemeralPublicKey = ephemeralPublicKeyPem,
            Salt = saltB64,
            Nonce = nonceB64,
            Ciphertext = ciphertextB64,
            SignatureAlgorithm = ExpectedSignatureAlgorithm,
            SignatureKeyId = signatureKeyId,
            Signature = "",
        };

        var preimage = BuildAadPreimage(envelope);
        envelope.Signature = SignForTest(serverSigningKeyPem, preimage);

        return envelope;
    }

    private static byte[]? DecryptPayload(ManagedEnvelopeV1 envelope, string devicePrivateKeyPem)
    {
        using var deviceEcdh = ECDiffieHellman.Create();
        deviceEcdh.ImportFromPem(devicePrivateKeyPem);

        using var ephemeralEcdh = ECDiffieHellman.Create();
        ephemeralEcdh.ImportFromPem(envelope.EphemeralPublicKey);

        var sharedSecret = deviceEcdh.DeriveKeyMaterial(ephemeralEcdh.PublicKey);

        var salt = Convert.FromBase64String(envelope.Salt);
        var aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, salt, Encoding.UTF8.GetBytes(HkdfInfo));

        var combined = Convert.FromBase64String(envelope.Ciphertext);
        if (combined.Length < NonceLength + TagLength)
            return null;

        var nonce = combined[..NonceLength];
        var cipherBytes = combined[NonceLength..^TagLength];
        var tagBytes = combined[^TagLength..];
        var plaintext = new byte[cipherBytes.Length];

        using var aes = new AesGcm(aesKey, TagLength);
        aes.Decrypt(nonce, cipherBytes, tagBytes, plaintext);

        return plaintext;
    }

    private static bool VerifySignature(string serverPublicKeyPem, byte[] preimage, string signatureB64)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(serverPublicKeyPem);

            var signature = Convert.FromBase64String(signatureB64);
            return ecdsa.VerifyData(preimage, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Signs the preimage using a PEM-encoded P-256 private key.
    /// Only for test fixtures.
    /// </summary>
    private static string SignForTest(string privateKeyPem, byte[] data)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        var signature = ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Convert.ToBase64String(signature);
    }
}
