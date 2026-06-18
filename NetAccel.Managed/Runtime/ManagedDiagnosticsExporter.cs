using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetAccel.Managed.Runtime;

public sealed record ManagedDiagnosticsExportData
{
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string ClientVersion { get; init; }
    public required ManagedDiagnosticsSnapshot Snapshot { get; init; }
    public required ManagedConnectionStatus Connection { get; init; }
    public required ManagedCoreRuntimeSnapshot Runtime { get; init; }
    public required ConnectionOwner Owner { get; init; }
}

public sealed record ManagedDiagnosticsExportResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public string? FileName { get; init; }
}

public interface IManagedDiagnosticsExporter
{
    Task<ManagedDiagnosticsExportResult> ExportAsync(
        string destinationPath,
        ManagedDiagnosticsExportData data,
        CancellationToken ct = default);
}

public sealed class ManagedDiagnosticsExporter : IManagedDiagnosticsExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<ManagedDiagnosticsExportResult> ExportAsync(
        string destinationPath,
        ManagedDiagnosticsExportData data,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("A diagnostic ZIP destination is required.", nameof(destinationPath));
        }

        var fullPath = Path.GetFullPath(destinationPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Diagnostic packages must use the .zip extension.", nameof(destinationPath));
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The diagnostic package destination directory does not exist.");
        }

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteJsonAsync(archive, BuildDocument(data), ct);
                await WritePrivacyNoticeAsync(archive, ct);
            }

            File.Move(tempPath, fullPath, overwrite: true);
            return new ManagedDiagnosticsExportResult
            {
                Success = true,
                Message = "脱敏诊断包已导出。",
                FileName = Path.GetFileName(fullPath),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new ManagedDiagnosticsExportResult
            {
                Success = false,
                Message = "诊断包导出失败，未写入账号或线路数据。",
            };
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // The export result remains authoritative; a locked temp file is never added to the ZIP.
                }
            }
        }
    }

    private static DiagnosticsDocument BuildDocument(ManagedDiagnosticsExportData data)
        => new()
        {
            Schema = "netaccel-diagnostics/v1",
            GeneratedAt = data.GeneratedAt,
            Client = new DiagnosticsClient
            {
                Product = "NetAccel",
                Version = data.ClientVersion,
                OperatingSystem = Environment.OSVersion.Platform.ToString(),
                Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            },
            Checks = data.Snapshot.Checks.Select(check => new DiagnosticsCheck
            {
                Name = check.Title,
                Status = check.Status,
                Condition = check.Condition.ToString(),
                NeedsRepair = check.NeedsRepair,
            }).ToArray(),
            Connection = new DiagnosticsConnection
            {
                State = data.Connection.State.ToString(),
                NetworkMode = data.Connection.NetworkMode?.ToString(),
                SelectionMode = data.Connection.SelectionMode?.ToString(),
                IsFallback = data.Connection.IsFallback,
                FailureKind = data.Connection.FailureKind.ToString(),
            },
            Runtime = new DiagnosticsRuntime
            {
                CoreRunning = data.Runtime.IsRunning,
                ManagedRuntimePresent = data.Runtime.HasManagedRuntime,
                ManagedSystemProxyPresent = data.Runtime.HasManagedSystemProxy,
                ManagedTunPresent = data.Runtime.HasManagedTun,
                ConnectionOwner = data.Owner.ToString(),
            },
        };

    private static async Task WriteJsonAsync(
        ZipArchive archive,
        DiagnosticsDocument document,
        CancellationToken ct)
    {
        var entry = archive.CreateEntry("diagnostics.json", CompressionLevel.SmallestSize);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, ct);
    }

    private static async Task WritePrivacyNoticeAsync(ZipArchive archive, CancellationToken ct)
    {
        var entry = archive.CreateEntry("privacy.txt", CompressionLevel.SmallestSize);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        var notice =
            "This package is generated from an allowlist.\n" +
            "It does not include logs, tokens, credentials, endpoints, profile configuration, " +
            "classic nodes, subscriptions, SQLite databases, or managed cache files.\n";
        await writer.WriteAsync(notice.AsMemory(), ct);
    }

    private sealed record DiagnosticsDocument
    {
        public required string Schema { get; init; }
        public required DateTimeOffset GeneratedAt { get; init; }
        public required DiagnosticsClient Client { get; init; }
        public required IReadOnlyList<DiagnosticsCheck> Checks { get; init; }
        public required DiagnosticsConnection Connection { get; init; }
        public required DiagnosticsRuntime Runtime { get; init; }
    }

    private sealed record DiagnosticsClient
    {
        public required string Product { get; init; }
        public required string Version { get; init; }
        public required string OperatingSystem { get; init; }
        public required string Architecture { get; init; }
    }

    private sealed record DiagnosticsCheck
    {
        public required string Name { get; init; }
        public required string Status { get; init; }
        public required string Condition { get; init; }
        public bool NeedsRepair { get; init; }
    }

    private sealed record DiagnosticsConnection
    {
        public required string State { get; init; }
        public string? NetworkMode { get; init; }
        public string? SelectionMode { get; init; }
        public bool IsFallback { get; init; }
        public required string FailureKind { get; init; }
    }

    private sealed record DiagnosticsRuntime
    {
        public bool CoreRunning { get; init; }
        public bool ManagedRuntimePresent { get; init; }
        public bool ManagedSystemProxyPresent { get; init; }
        public bool ManagedTunPresent { get; init; }
        public required string ConnectionOwner { get; init; }
    }
}
