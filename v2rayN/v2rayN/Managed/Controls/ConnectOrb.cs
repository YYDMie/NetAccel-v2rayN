using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using v2rayN.Managed.Helpers;

namespace v2rayN.Managed.Controls;

public enum ConnectOrbState
{
    Idle,
    Starting,
    Connected,
    Recovering,
    Faulted,
    Disabled,
}

public sealed class ConnectOrb : Button
{
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new ConnectOrbAutomationPeer(this);
    }
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(ConnectOrbState),
        typeof(ConnectOrb),
        new PropertyMetadata(ConnectOrbState.Idle));

    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText),
        typeof(string),
        typeof(ConnectOrb),
        new PropertyMetadata("未加速"));

    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(
        nameof(ActionText),
        typeof(string),
        typeof(ConnectOrb),
        new PropertyMetadata("一键加速"));

    public static readonly DependencyProperty IsAnimationEnabledProperty = DependencyProperty.Register(
        nameof(IsAnimationEnabled),
        typeof(bool),
        typeof(ConnectOrb),
        new PropertyMetadata(true));

    public ConnectOrbState State
    {
        get => (ConnectOrbState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public bool IsAnimationEnabled
    {
        get => (bool)GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }
}

internal sealed class ConnectOrbAutomationPeer : ButtonAutomationPeer
{
    private readonly ConnectOrb _owner;

    public ConnectOrbAutomationPeer(ConnectOrb owner)
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

        // Compose: "一键加速, 当前状态: 未加速"
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_owner.ActionText))
        {
            parts.Add(_owner.ActionText);
        }

        var stateDesc = AutomationHelper.GetOrbStateDescription(_owner.State.ToString());
        if (!string.IsNullOrWhiteSpace(stateDesc))
        {
            parts.Add($"当前状态: {stateDesc}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : base.GetNameCore();
    }

    private string? GetAutomationName()
    {
        return (string?)_owner.GetValue(AutomationProperties.NameProperty);
    }
}
