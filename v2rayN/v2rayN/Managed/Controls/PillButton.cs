using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace v2rayN.Managed.Controls;

public sealed class PillButton : Button
{
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new PillButtonAutomationPeer(this);
    }
}

internal sealed class PillButtonAutomationPeer : ButtonAutomationPeer
{
    private readonly PillButton _owner;

    public PillButtonAutomationPeer(PillButton owner)
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

        // Fall back to Content as string
        if (_owner.Content is string content && !string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        return base.GetNameCore();
    }

    private string? GetAutomationName()
    {
        return (string?)_owner.GetValue(AutomationProperties.NameProperty);
    }
}
