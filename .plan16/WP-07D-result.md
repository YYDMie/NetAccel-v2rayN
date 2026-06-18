# WP-07D Result - Managed Routes And Controlled Selection

Status: code_complete
Date: 2026-06-18

## Scope

This slice advances T16-R3-06 and closes the missing managed payload fixture
dependency used by the client tests:

- Displays only profiles returned by the managed status endpoint for the
  current account and instance.
- Provides automatic selection and manual selection within the assigned set.
- Disables maintenance, unavailable, and capability-incompatible routes.
- Disables selection while the live assignment revision and decrypted payload
  are out of sync, and enforces the managed manual-selection policy in the UI.
- Uses selection revision for disconnected preference updates.
- Uses `ManagedConnectionCoordinator.SwitchAsync` for connected route changes,
  preserving the existing stop/start and previous-connection restore behavior.
- Propagates the saved instance preference to the home page so the next start
  uses the selected automatic/manual mode.
- Shows cached assigned routes as read-only when the status endpoint is
  unavailable.
- Restores the canonical `managed-config/v1` payload Schema and the VLESS
  Reality/Hysteria2 fixtures in the sibling Master repository.

## Boundaries

- The route page has no add, edit, copy, share, export, reorder, or import
  operation.
- Host, port, protocol, UUID, credentials, and core details are never exposed
  by the route presentation model or XAML.
- The page cannot display or select a profile that is absent from the managed
  status response.
- Cached/offline routes are visible for context but cannot update the server
  selection.
- Classic local-node, subscription, import, and export behavior is unchanged.

## Verification

- `go test ./...` in the Master `contracts/` module: passed.
- Managed home and routes tests: 16/16 passed.
- Full `NetAccel.Managed.Tests`: 248/248 passed.
- `dotnet test v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj`: 69/69 passed.
- `dotnet build v2rayN/v2rayN/v2rayN.csproj -c Debug --no-restore -m:1`:
  passed with 0 warnings and 0 errors.
- Managed XAML copy scan found no protocol, subscription, proxy, port, key,
  UUID, or endpoint text.
- `git diff --check`: no whitespace errors; existing line-ending conversion
  warnings remain on tracked files.

## Remaining Work

- Real Master selection and revision-conflict evidence.
- Connected manual switching against real VLESS Reality and Hysteria2 routes.
- Route page screenshots at 100/125/150/200% and keyboard-focus evidence.
- Activity, diagnostics, settings, tray, classic handoff, and final branding
  remain later R3 slices.
