# WP-06B Result - Backup Isolation, ClassicModeLauncher, Owner Guard, Exit Cleanup

Status: code_complete_codex_verified
Date: 2026-06-17

## Scope

T16-R2-19 (backup/restore isolation), T16-R2-20 (ClassicModeLauncher),
T16-R2-21 (hotkey/tray/reload owner guard), T16-R2-22 (unified exit cleanup).

## Design Decisions

### 1. ServiceLib ↔ Managed Layer Bridge

ServiceLib cannot reference `NetAccel.Managed`. To enable cross-layer callbacks:

- **`ManagedConnectionGuard`** (ServiceLib.Common): Static class with settable
  `Func<bool>?` and `Func<Task>?` callbacks. The managed layer registers its
  implementations at startup. ServiceLib consumers call the guard methods.
- **`ManagedExitCleanup`** (NetAccel.Managed.Runtime): Registers cleanup callbacks
  with `ManagedConnectionGuard` at startup. Provides file/database cleanup for
  backup isolation and exit cleanup for ownership release.
- **Codex verification fix**: `ManagedConnectionCoordinator` now calls
  `ManagedExitCleanup.Register(_ownership, this)` from its constructor, so the
  ServiceLib owner guard and exit cleanup callbacks are active once the managed
  runtime coordinator is created. A regression test covers this wiring.

### 2. Backup Isolation

- **Backup**: `CreateZipFileFromDirectory` now filters out files matching
  `managed-*` or `netaccel-credential-*` prefixes before creating the ZIP.
  These files are excluded from the staging directory before compression.
- **Restore**: `LocalRestore` uses a new `ZipExtractToFileExcludingManaged`
  method that delegates to `FileUtils.ZipExtractToFile` with an exclude predicate.
  After extraction, `ManagedConnectionGuard.RunPostRestoreCleanupAsync` removes
  any managed entries from the restored SQLite database.
- **`FileUtils.ZipExtractToFile`**: New overload accepting a `Func<string, bool>`
  exclude predicate, preserving backward compatibility with the existing string-based
  `ignoredName` parameter.

### 3. ClassicModeLauncher

- Lives in `NetAccel.Managed.Runtime` alongside `ConnectionOwnershipCoordinator`
  and `ManagedConnectionCoordinator`.
- `TryHandoffToClassicAsync`: If Managed owns, stops managed connection via
  `ManagedConnectionCoordinator.StopAsync`, then releases Managed ownership. If
  Classic already owns, returns success (no-op). If no one owns, acquires and
  holds Classic ownership until `ReleaseClassicAsync`, disposal, or unified exit
  cleanup releases it. This prevents Managed from acquiring ownership while the
  classic core owns system state.
- `IsClassicOperationAllowed`: Returns `true` when owner is not Managed. This is
  the callback registered with `ManagedConnectionGuard.IsClassicOperationAllowed`.

### 4. Hotkey/Tray/Reload Owner Guard

- `MainWindowViewModel.Reload`: Checks `ManagedConnectionGuard.CanPerformClassicOperation()`
  before starting classic core. Shows user-friendly message if blocked.
- `StatusBarViewModel.SetListenerType`: Checks guard before changing system proxy.
- `StatusBarViewModel.DoEnableTun`: Checks guard before toggling TUN.
- F5 hotkey in `MainWindow.xaml.cs` calls `ViewModel.Reload()` which is now guarded.
- Global proxy hotkeys publish `AppEvents.SysProxyChangeRequested` which flows through
  `SetListenerType` — now guarded.

### 5. Unified Exit Cleanup

- `AppManager.AppExitAsync`: Calls `ManagedConnectionGuard.RunExitCleanupAsync()`
  before classic cleanup (proxy clear, core stop). This ensures managed connection
  is stopped, ownership is released, and TUN is cleaned up.
- `RunExitCleanupAsync` is idempotent: safe to call multiple times.
- All exceptions are swallowed to ensure the rest of shutdown continues.
- `SessionEnding` in `MainWindow.xaml.cs` calls `AppExitAsync` which now includes
  managed cleanup.

## Files Changed

