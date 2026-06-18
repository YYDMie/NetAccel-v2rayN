using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Axe.Windows.Automation;
using Axe.Windows.Automation.Data;
using v2rayN;
using v2rayN.Converters;
using v2rayN.Managed.Controls;
using v2rayN.Managed.Helpers;
using v2rayN.Managed.Services;
using v2rayN.Managed.Views;
using Xunit;

namespace NetAccel.Managed.Wpf.Tests;

public sealed class ManagedWpfSmokeTests
{
    [Fact]
    public async Task LoginHomeSettingsNavigationThemeAndAccessibilityUseRealWpfControls()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                RunSmokeTest();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        })
        {
            IsBackground = true,
            Name = "NetAccel WPF smoke test",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken);
    }

    private static void RunSmokeTest()
    {
        var app = new Application();
        LoadProductionResources(app);
        App.ProgramStarted = new EventWaitHandle(false, EventResetMode.AutoReset);

        Assert.IsType<DrawingImage>(app.TryFindResource("NetAccelIcon"));
        Assert.IsType<DrawingImage>(app.TryFindResource("NetAccelTrayIdle"));
        Assert.IsType<DrawingImage>(app.TryFindResource("NetAccelTrayConnected"));
        Assert.IsType<DrawingImage>(app.TryFindResource("NetAccelTrayFaulted"));

        using (var theme = new ManagedThemeService(app))
        {
            theme.Apply("dark");
            Assert.True(theme.IsDark);
            Assert.Equal(Color.FromRgb(0x10, 0x16, 0x13), Assert.IsType<Color>(app.TryFindResource("ManagedColorBgCanvas")));

            theme.Apply("light");
            Assert.False(theme.IsDark);
            Assert.Equal(Color.FromRgb(0xF5, 0xF7, 0xF6), Assert.IsType<Color>(app.TryFindResource("ManagedColorBgCanvas")));
        }

        var shell = new ManagedShellWindow();
        var brandIcon = Assert.IsType<Image>(shell.FindName("BrandIcon"));
        Assert.NotNull(brandIcon.Source);

        var login = Assert.IsType<ManagedLoginView>(shell.FindName("LoginView"));
        var username = Assert.IsType<TextBox>(login.FindName("UsernameTextBox"));
        Assert.Equal("账号", AutomationProperties.GetName(username));
        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(login));

        var home = Assert.IsType<RadioButton>(shell.FindName("HomeNavigation"));
        var settings = Assert.IsType<RadioButton>(shell.FindName("SettingsNavigation"));
        var pageTitle = Assert.IsType<TextBlock>(shell.FindName("PageTitle"));
        home.IsChecked = true;
        Assert.Equal("首页", pageTitle.Text);
        Assert.True(shell.TryHandleShellShortcut(Key.Tab, ModifierKeys.Control));
        Assert.Equal("线路", pageTitle.Text);
        Assert.True(shell.TryHandleShellShortcut(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Equal("首页", pageTitle.Text);
        settings.IsChecked = true;
        Assert.Equal("设置", pageTitle.Text);
        login.Visibility = Visibility.Collapsed;
        Assert.IsType<Grid>(shell.FindName("ShellContent")).Visibility = Visibility.Visible;
        var shellRoot = Assert.IsAssignableFrom<FrameworkElement>(shell.Content);
        shellRoot.Measure(new Size(820, 620));
        shellRoot.Arrange(new Rect(0, 0, 820, 620));
        shellRoot.UpdateLayout();
        var themeSelector = Assert.IsType<ComboBox>(shell.FindName("ThemeSelector"));
        Assert.True(
            themeSelector.ActualWidth >= 300,
            $"Theme selector width was {themeSelector.ActualWidth} with MinWidth {themeSelector.MinWidth}.");
        var themeSelectorRoot = Assert.IsAssignableFrom<FrameworkElement>(VisualTreeHelper.GetChild(themeSelector, 0));
        Assert.True(
            themeSelectorRoot.ActualWidth >= 300,
            $"Theme selector template width was {themeSelectorRoot.ActualWidth}.");
        var themeSelectorToggle = Assert.IsType<ToggleButton>(VisualTreeHelper.GetChild(themeSelectorRoot, 0));
        Assert.True(
            themeSelectorToggle.ActualWidth >= 300,
            $"Theme selector toggle width was {themeSelectorToggle.ActualWidth}.");

        using (var theme = new ManagedThemeService(app))
        {
            theme.Apply("dark", shell);
            var expectedText = Color.FromRgb(0xF1, 0xF7, 0xF4);
            Assert.Equal(expectedText, Assert.IsType<SolidColorBrush>(
                Assert.IsType<CheckBox>(shell.FindName("AutoRunCheckBox")).Foreground).Color);
            Assert.Equal(expectedText, Assert.IsType<SolidColorBrush>(
                    themeSelector.Foreground).Color);
        }

        var shellStatus = Assert.IsType<StatusBubble>(shell.FindName("ShellStatus"));
        AutomationHelper.AnnounceStatus(shellStatus, "设备已就绪", raiseLiveRegion: true);
        Assert.Equal("设备已就绪", AutomationProperties.GetName(shellStatus));
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(shellStatus));

        AssertAxeClean("login");
        AssertAxeClean("settings");
    }

    private static void AssertAxeClean(string mode)
    {
        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var hostPath = Path.Combine(
            repositoryRoot,
            "tools",
            "NetAccel.WpfEvidence",
            "bin",
            configuration,
            "net10.0-windows10.0.19041.0",
            "NetAccel.WpfEvidence.exe");
        Assert.True(File.Exists(hostPath), $"Accessibility host was not built: {hostPath}");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = hostPath,
            Arguments = $"--host {mode}",
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Could not start the accessibility host.");

        try
        {
            process.WaitForInputIdle(15000);
            var startup = Stopwatch.StartNew();
            while (!process.HasExited && process.MainWindowHandle == IntPtr.Zero && startup.Elapsed < TimeSpan.FromSeconds(15))
            {
                Thread.Sleep(100);
                process.Refresh();
            }

            Assert.False(process.HasExited, "Accessibility host exited before creating its window.");
            Assert.NotEqual(IntPtr.Zero, process.MainWindowHandle);
            var config = Config.Builder.ForProcessId(process.Id).Build();
            var scanner = ScannerFactory.CreateScanner(config);
            var output = scanner.Scan(new ScanOptions(scanId: mode, scanRootWindowHandle: process.MainWindowHandle));
            var errors = output.WindowScanOutputs.SelectMany(result => result.Errors).ToList();
            Assert.True(
                errors.Count == 0,
                $"Axe.Windows scan '{mode}' found {errors.Count} error(s):{Environment.NewLine}" +
                string.Join(
                    Environment.NewLine,
                    errors.Take(20).Select(error =>
                        $"{error.Rule.ID}: {error.Rule.Description}; " +
                        string.Join(", ", error.Element.Properties.Select(property => $"{property.Key}={property.Value}")))));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
    }

    private static string FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing global.json.");
    }

    private static void LoadProductionResources(Application app)
    {
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(
            """
            <materialDesign:BundledTheme
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:materialDesign="clr-namespace:MaterialDesignThemes.Wpf;assembly=MaterialDesignThemes.Wpf"
                BaseTheme="Light"
                PrimaryColor="Blue"
                SecondaryColor="Lime" />
            """));
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml",
                UriKind.Absolute),
        });

        foreach (var resource in new[]
                 {
                     "Colors.xaml",
                     "Typography.xaml",
                     "Spacing.xaml",
                     "Radius.xaml",
                     "Buttons.xaml",
                     "Cards.xaml",
                     "Inputs.xaml",
                     "Navigation.xaml",
                     "Controls.xaml",
                     "Accessibility.xaml",
                     "NetAccelIcon.xaml",
                     "NetAccelTrayIcons.xaml",
                 })
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/NetAccel;component/Managed/Resources/{resource}",
                    UriKind.Absolute),
            });
        }

        app.Resources["ManagedAnimationsEnabled"] = true;
        app.Resources["InverseBooleanConverter"] = new InverseBooleanConverter();
    }

}
