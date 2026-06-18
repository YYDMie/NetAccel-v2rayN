using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace v2rayN.Managed.Controls;

public sealed class RouteBubble : Button
{
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new RouteBubbleAutomationPeer(this);
    }
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

internal sealed class RouteBubbleAutomationPeer : ButtonAutomationPeer
{
    private readonly RouteBubble _owner;

    public RouteBubbleAutomationPeer(RouteBubble owner)
        : base(owner)
    {
        _owner = owner;
    }

    protected override string GetNameCore()
    {
        var name = GetAutomationName();
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        // Compose: "DisplayName, Region, StatusText" for screen readers
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_owner.DisplayName))
        {
            parts.Add(_owner.DisplayName);
        }
        if (!string.IsNullOrWhiteSpace(_owner.Region))
        {
            parts.Add(_owner.Region);
        }
        if (!string.IsNullOrWhiteSpace(_owner.StatusText))
        {
            parts.Add(_owner.StatusText);
        }

        return parts.Count > 0 ? string.Join(", ", parts) : base.GetNameCore();
    }

    private string? GetAutomationName()
    {
        return (string?)_owner.GetValue(AutomationProperties.NameProperty);
    }
}
