using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace v2rayN.Managed.Helpers;

/// <summary>
/// Static helper methods for common WPF accessibility patterns.
/// </summary>
public static class AutomationHelper
{
    /// <summary>
    /// Updates the <see cref="AutomationProperties.NameProperty"/> of a <see cref="Control"/>.
    /// Safe to call from any thread (uses the dispatcher internally).
    /// </summary>
    public static void UpdateAutomationName(Control control, string name)
    {
        if (control.Dispatcher.CheckAccess())
        {
            AutomationProperties.SetName(control, name);
        }
        else
        {
            control.Dispatcher.Invoke(() => AutomationProperties.SetName(control, name));
        }
    }

    /// <summary>
    /// Updates the <see cref="AutomationProperties.HelpTextProperty"/> of a <see cref="Control"/>.
    /// Safe to call from any thread.
    /// </summary>
    public static void UpdateAutomationHelpText(Control control, string helpText)
    {
        if (control.Dispatcher.CheckAccess())
        {
            AutomationProperties.SetHelpText(control, helpText);
        }
        else
        {
            control.Dispatcher.Invoke(() => AutomationProperties.SetHelpText(control, helpText));
        }
    }

    /// <summary>
    /// Raises a UI Automation live-region event so screen readers announce the new text.
    /// Call this from the UI thread; the element should have <c>AutomationProperties.LiveSetting="Polite"</c>
    /// or <c>"Assertive"</c> set in XAML.
    /// </summary>
    public static void RaiseLiveRegionChanged(FrameworkElement element, string newText)
    {
        if (element == null)
        {
            return;
        }

        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer is IValueProvider valueProvider)
        {
            try
            {
                valueProvider.SetValue(newText);
            }
            catch (InvalidOperationException)
            {
                // Some elements do not support IValueProvider; fall through silently.
            }
        }
    }

    /// <summary>
    /// Announces a status change by updating the automation name on a control and
    /// optionally raising a live-region notification.
    /// </summary>
    public static void AnnounceStatus(Control control, string name, bool raiseLiveRegion = false)
    {
        UpdateAutomationName(control, name);

        if (raiseLiveRegion)
        {
            RaiseLiveRegionChanged(control, name);
        }
    }

    /// <summary>
    /// Sets focus to the given element on the UI thread. Useful for startup focus
    /// and programmatic navigation.
    /// </summary>
    public static void SetFocusOnDispatcher(FrameworkElement element)
    {
        if (element.Dispatcher.CheckAccess())
        {
            element.Focus();
        }
        else
        {
            element.Dispatcher.Invoke(() => element.Focus());
        }
    }

    /// <summary>
    /// Returns a localized state description for <see cref="ConnectOrbState"/> values.
    /// </summary>
    public static string GetOrbStateDescription(string orbState) => orbState switch
    {
        "Idle" => "未加速，按 Enter 开始加速",
        "Starting" => "正在连接",
        "Connected" => "已加速",
        "Recovering" => "正在恢复连接",
        "Faulted" => "连接失败，请打开诊断",
        "Disabled" => "不可用",
        _ => orbState,
    };
}
