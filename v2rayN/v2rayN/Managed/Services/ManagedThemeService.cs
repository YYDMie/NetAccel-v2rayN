using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.ComponentModel;
using System.Windows.Media;

namespace v2rayN.Managed.Services;

public sealed class ManagedThemeService : IDisposable
{
    private const string LightSource = "/Managed/Resources/Colors.xaml";
    private const string DarkSource = "/Managed/Resources/Colors.Dark.xaml";
    private static readonly string[] HighContrastOverrideKeys =
    [
        "ManagedBrushBgCanvas",
        "ManagedBrushBgSurface",
        "ManagedBrushBgBubble",
        "ManagedBrushBgBubbleHover",
        "ManagedBrushTextPrimary",
        "ManagedBrushTextSecondary",
        "ManagedBrushTextDisabled",
        "ManagedBrushBorderDefault",
        "ManagedBrushPrimary500",
        "ManagedBrushPrimary600",
        "ManagedBrushPrimary100",
    ];

    private readonly Application _application;
    private string _preference = "system";
    private bool _disposed;

    public ManagedThemeService(Application application)
    {
        _application = application;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
    }

    public bool AnimationsEnabled { get; private set; } = true;
    public bool IsDark { get; private set; }

    public void Apply(string? preference, Window? window = null)
    {
        _preference = Normalize(preference);
        var highContrast = SystemParameters.HighContrast;
        var dark = _preference switch
        {
            "light" => false,
            "dark" => true,
            _ => IsWindowsDarkTheme(),
        };

        IsDark = dark;
        AnimationsEnabled = !highContrast && SystemParameters.ClientAreaAnimation;
        ClearHighContrastOverrides();
        ReplaceColorDictionary(dark ? DarkSource : LightSource);

        if (highContrast)
        {
            ApplyHighContrastColors();
        }

        _application.Resources["ManagedAnimationsEnabled"] = AnimationsEnabled;
        var theme = new PaletteHelper().GetTheme();
        theme.SetBaseTheme(dark ? BaseTheme.Dark : BaseTheme.Light);
        new PaletteHelper().SetTheme(theme);

        if (window != null)
        {
            WindowsUtils.SetDarkBorder(window, dark ? "Dark" : "Light");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
    }

    private void ReplaceColorDictionary(string source)
    {
        var dictionaries = _application.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.EndsWith("Colors.xaml", StringComparison.OrdinalIgnoreCase) == true
            || dictionary.Source?.OriginalString.EndsWith("Colors.Dark.xaml", StringComparison.OrdinalIgnoreCase) == true);
        var replacement = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };

        if (current == null)
        {
            dictionaries.Insert(0, replacement);
            return;
        }

        var index = dictionaries.IndexOf(current);
        dictionaries[index] = replacement;
    }

    private void ApplyHighContrastColors()
    {
        _application.Resources["ManagedBrushBgCanvas"] = SystemColors.WindowBrush;
        _application.Resources["ManagedBrushBgSurface"] = SystemColors.WindowBrush;
        _application.Resources["ManagedBrushBgBubble"] = SystemColors.ControlBrush;
        _application.Resources["ManagedBrushBgBubbleHover"] = SystemColors.HighlightBrush;
        _application.Resources["ManagedBrushTextPrimary"] = SystemColors.WindowTextBrush;
        _application.Resources["ManagedBrushTextSecondary"] = SystemColors.WindowTextBrush;
        _application.Resources["ManagedBrushTextDisabled"] = SystemColors.GrayTextBrush;
        _application.Resources["ManagedBrushBorderDefault"] = SystemColors.WindowTextBrush;
        _application.Resources["ManagedBrushPrimary500"] = SystemColors.HighlightBrush;
        _application.Resources["ManagedBrushPrimary600"] = SystemColors.HighlightTextBrush;
        _application.Resources["ManagedBrushPrimary100"] = SystemColors.ControlBrush;
    }

    private void ClearHighContrastOverrides()
    {
        foreach (var key in HighContrastOverrideKeys)
        {
            _application.Resources.Remove(key);
        }
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Color
            or UserPreferenceCategory.General
            or UserPreferenceCategory.Accessibility)
        {
            _application.Dispatcher.InvokeAsync(() => Apply(_preference, _application.MainWindow));
        }
    }

    private void SystemParameters_StaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.HighContrast)
            or nameof(SystemParameters.ClientAreaAnimation))
        {
            _application.Dispatcher.InvokeAsync(() => Apply(_preference, _application.MainWindow));
        }
    }

    private static bool IsWindowsDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string Normalize(string? preference)
        => preference?.Trim().ToLowerInvariant() switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => "system",
        };
}
