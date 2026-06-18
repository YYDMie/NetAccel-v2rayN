using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using v2rayN;
using v2rayN.Converters;
using v2rayN.Managed.Services;
using v2rayN.Managed.Views;

internal static class Program
{
    private const int Width = 820;
    private const int Height = 620;

    [STAThread]
    private static void Main(string[] args)
    {
        var hostMode = args.Length >= 2 && string.Equals(args[0], "--host", StringComparison.Ordinal);
        var repositoryRoot = hostMode || args.Length == 0
            ? FindRepositoryRoot(AppContext.BaseDirectory)
            : Path.GetFullPath(args[0]);
        var output = Path.Combine(repositoryRoot, ".plan16", "evidence", "WP-09");

        var app = new Application();
        LoadProductionResources(app);
        App.ProgramStarted = new EventWaitHandle(false, EventResetMode.AutoReset);

        var shell = new ManagedShellWindow();
        var shellRoot = (FrameworkElement)shell.Content;
        shellRoot.SetResourceReference(Panel.BackgroundProperty, "ManagedBrushBgCanvas");
        using var theme = new ManagedThemeService(app);

        if (hostMode)
        {
            RunAccessibilityHost(app, shell, shellRoot, theme, args[1]);
            return;
        }

        Directory.CreateDirectory(output);
        theme.Apply("light", shell);
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            Render(shellRoot, scale, Path.Combine(output, $"login-light-{scale * 100:0}.png"));
        }

        var login = (ManagedLoginView)shell.FindName("LoginView");
        var shellContent = (Grid)shell.FindName("ShellContent");
        login.Visibility = Visibility.Collapsed;
        shellContent.Visibility = Visibility.Visible;

        ((RadioButton)shell.FindName("HomeNavigation")).IsChecked = true;
        Render(shellRoot, 1.0, Path.Combine(output, "home-light-100.png"));

        ((RadioButton)shell.FindName("SettingsNavigation")).IsChecked = true;
        Render(shellRoot, 1.0, Path.Combine(output, "settings-light-100.png"));
        theme.Apply("dark", shell);
        Render(shellRoot, 1.0, Path.Combine(output, "settings-dark-100.png"));

        Console.WriteLine($"Rendered WP-09 WPF evidence to {output}");
    }

    private static void RunAccessibilityHost(
        Application app,
        ManagedShellWindow shell,
        FrameworkElement shellRoot,
        ManagedThemeService theme,
        string mode)
    {
        var login = (ManagedLoginView)shell.FindName("LoginView");
        var shellContent = (Grid)shell.FindName("ShellContent");
        if (string.Equals(mode, "settings", StringComparison.OrdinalIgnoreCase))
        {
            login.Visibility = Visibility.Collapsed;
            shellContent.Visibility = Visibility.Visible;
            ((RadioButton)shell.FindName("SettingsNavigation")).IsChecked = true;
            theme.Apply("dark");
        }
        else
        {
            login.Visibility = Visibility.Visible;
            shellContent.Visibility = Visibility.Collapsed;
            theme.Apply("light");
        }

        shell.Content = null;
        var host = new Window
        {
            Width = Width,
            Height = Height,
            Content = shellRoot,
            Title = $"NetAccel Accessibility Host - {mode}",
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        AutomationProperties.SetName(host, "NetAccel 加速客户端");
        app.Run(host);
    }

    private static void Render(FrameworkElement root, double scale, string path)
    {
        root.Measure(new Size(Width, Height));
        root.Arrange(new Rect(0, 0, Width, Height));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)Math.Round(Width * scale),
            (int)Math.Round(Height * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
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
                     "Colors.xaml", "Typography.xaml", "Spacing.xaml", "Radius.xaml", "Buttons.xaml",
                     "Cards.xaml", "Inputs.xaml", "Navigation.xaml", "Controls.xaml", "Accessibility.xaml",
                     "NetAccelIcon.xaml", "NetAccelTrayIcons.xaml",
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
}
