using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetAccel.Managed.Update;

public sealed record ManagedUpdateHandoffResult(bool Success, string? ErrorCode = null);

public sealed class ManagedUpdateHandoff
{
    public async Task<ManagedUpdateHandoffResult> LaunchAsync(
        string updaterDirectory,
        string sourceDirectory,
        string targetDirectory,
        string version,
        int parentProcessId,
        CancellationToken ct = default)
    {
        try
        {
            var updaterRoot = Path.GetFullPath(updaterDirectory);
            var updaterExe = Path.Combine(updaterRoot, "NetAccel.Updater.exe");
            if (!File.Exists(updaterExe) || !Directory.Exists(sourceDirectory) || parentProcessId <= 0)
            {
                return new(false, "update_install_failed");
            }

            var handoffRoot = Path.Combine(Path.GetTempPath(), "NetAccel", "updater", Guid.NewGuid().ToString("N"));
            var copiedUpdaterRoot = Path.Combine(handoffRoot, "bin");
            CopyDirectory(updaterRoot, copiedUpdaterRoot, ct);
            var healthMarker = Path.Combine(handoffRoot, "healthy.marker");
            var backup = Path.Combine(Path.GetFullPath(targetDirectory), ".netaccel-update-backup", version);
            var plan = new UpdateHandoffPlan
            {
                ParentProcessId = parentProcessId,
                SourceDirectory = Path.GetFullPath(sourceDirectory),
                TargetDirectory = Path.GetFullPath(targetDirectory),
                BackupDirectory = backup,
                ExecutableName = "NetAccel.exe",
                HealthMarkerPath = healthMarker,
                HealthTimeoutSeconds = 45,
            };
            Directory.CreateDirectory(handoffRoot);
            var planPath = Path.Combine(handoffRoot, "update-plan.json");
            await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan), ct);

            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(copiedUpdaterRoot, "NetAccel.Updater.exe"),
                Arguments = $"--plan \"{planPath}\"",
                WorkingDirectory = copiedUpdaterRoot,
                UseShellExecute = false,
            });
            return new(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new(false, "update_install_failed");
        }
    }

    private static void CopyDirectory(string source, string destination, CancellationToken ct)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private sealed record UpdateHandoffPlan
    {
        [JsonPropertyName("ParentProcessId")]
        public int ParentProcessId { get; init; }
        [JsonPropertyName("SourceDirectory")]
        public required string SourceDirectory { get; init; }
        [JsonPropertyName("TargetDirectory")]
        public required string TargetDirectory { get; init; }
        [JsonPropertyName("BackupDirectory")]
        public required string BackupDirectory { get; init; }
        [JsonPropertyName("ExecutableName")]
        public required string ExecutableName { get; init; }
        [JsonPropertyName("HealthMarkerPath")]
        public required string HealthMarkerPath { get; init; }
        [JsonPropertyName("HealthTimeoutSeconds")]
        public int HealthTimeoutSeconds { get; init; }
    }
}
