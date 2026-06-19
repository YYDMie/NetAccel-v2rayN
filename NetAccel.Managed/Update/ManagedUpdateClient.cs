using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace NetAccel.Managed.Update;

public sealed record ManagedStagedUpdate
{
    public required ManagedReleaseManifest Manifest { get; init; }
    public required string ArtifactPath { get; init; }
}

public sealed record ManagedUpdateStageResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public ManagedStagedUpdate? Update { get; init; }
}

public sealed class ManagedUpdateClient
{
    public const long DefaultMaximumArtifactBytes = 512L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly ManagedReleaseVerifier _verifier;
    private readonly Uri _masterOrigin;
    private readonly long _maximumArtifactBytes;

    public ManagedUpdateClient(
        HttpClient http,
        ManagedReleaseVerifier verifier,
        Uri masterOrigin,
        long maximumArtifactBytes = DefaultMaximumArtifactBytes)
    {
        _http = http;
        _verifier = verifier;
        _masterOrigin = NormalizeOrigin(masterOrigin);
        _maximumArtifactBytes = maximumArtifactBytes > 0
            ? maximumArtifactBytes
            : throw new ArgumentOutOfRangeException(nameof(maximumArtifactBytes));
    }

    public async Task<ManagedUpdateStageResult> StageLatestAsync(
        string platform,
        string arch,
        string channel,
        string stagingDirectory,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(stagingDirectory);
        ManagedReleaseManifest manifest;
        try
        {
            var request = new global::NetAccel.Managed.ManagedUpdateService()
                .BuildManifestRequest(platform, arch, channel);
            var uri = new Uri(
                _masterOrigin,
                $"/api/v1/client/releases/latest?{request.ToQueryString()}");
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                return Fail("update_not_available");
            }

            var envelope = await response.Content.ReadFromJsonAsync<ReleaseEnvelope>(cancellationToken: ct);
            manifest = envelope?.Code == 0 && envelope.Data != null
                ? envelope.Data
                : throw new InvalidDataException("Invalid release response envelope.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Fail("update_manifest_invalid");
        }

        var manifestResult = _verifier.VerifyManifest(manifest, _masterOrigin, platform, arch, channel);
        if (!manifestResult.Success)
        {
            return Fail(manifestResult.ErrorCode!);
        }

        var safeVersion = SanitizeVersion(manifest.Version);
        if (safeVersion == null)
        {
            return Fail("update_manifest_invalid");
        }

        var finalPath = Path.Combine(stagingDirectory, $"netaccel-{safeVersion}.zip");
        var tempPath = finalPath + $".{Guid.NewGuid():N}.part";
        try
        {
            using var response = await _http.GetAsync(
                manifest.ArtifactUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > _maximumArtifactBytes)
            {
                return Fail("update_artifact_too_large");
            }

            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var destination = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                long total = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, ct);
                    if (read == 0)
                    {
                        break;
                    }
                    total += read;
                    if (total > _maximumArtifactBytes)
                    {
                        return Fail("update_artifact_too_large");
                    }
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                await destination.FlushAsync(ct);
            }

            var artifactResult = await _verifier.VerifyArtifactAsync(manifest, tempPath, ct);
            if (!artifactResult.Success)
            {
                return Fail(artifactResult.ErrorCode!);
            }

            File.Move(tempPath, finalPath, overwrite: true);
            return new ManagedUpdateStageResult
            {
                Success = true,
                Update = new ManagedStagedUpdate { Manifest = manifest, ArtifactPath = finalPath },
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Fail("update_download_failed");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    private static ManagedUpdateStageResult Fail(string code)
        => new() { Success = false, ErrorCode = code };

    private static Uri NormalizeOrigin(Uri origin)
    {
        if (!origin.IsAbsoluteUri || origin.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(origin.UserInfo))
        {
            throw new ArgumentException("Master origin must be credential-free HTTPS.", nameof(origin));
        }
        return new UriBuilder(origin.Scheme, origin.IdnHost, origin.Port).Uri;
    }

    private static string? SanitizeVersion(string version)
    {
        var value = version.Trim();
        return value.Length is > 0 and <= 64 &&
               value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_')
            ? value
            : null;
    }

    private sealed record ReleaseEnvelope
    {
        [JsonPropertyName("code")]
        public int Code { get; init; }

        [JsonPropertyName("data")]
        public ManagedReleaseManifest? Data { get; init; }
    }
}
