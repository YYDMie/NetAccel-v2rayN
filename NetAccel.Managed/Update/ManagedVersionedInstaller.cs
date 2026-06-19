using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetAccel.Managed.Update;

public sealed record ManagedUpdateInstallState
{
    [JsonPropertyName("current_version")]
    public required string CurrentVersion { get; init; }

    [JsonPropertyName("previous_version")]
    public string? PreviousVersion { get; init; }

    [JsonPropertyName("pending_version")]
    public string? PendingVersion { get; init; }

    [JsonPropertyName("health_deadline")]
    public DateTimeOffset? HealthDeadline { get; init; }
}

public sealed record ManagedUpdateInstallResult(bool Success, string? ErrorCode = null, string? LaunchDirectory = null);

public sealed class ManagedVersionedInstaller
{
    public const long DefaultMaximumExpandedBytes = 1024L * 1024 * 1024;
    public const int DefaultMaximumEntries = 10_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _root;
    private readonly string _versionsRoot;
    private readonly string _statePath;
    private readonly long _maximumExpandedBytes;
    private readonly int _maximumEntries;

    public ManagedVersionedInstaller(
        string installRoot,
        long maximumExpandedBytes = DefaultMaximumExpandedBytes,
        int maximumEntries = DefaultMaximumEntries)
    {
        _root = Path.GetFullPath(installRoot);
        _versionsRoot = Path.Combine(_root, "versions");
        _statePath = Path.Combine(_root, "update-state.json");
        _maximumExpandedBytes = maximumExpandedBytes;
        _maximumEntries = maximumEntries;
    }

    public async Task<ManagedUpdateInstallResult> PrepareAsync(
        ManagedStagedUpdate staged,
        string currentVersion,
        DateTimeOffset now,
        TimeSpan healthTimeout,
        CancellationToken ct = default)
    {
        if (staged == null || !File.Exists(staged.ArtifactPath) ||
            string.IsNullOrWhiteSpace(currentVersion) || healthTimeout <= TimeSpan.Zero)
        {
            return new(false, "update_install_failed");
        }

        Directory.CreateDirectory(_versionsRoot);
        var version = staged.Manifest.Version;
        if (!IsSafeVersion(version))
        {
            return new(false, "update_manifest_invalid");
        }

        var versionDirectory = Path.Combine(_versionsRoot, version);
        var tempDirectory = versionDirectory + $".{Guid.NewGuid():N}.tmp";
        try
        {
            if (!Directory.Exists(versionDirectory))
            {
                Directory.CreateDirectory(tempDirectory);
                ExtractSafely(staged.ArtifactPath, tempDirectory, ct);
                Directory.Move(tempDirectory, versionDirectory);
            }

            var previousState = await ReadStateAsync(ct);
            var state = new ManagedUpdateInstallState
            {
                CurrentVersion = previousState?.CurrentVersion ?? currentVersion,
                PreviousVersion = previousState?.CurrentVersion ?? currentVersion,
                PendingVersion = version,
                HealthDeadline = now.Add(healthTimeout),
            };
            await WriteStateAsync(state, ct);
            return new(true, LaunchDirectory: versionDirectory);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new(false, "update_install_failed");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                try { Directory.Delete(tempDirectory, recursive: true); } catch { }
            }
        }
    }

    public async Task<ManagedUpdateInstallState?> ConfirmHealthyAsync(
        string runningVersion,
        CancellationToken ct = default)
    {
        var state = await ReadStateAsync(ct);
        if (state?.PendingVersion == null || state.PendingVersion != runningVersion)
        {
            return state;
        }

        var confirmed = state with
        {
            CurrentVersion = runningVersion,
            PendingVersion = null,
            HealthDeadline = null,
        };
        await WriteStateAsync(confirmed, ct);
        return confirmed;
    }

    public async Task<ManagedUpdateInstallState?> RecoverIfUnhealthyAsync(
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var state = await ReadStateAsync(ct);
        if (state?.PendingVersion == null || state.HealthDeadline == null || now < state.HealthDeadline)
        {
            return state;
        }

        var rollbackVersion = state.PreviousVersion ?? state.CurrentVersion;
        var rolledBack = state with
        {
            CurrentVersion = rollbackVersion,
            PendingVersion = null,
            HealthDeadline = null,
        };
        await WriteStateAsync(rolledBack, ct);
        return rolledBack;
    }

    public async Task<string?> GetLaunchDirectoryAsync(CancellationToken ct = default)
    {
        var state = await ReadStateAsync(ct);
        var version = state?.PendingVersion ?? state?.CurrentVersion;
        if (version == null || !IsSafeVersion(version))
        {
            return null;
        }
        var path = Path.Combine(_versionsRoot, version);
        return Directory.Exists(path) ? path : null;
    }

    public async Task<ManagedUpdateInstallState?> ReadStateAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }
        await using var stream = new FileStream(_statePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<ManagedUpdateInstallState>(stream, JsonOptions, ct);
    }

    private void ExtractSafely(string archivePath, string destinationRoot, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > _maximumEntries)
        {
            throw new InvalidDataException("Archive contains too many entries.");
        }

        long expandedBytes = 0;
        var rootPrefix = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (IsSymbolicLink(entry))
            {
                throw new InvalidDataException("Symbolic links are not allowed in update archives.");
            }
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > _maximumExpandedBytes)
            {
                throw new InvalidDataException("Expanded update exceeds the configured limit.");
            }

            var destination = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Archive entry escapes the version directory.");
            }
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: false);
        }
    }

    private async Task WriteStateAsync(ManagedUpdateInstallState state, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        var temp = _statePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, ct);
                await stream.FlushAsync(ct);
            }
            File.Move(temp, _statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    private static bool IsSafeVersion(string version)
        => !string.IsNullOrWhiteSpace(version) && version.Length <= 64 &&
           version.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_');

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
        => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;
}
