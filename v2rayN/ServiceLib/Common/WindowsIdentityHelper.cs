using System.Runtime.InteropServices;

namespace ServiceLib.Common;

/// <summary>
/// P/Invoke helper for Windows Shell identity APIs.
/// Used to set the explicit AppUserModelID for the current process,
/// which controls taskbar grouping and jump list identity.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsIdentityHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
