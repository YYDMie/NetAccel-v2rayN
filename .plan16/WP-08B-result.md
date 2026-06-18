# WP-08B Result - Managed Diagnostics Checks and Reversible Repair

Status: code_complete
Date: 2026-06-18

## Scope

This slice advances T16-R3-08 with the diagnostics page checks and safe repair
path requested for WP-08B:

- Adds four read-only checks for service connection, current route, local
  network state, and the acceleration engine.
- Adds friendly diagnostics cards, recheck, one-click repair, and expandable
  details to the managed WPF shell.
- Detects stale managed runtime state, a missing core, and an active route that
  is no longer in the authorized payload.
- Runs repair steps in the bounded order: stop the managed connection, stop a
  residual core, restore the managed system-proxy snapshot, remove stale
  managed TUN state, and resync the current valid managed configuration.

## Safety Boundaries

- Read-only checks use in-memory managed config, connection status, connection
  ownership, and runtime snapshots; they do not perform HTTP or repair writes.
- Automatic repair is blocked while classic mode owns the connection.
- A healthy active managed connection is not stopped merely because the user
  clicks repair; it only resyncs configuration.
- Windows system-proxy values are captured before managed activation and
  restored exactly, including a pre-existing user proxy or PAC URL.
- TUN cleanup runs only for TUN state marked as managed and only after the core
  has stopped.
- Resync goes through `ManagedLoginViewModel.RetryAsync`, so login/shell state
  returns to Ready instead of remaining stuck in a progress phase.
- The diagnostics and repair paths contain no account logout, credential
  deletion, classic SQLite, local-node, or classic-subscription calls.

## Verification

- `ManagedDiagnosticsServiceTests`: 7/7 passed.
- Full `NetAccel.Managed.Tests`: 264/264 passed.
- `ServiceLib.Tests`: 69/69 passed.
- WPF Debug build: 0 warnings, 0 errors.
- `git diff --check`: passed.
- Forbidden-path scan found no SQLite, subscription, account logout,
  credential-clear, or file-delete call in the diagnostics page and service.

## Remaining Evidence

- Capture real WPF screenshots at 100/125/150/200% scaling.
- Run real Windows fault injection for a stale proxy, failed core, and stale
  TUN device before acceptance.
- Diagnostic-package export remains a separate follow-up; this slice contains
  only the requested checks, details, and reversible repair path.
