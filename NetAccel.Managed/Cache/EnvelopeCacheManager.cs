using System.Text.Json;
using System.Text.Json.Serialization;
using NetAccel.Managed.Dto;

namespace NetAccel.Managed.Cache;

/// <summary>
/// Metadata extracted from a cached envelope for quick offline checks without decryption.
/// </summary>
public sealed record EnvelopeCacheMetadata
{
    [JsonPropertyName("assignment_revision")]
    public int AssignmentRevision { get; init; }

    [JsonPropertyName("selection_revision")]
    public int SelectionRevision { get; init; }

    [JsonPropertyName("instance_id")]
    public string InstanceId { get; init; } = string.Empty;

    [JsonPropertyName("key_id")]
    public string KeyId { get; init; } = string.Empty;

    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; init; }

    [JsonPropertyName("schema")]
    public string Schema { get; init; } = string.Empty;

    [JsonPropertyName("cached_at")]
    public DateTimeOffset CachedAt { get; init; }
}

/// <summary>
/// A cached envelope with its extracted metadata.
/// </summary>
public sealed record EnvelopeCacheEntry
{
    public required ManagedEnvelopeV1 Envelope { get; init; }
    public required EnvelopeCacheMetadata Metadata { get; init; }
}

/// <summary>
/// Manages atomic disk cache of stable managed-envelope/v1.
/// Spike-v0 must never be written to disk.
/// </summary>
public interface IEnvelopeCacheManager
{
    /// <summary>
    /// Reads the cached envelope and metadata if present and valid.
    /// Returns null if no cache exists or the cache is corrupt.
    /// </summary>
    Task<EnvelopeCacheEntry?> ReadAsync(CancellationToken ct = default);

    /// <summary>
    /// Atomically writes a v1 envelope to disk.
    /// Throws if the envelope is not a stable v1 envelope.
    /// </summary>
    Task WriteAsync(ManagedEnvelopeV1 envelope, CancellationToken ct = default);

    /// <summary>
    /// Removes any cached envelope from disk.
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);
}

public sealed class EnvelopeCacheManager : IEnvelopeCacheManager
{
    private const string V1Schema = "managed-envelope/v1";
    private const string CacheFileName = "envelope.cache.json";
    private const string TempFileName = "envelope.cache.tmp";

    private readonly string _cacheDir;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public EnvelopeCacheManager(string cacheDir, Func<DateTimeOffset>? clock = null)
    {
        _cacheDir = cacheDir;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<EnvelopeCacheEntry?> ReadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var cachePath = Path.Combine(_cacheDir, CacheFileName);
            if (!File.Exists(cachePath))
            {
                return null;
            }

            string json;
            try
            {
                json = await File.ReadAllTextAsync(cachePath, ct);
            }
            catch (IOException)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var envelope = JsonSerializer.Deserialize<ManagedEnvelopeV1>(json, _jsonOptions);
                if (envelope == null)
                {
                    return null;
                }

                var metadata = ExtractMetadata(envelope);
                if (metadata == null)
                {
                    return null;
                }

                return new EnvelopeCacheEntry { Envelope = envelope, Metadata = metadata };
            }
            catch (JsonException)
            {
                return null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task WriteAsync(ManagedEnvelopeV1 envelope, CancellationToken ct = default)
    {
        if (envelope == null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        if (!IsV1Envelope(envelope))
        {
            throw new InvalidOperationException("Only managed-envelope/v1 may be cached. Spike-v0 must never be written to disk.");
        }

        await _lock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_cacheDir);

            var tempPath = Path.Combine(_cacheDir, TempFileName);
            var cachePath = Path.Combine(_cacheDir, CacheFileName);

            var json = JsonSerializer.Serialize(envelope, _jsonOptions);

            // Atomic write: write temp, flush, replace
            await File.WriteAllTextAsync(tempPath, json, ct);

            // Ensure data is flushed to disk before rename
            if (OperatingSystem.IsWindows())
            {
                using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                stream.Flush(true);
            }

            File.Move(tempPath, cachePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var cachePath = Path.Combine(_cacheDir, CacheFileName);
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
            }

            var tempPath = Path.Combine(_cacheDir, TempFileName);
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private EnvelopeCacheMetadata? ExtractMetadata(ManagedEnvelopeV1 envelope)
    {
        if (!IsV1Envelope(envelope))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(envelope.ExpiresAt, out var expiresAt))
        {
            return null;
        }

        if (envelope.AssignmentRevision < 0 || envelope.SelectionRevision < 0)
        {
            return null;
        }

        return new EnvelopeCacheMetadata
        {
            AssignmentRevision = envelope.AssignmentRevision,
            SelectionRevision = envelope.SelectionRevision,
            InstanceId = envelope.InstanceId,
            KeyId = envelope.KeyId,
            ExpiresAt = expiresAt,
            Schema = envelope.Schema,
            CachedAt = _clock(),
        };
    }

    public static bool IsV1Envelope(ManagedEnvelopeV1 envelope)
    {
        return envelope != null
            && string.Equals(envelope.Schema, V1Schema, StringComparison.Ordinal);
    }
}
