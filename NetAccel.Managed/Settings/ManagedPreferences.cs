using System.Text.Json;

namespace NetAccel.Managed.Settings;

public sealed record ManagedPreferences
{
    public bool AutoConnect { get; init; }
    public bool MinimizeToTray { get; init; } = true;
    public bool NotificationsEnabled { get; init; } = true;
    public string PreferredNetworkMode { get; init; } = "system_proxy";
    public string Theme { get; init; } = "system";
}

public interface IManagedPreferencesStore
{
    Task<ManagedPreferences> ReadAsync(CancellationToken ct = default);
    Task WriteAsync(ManagedPreferences preferences, CancellationToken ct = default);
}

public sealed class ManagedPreferencesStore : IManagedPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ManagedPreferencesStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetAccel",
            "managed-preferences.json");
    }

    public async Task<ManagedPreferences> ReadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(_path))
            {
                return new ManagedPreferences();
            }

            try
            {
                await using var stream = File.OpenRead(_path);
                return await JsonSerializer.DeserializeAsync<ManagedPreferences>(stream, JsonOptions, ct)
                    ?? new ManagedPreferences();
            }
            catch (JsonException)
            {
                return new ManagedPreferences();
            }
            catch (IOException)
            {
                return new ManagedPreferences();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(ManagedPreferences preferences, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        await _gate.WaitAsync(ct);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(
                                 tempPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 4096,
                                 FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(stream, preferences, JsonOptions, ct);
                    await stream.FlushAsync(ct);
                }

                File.Move(tempPath, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
