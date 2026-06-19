using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace NetAccel.Managed.Update;

public sealed record ManagedReleaseManifest
{
    [JsonPropertyName("client_product")]
    public required string ClientProduct { get; init; }

    [JsonPropertyName("platform")]
    public required string Platform { get; init; }

    [JsonPropertyName("arch")]
    public required string Arch { get; init; }

    [JsonPropertyName("channel")]
    public required string Channel { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("artifact_url")]
    public required string ArtifactUrl { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("signature")]
    public required string Signature { get; init; }

    [JsonPropertyName("signature_algorithm")]
    public required string SignatureAlgorithm { get; init; }

    [JsonPropertyName("signature_key_id")]
    public required string SignatureKeyId { get; init; }

    [JsonPropertyName("min_version")]
    public string? MinVersion { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

public sealed record ManagedUpdateVerificationResult(bool Success, string? ErrorCode = null)
{
    public static ManagedUpdateVerificationResult Ok() => new(true);
    public static ManagedUpdateVerificationResult Fail(string errorCode) => new(false, errorCode);
}

public interface IManagedReleaseTrustStore
{
    bool TryGetPublicKey(string keyId, out string publicKeyPem);
}

public sealed class ManagedReleaseTrustStore : IManagedReleaseTrustStore
{
    private readonly IReadOnlyDictionary<string, string> _keys;

    public ManagedReleaseTrustStore(IReadOnlyDictionary<string, string> keys)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    public bool TryGetPublicKey(string keyId, out string publicKeyPem)
        => _keys.TryGetValue(keyId, out publicKeyPem!);
}

public sealed class DirectoryManagedReleaseTrustStore : IManagedReleaseTrustStore
{
    private readonly string _directory;

    public DirectoryManagedReleaseTrustStore(string directory)
    {
        _directory = Path.GetFullPath(directory);
    }

    public bool TryGetPublicKey(string keyId, out string publicKeyPem)
    {
        publicKeyPem = string.Empty;
        if (string.IsNullOrWhiteSpace(keyId) || keyId.Length > 128 ||
            !keyId.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.'))
        {
            return false;
        }
        var path = Path.Combine(_directory, keyId + ".pem");
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }
            publicKeyPem = File.ReadAllText(path);
            return publicKeyPem.Contains("BEGIN PUBLIC KEY", StringComparison.Ordinal);
        }
        catch
        {
            publicKeyPem = string.Empty;
            return false;
        }
    }
}

public sealed class ManagedReleaseVerifier
{
    public const string Product = "netaccel-v2rayn-wpf";
    public const string SignatureAlgorithm = "ecdsa-p256-sha256";

    private readonly IManagedReleaseTrustStore _trustStore;

    public ManagedReleaseVerifier(IManagedReleaseTrustStore trustStore)
    {
        _trustStore = trustStore;
    }

    public ManagedUpdateVerificationResult VerifyManifest(
        ManagedReleaseManifest manifest,
        Uri expectedMasterOrigin,
        string expectedPlatform,
        string expectedArch,
        string expectedChannel)
    {
        if (manifest is null ||
            manifest.ClientProduct != Product ||
            !EqualsNormalized(manifest.Platform, expectedPlatform) ||
            !EqualsNormalized(manifest.Arch, expectedArch) ||
            !EqualsNormalized(manifest.Channel, expectedChannel) ||
            string.IsNullOrWhiteSpace(manifest.Version) ||
            !IsSha256(manifest.Sha256))
        {
            return ManagedUpdateVerificationResult.Fail("update_manifest_invalid");
        }

        if (!Uri.TryCreate(manifest.ArtifactUrl, UriKind.Absolute, out var artifactUri) ||
            artifactUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(artifactUri.UserInfo))
        {
            return ManagedUpdateVerificationResult.Fail("update_artifact_insecure_url");
        }

        if (!SameOrigin(artifactUri, expectedMasterOrigin))
        {
            return ManagedUpdateVerificationResult.Fail("update_artifact_origin_mismatch");
        }

        if (manifest.SignatureAlgorithm != SignatureAlgorithm ||
            string.IsNullOrWhiteSpace(manifest.SignatureKeyId) ||
            string.IsNullOrWhiteSpace(manifest.Signature))
        {
            return ManagedUpdateVerificationResult.Fail("update_signature_missing");
        }

        if (!_trustStore.TryGetPublicKey(manifest.SignatureKeyId, out var publicKeyPem))
        {
            return ManagedUpdateVerificationResult.Fail("update_signature_unavailable");
        }

        try
        {
            var signature = Convert.FromBase64String(manifest.Signature);
            if (signature.Length != 64)
            {
                return ManagedUpdateVerificationResult.Fail("update_signature_invalid");
            }

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            return ecdsa.VerifyData(
                BuildSignaturePreimage(manifest),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                ? ManagedUpdateVerificationResult.Ok()
                : ManagedUpdateVerificationResult.Fail("update_signature_invalid");
        }
        catch (CryptographicException)
        {
            return ManagedUpdateVerificationResult.Fail("update_signature_invalid");
        }
        catch (FormatException)
        {
            return ManagedUpdateVerificationResult.Fail("update_signature_invalid");
        }
    }

    public async Task<ManagedUpdateVerificationResult> VerifyArtifactAsync(
        ManagedReleaseManifest manifest,
        string artifactPath,
        CancellationToken ct = default)
    {
        if (!File.Exists(artifactPath))
        {
            return ManagedUpdateVerificationResult.Fail("update_io_failed");
        }

        try
        {
            await using var stream = new FileStream(
                artifactPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = await SHA256.HashDataAsync(stream, ct);
            return CryptographicOperations.FixedTimeEquals(
                digest,
                Convert.FromHexString(manifest.Sha256))
                ? ManagedUpdateVerificationResult.Ok()
                : ManagedUpdateVerificationResult.Fail("update_hash_mismatch");
        }
        catch (FormatException)
        {
            return ManagedUpdateVerificationResult.Fail("update_manifest_invalid");
        }
    }

    public static byte[] BuildSignaturePreimage(ManagedReleaseManifest manifest)
    {
        var fields = new[]
        {
            manifest.ClientProduct,
            manifest.Platform,
            manifest.Arch,
            manifest.Channel,
            manifest.Version,
            manifest.ArtifactUrl,
            manifest.Sha256.ToLowerInvariant(),
            manifest.SignatureAlgorithm,
            manifest.SignatureKeyId,
            manifest.MinVersion ?? string.Empty,
        };

        using var stream = new MemoryStream();
        foreach (var field in fields)
        {
            var bytes = Encoding.UTF8.GetBytes(field ?? string.Empty);
            var prefix = Encoding.UTF8.GetBytes($"{bytes.Length}:");
            stream.Write(prefix);
            stream.Write(bytes);
            stream.WriteByte((byte)'\n');
        }
        return stream.ToArray();
    }

    private static bool IsSha256(string value)
        => value?.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool EqualsNormalized(string left, string right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameOrigin(Uri left, Uri right)
        => left.Scheme == Uri.UriSchemeHttps &&
           right.Scheme == Uri.UriSchemeHttps &&
           string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
           left.Port == right.Port;
}
