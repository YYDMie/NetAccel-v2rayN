using System.Windows.Controls;

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
}
