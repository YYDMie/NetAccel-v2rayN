# WP-08D Result - Settings, Tray, and Classic Handoff

Status: code_complete
Date: 2026-06-18

## Scope

This slice completes the remaining T16-R3-09 through T16-R3-11 code paths:

- Makes `ManagedShellWindow` the default NetAccel startup window.
- Adds common, account/device, update, and advanced settings sections.
- Persists only non-sensitive managed preferences in the NetAccel user-data
  directory with atomic replacement and corrupt-file fallback.
- Wires auto-run to the existing NetAccel-specific startup handler, auto-connect
  to `ManagedHomeViewModel`, close-to-tray to the managed window lifecycle, and
  notifications to connection state transitions.
- Adds one managed tray icon whose menu follows `ManagedConnectionCoordinator`
  state and switches to the classic-mode menu while Classic owns the connection.
- Uses `ClassicModeLauncher` to stop Managed safely and hold Classic ownership
  before creating the upstream WPF window.
- Marks the upstream window as `NetAccel · 经典模式`, hides the promotion entry,
  preserves upstream/GPL help, and adds a return-to-managed action.
- Stops the classic core, restores system proxy/TUN state, releases Classic
  ownership, and returns to the managed home.
- Restores single-instance wake-up and Windows session-ending cleanup for the
  new default window.

## Boundaries

- Managed preferences contain only booleans and the preferred network mode.
- Settings and tray presentation do not read or persist tokens, credentials,
  endpoints, profile configuration, classic nodes, or subscriptions.
- The classic source database and compatibility features remain unchanged.
- The upstream tray is hidden only for the NetAccel product; the managed tray
  remains the single visible tray owner.
- The update section reports the current version and stable channel but does
  not expose a fake update action before WP-12 implements signed updates.

## Verification

- `ManagedSettingsAndTrayTests`: 5/5 passed.
- Full `NetAccel.Managed.Tests`: 274/274 passed.
- `ServiceLib.Tests`: 69/69 passed.
- WPF Debug build: 0 warnings, 0 errors.
- `git diff --check`: passed.
- Managed settings/tray boundary scan found no classic persistence, subscription,
  credential, token, endpoint, or managed-profile parameter access.

## Remaining Evidence

- The Windows automation helper could not initialize because its packaged
  runtime exposed an incompatible module surface, so no UI clicks or screenshots
  are claimed in this result.
- Run the default-startup, close-to-tray, notification, classic open/return, and
  session-ending flows on the target Windows build.
- Capture managed settings, tray menus, and the classic-mode marker at the
  required 100/125/150/200% display scaling before Codex acceptance.
