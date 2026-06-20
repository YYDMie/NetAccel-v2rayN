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
Process? updatedProcess = null;
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
    updatedProcess = Process.Start(new ProcessStartInfo
    {
        FileName = executable,
        Arguments = $"--netaccel-update-health-marker \"{plan.HealthMarkerPath}\"",
        WorkingDirectory = plan.TargetDirectory,
        UseShellExecute = false,
    }) ?? throw new InvalidOperationException("Updated client did not start.");

    var deadline = DateTimeOffset.UtcNow.AddSeconds(plan.HealthTimeoutSeconds);
    while (DateTimeOffset.UtcNow < deadline && !updatedProcess.HasExited)
    {
        if (File.Exists(plan.HealthMarkerPath))
        {
            updatedProcess.Dispose();
            return 0;
        }
        await Task.Delay(500);
    }
    throw new InvalidOperationException("Updated client did not report healthy state.");
}
catch
{
    await StopUpdatedProcessAsync(updatedProcess);
    var rollbackSucceeded = true;
    foreach (var file in changed.AsEnumerable().Reverse())
    {
        var target = SafeChild(plan.TargetDirectory, file.RelativePath);
        var backup = SafeChild(plan.BackupDirectory, file.RelativePath);
        if (file.Existed && File.Exists(backup))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            rollbackSucceeded &= await CopyWithRetryAsync(backup, target);
        }
        else if (!file.Existed && File.Exists(target))
        {
            rollbackSucceeded &= await DeleteWithRetryAsync(target);
        }
    }

    var original = SafeChild(plan.TargetDirectory, plan.ExecutableName);
    if (rollbackSucceeded && File.Exists(original))
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = original,
                WorkingDirectory = plan.TargetDirectory,
                UseShellExecute = false,
            });
        }
        catch
        {
        }
    }
    return 1;
}
finally
{
    updatedProcess?.Dispose();
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

static async Task StopUpdatedProcessAsync(Process? process)
{
    if (process == null)
    {
        return;
    }

    var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
    while (DateTimeOffset.UtcNow < deadline)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
        await Task.Delay(100);
    }
}

static async Task<bool> CopyWithRetryAsync(string source, string target)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
    while (DateTimeOffset.UtcNow < deadline)
    {
        try
        {
            File.Copy(source, target, overwrite: true);
            return true;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        await Task.Delay(100);
    }
    return false;
}

static async Task<bool> DeleteWithRetryAsync(string path)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
    while (DateTimeOffset.UtcNow < deadline)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        await Task.Delay(100);
    }
    return false;
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
