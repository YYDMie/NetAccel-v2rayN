# WP-07C Result - Managed Home Connection Wiring

Status: code_complete
Date: 2026-06-18

## Scope

This slice advances T16-R3-05 and connects the managed home page to the R2
runtime:

- Composes `ConnectionOwnershipCoordinator`, `ServiceLibManagedCoreRunner`, and
  `ManagedConnectionCoordinator` in the WPF managed runtime.
- Binds the home card to the current managed payload, recommended route, and
  connection state.
- Implements one-click start/stop for system-proxy and TUN modes.
- Maps starting, connected, stopping, no-assignment, permission, fallback,
  classic-owner conflict, and failure states to beginner-friendly copy.
- Prevents duplicate home actions and repeated home initialization.
- Cleans the managed connection and unregisters ServiceLib guard callbacks when
  the managed shell closes.

The classic WPF window remains the default startup entry. Default-startup
switching still waits for the classic-mode handoff UI and real managed
environment evidence.

## Boundaries

- The View does not call HTTP, `CoreManager`, system proxy, TUN, or classic
  import/export APIs directly.
- Managed profiles remain in memory and are not written to `SubItem` or classic
  SQLite.
- The first-stage classic local-node, subscription, import, and export paths are
  unchanged.
- The home page exposes only friendly route names and status text; it does not
  show host, port, protocol, UUID, credentials, or core details.

## Verification

- `dotnet build v2rayN/v2rayN/v2rayN.csproj -c Debug --no-restore -m:1`:
  passed with 0 warnings and 0 errors.
- Home ViewModel tests: 8/8 passed.
- Login, startup, connection, home, and cleanup regression filter: 68/68
  passed.
- `dotnet test v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj -c Debug
  --no-restore --no-build -m:1`: 69/69 passed.
- Full `NetAccel.Managed.Tests`: 238/238 passed after the sibling Master
  restored the canonical `managed-config/v1` Schema and VLESS/Hysteria2
  fixtures from the existing Plan 16 contract branch.
- `git diff --check`: no whitespace errors in tracked changes; existing
  line-ending conversion warnings remain.

## Remaining Work

- Real Master login and managed connection with a configured signing key.
- Real system-proxy and administrator/non-administrator TUN evidence.
- 100/125/150/200% screenshots, keyboard focus, and reduced-animation checks.
- Classic-mode handoff and return entry before changing the default startup
  window.
- Routes, records, diagnostics, settings, tray, and final branding remain later
  R3 slices.
