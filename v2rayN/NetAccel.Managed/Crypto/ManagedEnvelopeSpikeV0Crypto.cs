using System.Security.Cryptography;
using NetAccel.Managed.Dtos;

namespace NetAccel.Managed.Crypto;

/// <summary>
/// R1-only spike-v0 envelope decryption using access-token SHA-256 + AES-256-GCM.
/// This is intentionally temporary and will be replaced by stable managed-envelope/v1 in WP-10/R2.
/// </summary>
public interface IManagedEnvelopeSpikeV0Crypto
{
    string? Decrypt(ManagedEnvelopeSpikeV0 envelope, string accessToken);
}

public sealed class ManagedEnvelopeSpikeV0Crypto : IManagedEnvelopeSpikeV0Crypto
{
    public string? Decrypt(ManagedEnvelopeSpikeV0 envelope, string accessToken)
    {
        if (envelope.Schema != "managed-envelope/spike-v0")
            return null;
        if (envelope.Algorithm != "access-token-sha256-aes256gcm")
            return null;

        try
        {
            var key = SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
            var nonce = Convert.FromBase64String(envelope.Nonce);
            var ciphertext = Convert.FromBase64String(envelope.Ciphertext);

            using var aes = new AesGcm(key, 16);
            var plaintext = new byte[ciphertext.Length - 16];
            var tag = ciphertext[^16..];
            var encryptedData = ciphertext[..^16];

            aes.Decrypt(nonce, encryptedData, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            return null;
        }
    }
}
