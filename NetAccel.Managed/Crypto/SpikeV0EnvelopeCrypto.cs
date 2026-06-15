using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetAccel.Managed.Dto;

namespace NetAccel.Managed.Crypto;

/// <summary>
/// R1-only spike-v0 envelope decryption.
/// Uses access-token SHA-256 derived AES-256-GCM key.
/// IMPORTANT: This is explicitly R1 spike-only. WP-04/R2 will replace with stable managed-envelope/v1.
/// - Decrypted plaintext is kept only in memory.
/// - spike-v0 envelope is NOT cached to disk.
/// - Offline startup is NOT supported.
/// </summary>
public static class SpikeV0EnvelopeCrypto
{
    /// <summary>
    /// Decrypts a spike-v0 envelope using the current access token.
    /// Returns null if decryption fails or the envelope is malformed.
    /// </summary>
    public static ManagedConfigPayload? Decrypt(ManagedEnvelopeSpikeV0 envelope, string accessToken)
    {
        if (envelope == null) throw new ArgumentNullException(nameof(envelope));
        if (string.IsNullOrEmpty(accessToken)) throw new ArgumentException("Access token is required", nameof(accessToken));
        if (envelope.Algorithm != "access-token-sha256-aes256gcm")
        {
            throw new NotSupportedException($"Algorithm {envelope.Algorithm} is not supported by spike-v0");
        }

        try
        {
            var key = DeriveKey(accessToken);
            var nonce = Convert.FromBase64String(envelope.Nonce);
            var ciphertext = Convert.FromBase64String(envelope.Ciphertext);

            if (nonce.Length != 12)
            {
                // AES-GCM standard nonce is 12 bytes; if base64 decodes to something else, reject
                return null;
            }

            // ciphertext includes auth tag appended by typical AES-GCM implementations
            // In .NET AesGcm, ciphertext and tag are separate.
            // We'll assume the standard 16-byte auth tag is appended to the ciphertext.
            if (ciphertext.Length < 16)
            {
                return null;
            }

            var tagLength = 16;
            var cipherBytes = ciphertext[..^tagLength];
            var tagBytes = ciphertext[^tagLength..];
            var plaintext = new byte[cipherBytes.Length];

            using var aes = new AesGcm(key, tagLength);
            aes.Decrypt(nonce, cipherBytes, tagBytes, plaintext);

            var json = Encoding.UTF8.GetString(plaintext);
            return JsonSerializer.Deserialize<ManagedConfigPayload>(json, new System.Text.Json.JsonSerializerOptions
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
    /// Encrypts a payload for test interop (Go/.NET fixture round-trip).
    /// Returns base64(nonce) and base64(ciphertext+tag).
    /// </summary>
    public static (string Nonce, string Ciphertext) EncryptForTest(string jsonPayload, string accessToken)
    {
        var key = DeriveKey(accessToken);
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var plaintext = Encoding.UTF8.GetBytes(jsonPayload);
        var tagLength = 16;
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[tagLength];

        using var aes = new AesGcm(key, tagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var combined = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);

        return (Convert.ToBase64String(nonce), Convert.ToBase64String(combined));
    }

    private static byte[] DeriveKey(string accessToken)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
    }
}
