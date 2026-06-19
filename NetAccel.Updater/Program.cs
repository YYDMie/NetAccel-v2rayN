using System.Diagnostics;
using System.Text.Json;

if (args.Length != 2 || args[0] != "--plan")
{
    return 2;
}

var planPath = Path.GetFullPath(args[1]);
var plan = JsonSerializer.Deserialize<UpdatePlan>(await File.ReadAllTextAsync(planPath))
    ?? throw new InvalidDataException("Update plan is empty.");
plan.Validate();

await WaitForParentAsync(plan.ParentProcessId, TimeSpan.FromMinutes(2));
Directory.CreateDirectory(plan.BackupDirectory);
var changed = new List<ChangedFile>();
try
{
    foreach (var source in Directory.EnumerateFiles(plan.SourceDirectory, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(plan.SourceDirectory, source);
        var target = SafeChild(plan.TargetDirectory, relative);
        var backup = SafeChild(plan.BackupDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(target, backup, overwrite: true);
            changed.Add(new ChangedFile(relative, Existed: true));
        }
        else
        {
            changed.Add(new ChangedFile(relative, Existed: false));
        }
        File.Copy(source, target, overwrite: true);
    }

    if (File.Exists(plan.HealthMarkerPath))
    {
        File.Delete(plan.HealthMarkerPath);
    }
    var executable = SafeChild(plan.TargetDirectory, plan.ExecutableName);
    using var process = Process.Start(new ProcessStartInfo
    {
        FileName = executable,
        Arguments = $"--netaccel-update-health-marker \"{plan.HealthMarkerPath}\"",
        WorkingDirectory = plan.TargetDirectory,
        UseShellExecute = false,
    }) ?? throw new InvalidOperationException("Updated client did not start.");

    var deadline = DateTimeOffset.UtcNow.AddSeconds(plan.HealthTimeoutSeconds);
    while (DateTimeOffset.UtcNow < deadline && !process.HasExited)
    {
        if (File.Exists(plan.HealthMarkerPath))
        {
            return 0;
        }
        await Task.Delay(500);
    }
    throw new InvalidOperationException("Updated client did not report healthy state.");
}
catch
{
    foreach (var file in changed.AsEnumerable().Reverse())
    {
        var target = SafeChild(plan.TargetDirectory, file.RelativePath);
        var backup = SafeChild(plan.BackupDirectory, file.RelativePath);
        if (file.Existed && File.Exists(backup))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(backup, target, overwrite: true);
        }
        else if (!file.Existed && File.Exists(target))
        {
            File.Delete(target);
        }
    }

    var original = SafeChild(plan.TargetDirectory, plan.ExecutableName);
    if (File.Exists(original))
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = original,
            WorkingDirectory = plan.TargetDirectory,
            UseShellExecute = false,
        });
    }
    return 1;
}

static async Task WaitForParentAsync(int pid, TimeSpan timeout)
{
    try
    {
        using var process = Process.GetProcessById(pid);
        using var cts = new CancellationTokenSource(timeout);
        await process.WaitForExitAsync(cts.Token);
    }
    catch (ArgumentException) { }
    catch (OperationCanceledException)
    {
        throw new TimeoutException("Parent process did not exit.");
    }
}

static string SafeChild(string root, string relative)
{
    var prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
    var path = Path.GetFullPath(Path.Combine(root, relative));
    if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("Update path escapes its root.");
    }
    return path;
}

sealed record ChangedFile(string RelativePath, bool Existed);

sealed record UpdatePlan
{
    public required int ParentProcessId { get; init; }
    public required string SourceDirectory { get; set; }
    public required string TargetDirectory { get; set; }
    public required string BackupDirectory { get; set; }
    public required string ExecutableName { get; init; }
    public required string HealthMarkerPath { get; set; }
    public int HealthTimeoutSeconds { get; init; } = 30;

    public void Validate()
    {
        SourceDirectory = Path.GetFullPath(SourceDirectory);
        TargetDirectory = Path.GetFullPath(TargetDirectory);
        BackupDirectory = Path.GetFullPath(BackupDirectory);
        HealthMarkerPath = Path.GetFullPath(HealthMarkerPath);
        if (ParentProcessId <= 0 || !Directory.Exists(SourceDirectory) ||
            string.IsNullOrWhiteSpace(TargetDirectory) || SourceDirectory == TargetDirectory ||
            Path.GetFileName(ExecutableName) != ExecutableName ||
            HealthTimeoutSeconds is < 5 or > 300)
        {
            throw new InvalidDataException("Update plan is invalid.");
        }
    }
}
