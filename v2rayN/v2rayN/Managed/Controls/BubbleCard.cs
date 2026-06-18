using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace v2rayN.Managed.Controls;

public sealed class BubbleCard : ContentControl
{
    static BubbleCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(BubbleCard),
            new FrameworkPropertyMetadata(typeof(BubbleCard)));
        IsTabStopProperty.OverrideMetadata(
            typeof(BubbleCard),
            new FrameworkPropertyMetadata(false));
        FocusableProperty.OverrideMetadata(
            typeof(BubbleCard),
            new FrameworkPropertyMetadata(false));
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new BubbleCardAutomationPeer(this);
    }
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(BubbleCard),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(BubbleCard),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsHighlightedProperty = DependencyProperty.Register(
        nameof(IsHighlighted),
        typeof(bool),
        typeof(BubbleCard),
        new PropertyMetadata(false));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool IsHighlighted
    {
        get => (bool)GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }
}

internal sealed class BubbleCardAutomationPeer : FrameworkElementAutomationPeer
{
    private readonly BubbleCard _owner;

    public BubbleCardAutomationPeer(BubbleCard owner)
        : base(owner)
    {
        _owner = owner;
    }

    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Group;
    }

    protected override string GetNameCore()
    {
        var name = GetAutomationName();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = _owner.Title;
        }

        return string.IsNullOrWhiteSpace(name) ? base.GetNameCore() : name;
    }

    private string? GetAutomationName()
    {
        return (string?)_owner.GetValue(AutomationProperties.NameProperty);
    }
}
