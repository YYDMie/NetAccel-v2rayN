# WP-07A Result - Managed WPF UI Foundation

Status: code_complete
Date: 2026-06-18

## Scope

This slice covers the independently testable foundation of T16-R3-01 through
T16-R3-03:

- Managed design tokens and shared resource dictionaries.
- BubbleCard, PillButton, StatusBubble, RouteBubble, and ConnectOrb controls.
- A minimal ManagedShellWindow with navigation and a home-state preview.

It does not yet make the managed shell the default startup window and does not
implement final login, connection wiring, route selection, tray, diagnostics,
settings, or classic-window handoff.

## Boundaries

- Existing classic WPF views and v2rayN compatibility features are unchanged.
- The shell contains no HTTP, credential, CoreManager, system proxy, TUN, or
  managed-profile persistence calls.
- Managed controls use shared resources rather than page-level repeated colors
  and corner radii.

## Verification

- `dotnet build v2rayN/v2rayN/v2rayN.csproj -c Debug`: passed with 0 warnings
  and 0 errors.
- `dotnet test v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj -c Debug`:
  passed, 69/69.
- WP-05/WP-06 regression filter in `NetAccel.Managed.Tests`: passed, 72/72.
- `git diff --check`: no whitespace errors; existing line-ending conversion
  warning remains on `App.xaml`.
- Static boundary scan confirms the new Managed shell does not reference HTTP,
  credentials, `CoreManager`, system proxy, TUN, `SubItem`, or
  `AddBatchServers`.
- Visual automation was attempted after temporarily setting the managed shell as
  the startup window, but the Windows automation runtime failed before launch.
  The startup entry was restored to the classic window. Screenshot, scaling, and
  keyboard-focus evidence remain required before Codex acceptance.

## Remaining Work

- WP-07B: final login page and startup-state wiring.
- WP-07C: home page state mapping to `ManagedConnectionCoordinator`.
- Real 100/125/150/200% screenshots and keyboard-navigation evidence.
- Switch the default startup entry only after managed login/startup recovery and
  classic-mode handoff are wired.
