# WP-08C Result - Allowlisted Diagnostic Export

Status: code_complete
Date: 2026-06-18

## Scope

This slice completes the diagnostic-package follow-up for T16-R3-08:

- Adds a WPF save action for an explicitly named ZIP diagnostic package.
- Generates exactly `diagnostics.json` and `privacy.txt` from an allowlist.
- Exports only product/version, coarse check results, connection state enums,
  runtime booleans, and the connection owner.
- Reports cancellation and write failures with friendly UI messages.
- Uses a same-directory temporary file and removes it after failed exports.

## Privacy Boundaries

- Export does not call the managed-config provider or read profile parameters.
- The package does not include profile IDs, display names, endpoints, raw error
  messages, tokens, credentials, logs, SQLite, envelopes, runtime config, or
  classic nodes and subscriptions.
- The exporter does not enumerate or compress an existing directory.
- Tests inject token, UUID, endpoint, profile ID, and Authorization-shaped data
  and verify that none of it enters the archive.
- A forced destination failure verifies that no temporary archive remains.

## Verification

- `ManagedDiagnosticsServiceTests`: 12/12 passed.
- Full `NetAccel.Managed.Tests`: 269/269 passed.
- WPF Debug build: 0 warnings, 0 errors.
- `git diff --check`: passed.
- Boundary scan found no log/database/config-directory reads or directory ZIP
  operations in the diagnostic export path.

## Remaining Evidence

- Exercise the real WPF save dialog and inspect the resulting ZIP on the target
  Windows build.
- Capture the diagnostics page and save-dialog flow at the required display
  scaling levels before acceptance.
