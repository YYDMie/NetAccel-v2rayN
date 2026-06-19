using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetAccel.Managed.Update;
using Xunit;

namespace NetAccel.Managed.Tests;

public sealed class ManagedUpdateSecurityTests
{
    [Fact]
    public void SignedReleaseManifest_AcceptsValidAndRejectsTampering()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(signingKey);
        var verifier = Verifier(signingKey);

        Assert.True(verifier.VerifyManifest(
            manifest, new Uri("https://master.example"), "windows", "x86_64", "stable").Success);

        var tampered = manifest with { Version = "9.9.9" };
        var result = verifier.VerifyManifest(
            tampered, new Uri("https://master.example"), "windows", "x86_64", "stable");
        Assert.False(result.Success);
        Assert.Equal("update_signature_invalid", result.ErrorCode);
    }

    [Fact]
    public void SignedReleaseManifest_RejectsWrongOriginAndUnknownKey()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(signingKey);
        var wrongOrigin = Verifier(signingKey).VerifyManifest(
            manifest, new Uri("https://other.example"), "windows", "x86_64", "stable");
        Assert.Equal("update_artifact_origin_mismatch", wrongOrigin.ErrorCode);

        var unknownKey = new ManagedReleaseVerifier(new ManagedReleaseTrustStore(new Dictionary<string, string>()))
            .VerifyManifest(manifest, new Uri("https://master.example"), "windows", "x86_64", "stable");
        Assert.Equal("update_signature_unavailable", unknownKey.ErrorCode);
    }

    [Fact]
    public void DirectoryTrustStore_RejectsKeyPathTraversal()
    {
        using var temp = new TempDirectory();
        var store = new DirectoryManagedReleaseTrustStore(temp.Path);
        Assert.False(store.TryGetPublicKey("../outside", out _));
        Assert.False(store.TryGetPublicKey("missing", out _));
    }

    [Fact]
    public async Task ArtifactVerification_UsesExactSha256()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var temp = new TempDirectory();
        var bytes = Encoding.UTF8.GetBytes("verified update artifact");
        var path = Path.Combine(temp.Path, "artifact.zip");
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        var manifest = SignedManifest(signingKey, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

        Assert.True((await Verifier(signingKey).VerifyArtifactAsync(
            manifest, path, TestContext.Current.CancellationToken)).Success);

        await File.WriteAllTextAsync(path, "tampered", TestContext.Current.CancellationToken);
        var result = await Verifier(signingKey).VerifyArtifactAsync(
            manifest, path, TestContext.Current.CancellationToken);
        Assert.Equal("update_hash_mismatch", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateClient_DownloadsOnlySignedMatchingArtifact()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var temp = new TempDirectory();
        var artifact = CreateZip(("NetAccel.exe", "binary"));
        var sha = Convert.ToHexString(SHA256.HashData(artifact)).ToLowerInvariant();
        var manifest = SignedManifest(signingKey, sha);
        var handler = new UpdateHttpHandler(manifest, artifact);
        var client = new ManagedUpdateClient(
            new HttpClient(handler),
            Verifier(signingKey),
            new Uri("https://master.example"),
            maximumArtifactBytes: 1024 * 1024);

        var result = await client.StageLatestAsync(
            "windows", "x86_64", "stable", temp.Path, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(result.Update);
        Assert.True(File.Exists(result.Update.ArtifactPath));
        Assert.DoesNotContain(Directory.EnumerateFiles(temp.Path), path => path.EndsWith(".part"));
    }

    [Fact]
    public async Task UpdateClient_RejectsOversizedArtifactAndCleansPartialFile()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var temp = new TempDirectory();
        var artifact = new byte[4096];
        var manifest = SignedManifest(
            signingKey,
            Convert.ToHexString(SHA256.HashData(artifact)).ToLowerInvariant());
        var client = new ManagedUpdateClient(
            new HttpClient(new UpdateHttpHandler(manifest, artifact)),
            Verifier(signingKey),
            new Uri("https://master.example"),
            maximumArtifactBytes: 1024);

        var result = await client.StageLatestAsync(
            "windows", "x86_64", "stable", temp.Path, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("update_artifact_too_large", result.ErrorCode);
        Assert.Empty(Directory.EnumerateFiles(temp.Path));
    }

    [Fact]
    public async Task VersionedInstaller_ConfirmsHealthyAndRollsBackExpiredPendingVersion()
    {
        using var temp = new TempDirectory();
        var installer = new ManagedVersionedInstaller(temp.Path);
        var first = StagedZip(temp.Path, "2.0.0", CreateZip(("NetAccel.exe", "v2")));
        var now = DateTimeOffset.Parse("2026-06-19T10:00:00Z");

        var prepared = await installer.PrepareAsync(
            first, "1.0.0", now, TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
        Assert.True(prepared.Success);
        Assert.EndsWith(Path.Combine("versions", "2.0.0"), prepared.LaunchDirectory);
        Assert.EndsWith(Path.Combine("versions", "2.0.0"), await installer.GetLaunchDirectoryAsync());

        var confirmed = await installer.ConfirmHealthyAsync("2.0.0");
        Assert.Equal("2.0.0", confirmed?.CurrentVersion);
        Assert.Null(confirmed?.PendingVersion);

        var second = StagedZip(temp.Path, "3.0.0", CreateZip(("NetAccel.exe", "v3")));
        Assert.True((await installer.PrepareAsync(
            second, "2.0.0", now, TimeSpan.FromMinutes(2))).Success);
        var rolledBack = await installer.RecoverIfUnhealthyAsync(now.AddMinutes(3));
        Assert.Equal("2.0.0", rolledBack?.CurrentVersion);
        Assert.Null(rolledBack?.PendingVersion);
    }

    [Fact]
    public async Task VersionedInstaller_RejectsZipTraversal()
    {
        using var temp = new TempDirectory();
        var staged = StagedZip(temp.Path, "2.0.0", CreateZip(("../escape.txt", "bad")));
        var result = await new ManagedVersionedInstaller(temp.Path).PrepareAsync(
            staged,
            "1.0.0",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.False(File.Exists(Path.Combine(temp.Path, "escape.txt")));
    }

    [Fact]
    public void CorePolicy_RequiresPinnedVersionAndValidSignature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = new ManagedCoreReleaseManifest
        {
            Core = "xray",
            Version = "25.6.8",
            ArtifactUrl = "https://master.example/cores/xray.zip",
            Sha256 = new string('a', 64),
            SignatureAlgorithm = ManagedReleaseVerifier.SignatureAlgorithm,
            SignatureKeyId = "test-key",
            Signature = string.Empty,
        };
        manifest = manifest with
        {
            Signature = Convert.ToBase64String(key.SignData(
                ManagedCoreReleasePolicy.BuildPreimage(manifest),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
        };
        var policy = new ManagedCoreReleasePolicy(
            new Dictionary<string, string> { ["xray"] = "25.6.8" },
            new ManagedReleaseTrustStore(new Dictionary<string, string> { ["test-key"] = key.ExportSubjectPublicKeyInfoPem() }));

        Assert.True(policy.Verify(manifest, new Uri("https://master.example")).Success);
        Assert.False(policy.Verify(manifest with { Version = "25.6.9" }, new Uri("https://master.example")).Success);
    }

    private static ManagedReleaseVerifier Verifier(ECDsa key)
        => new(new ManagedReleaseTrustStore(new Dictionary<string, string>
        {
            ["test-key"] = key.ExportSubjectPublicKeyInfoPem(),
        }));

    private static ManagedReleaseManifest SignedManifest(ECDsa key, string? sha = null)
    {
        var manifest = new ManagedReleaseManifest
        {
            ClientProduct = ManagedReleaseVerifier.Product,
            Platform = "windows",
            Arch = "x86_64",
            Channel = "stable",
            Version = "2.0.0",
            ArtifactUrl = "https://master.example/releases/netaccel-2.0.0.zip",
            Sha256 = sha ?? new string('a', 64),
            SignatureAlgorithm = ManagedReleaseVerifier.SignatureAlgorithm,
            SignatureKeyId = "test-key",
            Signature = string.Empty,
            MinVersion = "1.0.0",
        };
        return manifest with
        {
            Signature = Convert.ToBase64String(key.SignData(
                ManagedReleaseVerifier.BuildSignaturePreimage(manifest),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
        };
    }

    private static ManagedStagedUpdate StagedZip(string root, string version, byte[] bytes)
    {
        var path = Path.Combine(root, $"{version}.zip");
        File.WriteAllBytes(path, bytes);
        return new ManagedStagedUpdate
        {
            ArtifactPath = path,
            Manifest = SignedManifest(ECDsa.Create(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()) with
            {
                Version = version,
            },
        };
    }

    private static byte[] CreateZip(params (string Name, string Content)[] files)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = zip.CreateEntry(file.Name);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(file.Content);
            }
        }
        return buffer.ToArray();
    }

    private sealed class UpdateHttpHandler(ManagedReleaseManifest manifest, byte[] artifact) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal))
            {
                var json = JsonSerializer.Serialize(new { code = 0, data = manifest });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(artifact),
            });
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"netaccel-update-test-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