| File | Change |
|------|--------|
| `v2rayN/ServiceLib/Common/ManagedConnectionGuard.cs` | **New** — bridge for ownership checks and cleanup callbacks |
| `NetAccel.Managed/Runtime/ClassicModeLauncher.cs` | **New** — managed→classic handoff service |
| `NetAccel.Managed/Runtime/ManagedExitCleanup.cs` | **New** — exit cleanup, backup isolation, post-restore cleanup |
| `NetAccel.Managed.Tests/ClassicModeLauncherTests.cs` | **New** — 9 tests for handoff, persistent Classic lease, and guard behavior |
| `NetAccel.Managed.Tests/ManagedExitCleanupTests.cs` | **New** — 16 tests for cleanup, backup exclusion, guard callbacks, and runtime guard registration |
| `NetAccel.Managed/Runtime/ManagedConnectionCoordinator.cs` | Registers managed cleanup and classic-operation guard callbacks during construction |
| `v2rayN/ServiceLib/Manager/AppManager.cs` | Added managed exit cleanup call in AppExitAsync |
| `v2rayN/ServiceLib/ViewModels/MainWindowViewModel.cs` | Added owner guard in Reload |
| `v2rayN/ServiceLib/ViewModels/StatusBarViewModel.cs` | Added owner guard in SetListenerType and DoEnableTun |
| `v2rayN/ServiceLib/ViewModels/BackupAndRestoreViewModel.cs` | Added backup exclusion filter, post-restore cleanup, new extract method |
| `v2rayN/ServiceLib/Common/FileUtils.cs` | Added ZipExtractToFile overload with exclude predicate |

## Protection Matrix

| Scenario | Protection | Location |
|----------|-----------|----------|
| Backup ZIP contains managed secrets | Files filtered out before ZIP creation | BackupAndRestoreViewModel.CreateZipFileFromDirectory |
| Restore ZIP overwrites managed state | Files skipped during extraction | BackupAndRestoreViewModel.ZipExtractToFileExcludingManaged |
| Restored DB has managed:* entries | SQL DELETE after extraction | ManagedExitCleanup.RemoveManagedDatabaseEntries |
| Managed active → classic F5/Reload | Blocked with message | MainWindowViewModel.Reload |
| Managed active → classic proxy change | Blocked with message | StatusBarViewModel.SetListenerType |
| Managed active → classic TUN toggle | Blocked with message | StatusBarViewModel.DoEnableTun |
| Managed active → classic global hotkey | Blocked (flows through SetListenerType) | StatusBarViewModel |
| Exit with managed active | Cleanup runs before classic cleanup | AppManager.AppExitAsync |
| Exit cleanup called twice | Idempotent — no double-release | ManagedExitCleanup.RunExitCleanupAsync |
| Classic already owns → handoff | No-op, returns success | ClassicModeLauncher.TryHandoffToClassicAsync |
| Managed owns → handoff | Stops managed, releases Managed, holds Classic owner | ClassicModeLauncher.TryHandoffToClassicAsync |
| Classic lease held → Managed acquire | Blocked by owner coordinator | ClassicModeLauncherTests |

## Verification

```
dotnet build NetAccel.Managed -c Debug           → 0 warnings, 0 errors
dotnet build v2rayN/v2rayN -c Debug              → 0 warnings, 0 errors
dotnet test WP-05+06A+06B filtered tests         → 71/71 passed
dotnet test ServiceLib.Tests                     → 69/69 passed
dotnet test NetAccel.Managed.Tests (full)        → 205 passed, 14 failed (pre-existing fixture path)
git diff --check                                  → CRLF warning on CoreManager.cs (pre-existing)
```

Pre-existing failures (14): All `FileNotFoundException` for
`contracts/fixtures/managed-config-payload-vless.json` — fixture loader resolves
relative to Master repo sibling directory. Not WP-06B regressions.

## Not Executed

| Item | Reason |
|------|--------|
| Real Windows TUN cleanup E2E | Requires admin elevation; verified via unit tests |
| Real backup/restore ZIP audit | Requires running app with guiConfigs directory; logic verified via unit tests |
| WPF UI-level owner guard verification | No managed WPF shell yet (R3); guards verified at ViewModel level |
| Cross-process ownership handoff | Requires two processes; single-process tests verify the logic |

## Risks/Limits

1. **Backup exclusion is prefix-based**: Files matching `managed-*` or
   `netaccel-credential-*` are excluded. If a non-managed file happens to start
   with these prefixes, it would be incorrectly excluded. This is unlikely given
   the naming conventions.

2. **Post-restore SQLite cleanup uses direct SQL**: The `RemoveManagedDatabaseEntries`
   method opens a direct SQLite connection to the restored database. If the database
   schema changes in a future v2rayN version, the SQL queries may need updating.

3. **ClassicModeLauncher lifetime**: Classic ownership is now held by the
   launcher until `ReleaseClassicAsync`, disposal, or unified exit cleanup. R3
   shell integration must keep this launcher/service alive while classic mode is
   active.

4. **Owner guard is advisory**: The guard checks `CurrentOwner != Managed` before
   allowing classic operations. If the managed layer hasn't registered its callback
   (e.g., during early startup), the guard defaults to allowing all operations.
   This is the correct behavior for backward compatibility.

## Dependencies for Next Tasks

- WP-07 (R3 UI): Will integrate `ManagedConnectionGuard` with the managed shell
  for proper owner lifecycle management.
- Classic mode entry point: The WPF `ClassicModeLauncher` integration (opening
  the classic window) is R3 scope. The runtime service is ready.
