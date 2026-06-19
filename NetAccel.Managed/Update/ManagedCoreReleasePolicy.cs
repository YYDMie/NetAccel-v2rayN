using System.Security.Cryptography;
using System.Text;

namespace NetAccel.Managed.Update;

public sealed record ManagedCoreReleaseManifest
{
    public required string Core { get; init; }
    public required string Version { get; init; }
    public required string ArtifactUrl { get; init; }
    public required string Sha256 { get; init; }
    public required string SignatureAlgorithm { get; init; }
    public required string SignatureKeyId { get; init; }
    public required string Signature { get; init; }
}

public sealed class ManagedCoreReleasePolicy
{
    private readonly IReadOnlyDictionary<string, string> _pinnedVersions;
    private readonly IManagedReleaseTrustStore _trustStore;

    public ManagedCoreReleasePolicy(
        IReadOnlyDictionary<string, string> pinnedVersions,
        IManagedReleaseTrustStore trustStore)
    {
        _pinnedVersions = pinnedVersions;
        _trustStore = trustStore;
    }

    public ManagedUpdateVerificationResult Verify(ManagedCoreReleaseManifest manifest, Uri expectedOrigin)
    {
        if (!_pinnedVersions.TryGetValue(manifest.Core, out var version) ||
            !string.Equals(version, manifest.Version, StringComparison.Ordinal) ||
            manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
        {
            return ManagedUpdateVerificationResult.Fail("update_manifest_invalid");
        }
        if (!Uri.TryCreate(manifest.ArtifactUrl, UriKind.Absolute, out var artifactUri) ||
            artifactUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(artifactUri.IdnHost, expectedOrigin.IdnHost, StringComparison.OrdinalIgnoreCase) ||
            artifactUri.Port != expectedOrigin.Port)
        {
            return ManagedUpdateVerificationResult.Fail("update_artifact_origin_mismatch");
        }
        if (manifest.SignatureAlgorithm != ManagedReleaseVerifier.SignatureAlgorithm ||
            !_trustStore.TryGetPublicKey(manifest.SignatureKeyId, out var publicKey))
        {
            return ManagedUpdateVerificationResult.Fail("update_signature_unavailable");
        }

        try
        {
            var signature = Convert.FromBase64String(manifest.Signature);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKey);
            return signature.Length == 64 && ecdsa.VerifyData(
                BuildPreimage(manifest),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                ? ManagedUpdateVerificationResult.Ok()
                : ManagedUpdateVerificationResult.Fail("update_signature_invalid");
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return ManagedUpdateVerificationResult.Fail("update_signature_invalid");
        }
    }

    public async Task<ManagedUpdateVerificationResult> VerifyArtifactAsync(
        ManagedCoreReleaseManifest manifest,
        string artifactPath,
        CancellationToken ct = default)
    {
        if (!File.Exists(artifactPath))
        {
            return ManagedUpdateVerificationResult.Fail("update_io_failed");
        }
        try
        {
            await using var stream = File.OpenRead(artifactPath);
            var actual = await SHA256.HashDataAsync(stream, ct);
            var expected = Convert.FromHexString(manifest.Sha256);
            return CryptographicOperations.FixedTimeEquals(actual, expected)
                ? ManagedUpdateVerificationResult.Ok()
                : ManagedUpdateVerificationResult.Fail("update_hash_mismatch");
        }
        catch (FormatException)
        {
            return ManagedUpdateVerificationResult.Fail("update_manifest_invalid");
        }
    }

    public static byte[] BuildPreimage(ManagedCoreReleaseManifest manifest)
    {
        var fields = new[]
        {
            manifest.Core,
            manifest.Version,
            manifest.ArtifactUrl,
            manifest.Sha256.ToLowerInvariant(),
            manifest.SignatureAlgorithm,
            manifest.SignatureKeyId,
        };
        using var stream = new MemoryStream();
        foreach (var field in fields)
        {
            var value = Encoding.UTF8.GetBytes(field);
            var prefix = Encoding.UTF8.GetBytes($"{value.Length}:");
            stream.Write(prefix);
            stream.Write(value);
            stream.WriteByte((byte)'\n');
        }
        return stream.ToArray();
    }
}
