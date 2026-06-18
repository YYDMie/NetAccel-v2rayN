using NetAccel.Managed.Dto;
using NetAccel.Managed.Presentation;
using NetAccel.Managed.Runtime;
using NetAccel.Managed.Settings;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedSettingsAndTrayTests
{
    [Fact]
    public async Task PreferencesStore_RoundTripsOnlyManagedPreferences()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "managed-preferences.json");
        var store = new ManagedPreferencesStore(path);
        var expected = new ManagedPreferences
        {
            AutoConnect = true,
            MinimizeToTray = false,
            NotificationsEnabled = false,
            PreferredNetworkMode = "tun",
        };

        await store.WriteAsync(expected, TestContext.Current.CancellationToken);
        var actual = await store.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
        var json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profile", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("endpoint", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreferencesStore_CorruptFileReturnsSafeDefaults()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "managed-preferences.json");
        await File.WriteAllTextAsync(path, "{broken", TestContext.Current.CancellationToken);
        var store = new ManagedPreferencesStore(path);

        var preferences = await store.ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(preferences.AutoConnect);
        Assert.True(preferences.MinimizeToTray);
        Assert.True(preferences.NotificationsEnabled);
        Assert.Equal("system_proxy", preferences.PreferredNetworkMode);
    }

    [Fact]
    public async Task SettingsViewModel_SavePersistsPreferencesAndUpdatesAutoRun()
    {
        var store = new InMemoryPreferencesStore();
        var autoRunUpdates = new List<bool>();
        var viewModel = CreateSettingsViewModel(
            store,
            autoRunProvider: () => false,
            autoRunUpdater: (enabled, _) =>
            {
                autoRunUpdates.Add(enabled);
                return Task.FromResult(true);
            });
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.AutoRun = true;
        viewModel.AutoConnect = true;
        viewModel.MinimizeToTray = false;
        viewModel.NotificationsEnabled = false;
        viewModel.UseTun = true;

        await viewModel.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal([true], autoRunUpdates);
        Assert.Equal(
            new ManagedPreferences
            {
                AutoConnect = true,
                MinimizeToTray = false,
                NotificationsEnabled = false,
                PreferredNetworkMode = "tun",
            },
            store.Value);
        Assert.Equal("设置已保存。", viewModel.Summary);
    }

    [Fact]
    public async Task SettingsViewModel_LogoutAndClassicHandoffUseBoundedActions()
    {
        var logoutCalls = 0;
        var handoffCalls = 0;
        var viewModel = CreateSettingsViewModel(
            new InMemoryPreferencesStore(),
            logout: _ =>
            {
                logoutCalls++;
                return Task.CompletedTask;
            },
            classicHandoff: _ =>
            {
                handoffCalls++;
                return Task.FromResult(ClassicModeHandoffResult.Succeeded());
            });

        await viewModel.LogoutAsync(TestContext.Current.CancellationToken);
        var result = await viewModel.PrepareClassicModeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, logoutCalls);
        Assert.Equal(1, handoffCalls);
        Assert.True(result.Success);
    }

    [Fact]
    public void TrayViewModel_TracksCoordinatorAndClassicOverride()
    {
        var coordinator = new FakeCoordinator();
        using var tray = new ManagedTrayViewModel(coordinator);

        coordinator.SetStatus(new ManagedConnectionStatus { State = ManagedConnectionState.Starting });
        Assert.Equal(ManagedTrayMode.Starting, tray.Mode);
        Assert.Equal("NetAccel · 正在连接", tray.Header);

        coordinator.SetStatus(new ManagedConnectionStatus { State = ManagedConnectionState.Connected });
        tray.SetRouteName("香港智能通道");
        Assert.Equal(ManagedTrayMode.Connected, tray.Mode);
        Assert.Equal("停止加速", tray.PrimaryActionText);
        Assert.Equal("当前线路：香港智能通道", tray.RouteText);

        tray.SetClassicMode(true);
        coordinator.SetStatus(new ManagedConnectionStatus
        {
            State = ManagedConnectionState.Faulted,
            FailureKind = ManagedConnectionFailureKind.Unknown,
        });
        Assert.Equal(ManagedTrayMode.Classic, tray.Mode);
        Assert.Equal("打开经典窗口", tray.PrimaryActionText);

        tray.SetClassicMode(false);
        Assert.Equal(ManagedTrayMode.Faulted, tray.Mode);
    }

    private static ManagedSettingsViewModel CreateSettingsViewModel(
        IManagedPreferencesStore store,
        Func<bool>? autoRunProvider = null,
        Func<bool, CancellationToken, Task<bool>>? autoRunUpdater = null,
        Func<CancellationToken, Task>? logout = null,
        Func<CancellationToken, Task<ClassicModeHandoffResult>>? classicHandoff = null)
        => new(
            store,
            autoRunProvider ?? (() => false),
            autoRunUpdater ?? ((_, _) => Task.FromResult(true)),
            _ => Task.FromResult<int?>(42),
            logout ?? (_ => Task.CompletedTask),
            classicHandoff ?? (_ => Task.FromResult(ClassicModeHandoffResult.Succeeded())),
            _ => Task.CompletedTask);

    private sealed class InMemoryPreferencesStore : IManagedPreferencesStore
    {
        public ManagedPreferences Value { get; private set; } = new();

        public Task<ManagedPreferences> ReadAsync(CancellationToken ct = default)
            => Task.FromResult(Value);

        public Task WriteAsync(ManagedPreferences preferences, CancellationToken ct = default)
        {
            Value = preferences;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCoordinator : IManagedConnectionCoordinator
    {
        public event Action<ManagedConnectionStatus>? StatusChanged;

        public ManagedConnectionStatus Status { get; private set; } = new();

        public Task<ManagedConnectionResult> StartAsync(
            ManagedConnectionStartRequest request,
            CancellationToken ct = default)
            => Task.FromResult(ManagedConnectionResult.FromStatus(Status));

        public Task<ManagedConnectionResult> StopAsync(CancellationToken ct = default)
            => Task.FromResult(ManagedConnectionResult.FromStatus(Status));

        public void SetStatus(ManagedConnectionStatus status)
        {
            Status = status;
            StatusChanged?.Invoke(status);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"netaccel-settings-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
