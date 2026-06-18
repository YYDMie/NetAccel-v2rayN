using System.Windows.Controls;

namespace v2rayN.Managed.Controls;

public sealed class RouteBubble : Button
{
    public static readonly DependencyProperty DisplayNameProperty = DependencyProperty.Register(
        nameof(DisplayName),
        typeof(string),
        typeof(RouteBubble),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RegionProperty = DependencyProperty.Register(
        nameof(Region),
        typeof(string),
        typeof(RouteBubble),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText),
        typeof(string),
        typeof(RouteBubble),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected),
        typeof(bool),
        typeof(RouteBubble),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsRecommendedProperty = DependencyProperty.Register(
        nameof(IsRecommended),
        typeof(bool),
        typeof(RouteBubble),
        new PropertyMetadata(false));

    public string DisplayName
    {
        get => (string)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public string Region
    {
        get => (string)GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public bool IsRecommended
    {
        get => (bool)GetValue(IsRecommendedProperty);
        set => SetValue(IsRecommendedProperty, value);
    }
}
