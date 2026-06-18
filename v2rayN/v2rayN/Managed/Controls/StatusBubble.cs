using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace v2rayN.Managed.Controls;

public enum StatusTone
{
    Neutral,
    Success,
    Warning,
    Danger,
    Info,
}

public sealed class StatusBubble : Control
{
    static StatusBubble()
    {
        IsTabStopProperty.OverrideMetadata(
            typeof(StatusBubble),
            new FrameworkPropertyMetadata(false));
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new StatusBubbleAutomationPeer(this);
    }
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(StatusBubble),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(string),
        typeof(StatusBubble),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone),
        typeof(StatusTone),
        typeof(StatusBubble),
        new PropertyMetadata(StatusTone.Neutral));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public StatusTone Tone
    {
        get => (StatusTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }
}

internal sealed class StatusBubbleAutomationPeer : FrameworkElementAutomationPeer
{
    private readonly StatusBubble _owner;

    public StatusBubbleAutomationPeer(StatusBubble owner)
        : base(owner)
    {
        _owner = owner;
    }

    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Text;
    }

    protected override string GetNameCore()
    {
        var name = GetAutomationName();
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        // Compose "label: value" for screen readers
        var label = _owner.Text;
        var value = _owner.Value;
        if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(value))
        {
            return $"{label}: {value}";
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (!string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        return base.GetNameCore();
    }

    private string? GetAutomationName()
    {
        return (string?)_owner.GetValue(AutomationProperties.NameProperty);
    }
}
