# WP-06A Result - Managed Source Isolation and Operation Guards

Status: code_complete
Date: 2026-06-17

## Scope

T16-R2-16 (managed/native SQLite isolation) and T16-R2-17/T16-R2-18 front-half
(edit/delete/copy/share/export guards). Does not enter WP-06B (backup/restore,
classic-mode launcher, hotkey/tray owner guard, unified exit cleanup).

## Design Decisions

1. **Detection mechanism**: Managed profiles use `IndexId = "managed:{profile.Id}"`
   (set by `ManagedProfileAdapter.ToProfileItem`). The guard class checks this
   prefix rather than adding a new `ProfileSource` field to `ProfileItem`, which
   would require upstream schema changes and migration.

2. **Defense in depth**: Guards are placed at both the ServiceLib handler level
   (ConfigHandler, FmtHandler, InnerFmt) and the ViewModel level (ProfilesViewModel).
   Handler-level guards prevent any direct call from persisting/exporting managed
   data. ViewModel-level guards provide user-friendly notices before the handler
   is reached.

3. **Existing `ProfileSourcePolicy` preserved**: The domain-level policy in
   `NetAccel.Managed.Domain` already defines the correct rules
   (Managed: only Connect/Select/Diagnose). The new `ManagedProfileGuard` in
   ServiceLib implements the enforcement at the actual call sites, using the same
   semantics.

4. **Classic paths preserved**: All v2rayN native local-node, subscription,
   clipboard import, and export capabilities remain unchanged in ServiceLib for
   classic compatibility. WP-06A blocks only NetAccel Managed profiles from
   classic mutation/export paths. No classic parser, handler, or UI entry point
   was deleted or disabled.

5. **Exception-based guard**: `ManagedProfileGuard.EnsureNotManaged` throws
   `InvalidOperationException`, matching the pattern used by
   `ProfileSourcePolicy.EnsureAllowed`. ViewModel methods pre-check managed
   profiles and show user-friendly notices before handler-level exceptions are
   reached.

## Files Changed

| File | Change |
|------|--------|
| `v2rayN/ServiceLib/Common/ManagedProfileGuard.cs` | **New** — guard class with `IsManaged`, `EnsureNotManaged`, `EnsureNoneManaged` |
| `v2rayN/ServiceLib/Handler/ConfigHandler.cs` | Added guard to `AddServerCommon`, `RemoveServers`, `CopyServer` |
| `v2rayN/ServiceLib/Handler/Fmt/FmtHandler.cs` | Added guard to `GetShareUri` (returns null for managed) |
| `v2rayN/ServiceLib/Handler/Fmt/InnerFmt.cs` | Added guard to `ToUri` |
| `v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs` | Added guards to `EditServerAsync`, `RemoveServerAsync`, `ShareServerAsync`, `CopyServer`, `Export2ClientConfigAsync`, `Export2ClientConfigResult`, `Export2ShareUrlAsync`, `Export2InnerUrlAsync` |
| `NetAccel.Managed.Tests/ManagedProfileGuardTests.cs` | **New** — 21 tests for guard behavior and ProfileSourcePolicy consistency |

## Guard Matrix

| Operation | Managed | Local | LegacySubscription | Guard location |
|-----------|---------|-------|--------------------|----------------|
| Edit | ❌ blocked | ✅ allowed | ✅ allowed | ProfilesViewModel + ConfigHandler.AddServerCommon |
| Delete | ❌ blocked | ✅ allowed | ✅ allowed | ProfilesViewModel + ConfigHandler.RemoveServers |
| Copy | ❌ blocked | ✅ allowed | ✅ allowed | ProfilesViewModel + ConfigHandler.CopyServer |
| Share | ❌ blocked | ✅ allowed | ✅ classic path preserved | ProfilesViewModel + FmtHandler.GetShareUri |
| Export | ❌ blocked | ✅ allowed | ✅ allowed | ProfilesViewModel + FmtHandler.GetShareUri + InnerFmt.ToUri |
| Connect | ✅ allowed | ✅ allowed | ✅ allowed | (no guard needed) |
| Select | ✅ allowed | ✅ allowed | ✅ allowed | (no guard needed) |
| Diagnose | ✅ allowed | ✅ allowed | ✅ allowed | (no guard needed) |
| Backup | ❌ blocked (domain) | ✅ allowed | ✅ allowed | WP-06B scope |

## SQLite SubItem/ProfileItem Isolation

- `ConfigHandler.AddServerCommon` — rejects managed profiles before `SQLiteHelper.Instance.ReplaceAsync`
- `ConfigHandler.RemoveServers` — rejects managed profiles before `SQLiteHelper.Instance.UpdateAllAsync`
- `ConfigHandler.CopyServer` — rejects managed profiles before `AddServerCommon`
- Managed profiles never enter SQLite because `ManagedProfileAdapter` creates
  short-lived in-memory `ProfileItem` objects that are only used during a
  managed connection session

## Direct Handler/ViewModel Bypass Analysis

**WPF code-behind calls ViewModel methods directly** (keyboard shortcuts bypass
command CanExecute). The guards are placed at the ViewModel method level, so
direct calls from code-behind are also protected:

- `Ctrl+D` → `EditServerAsync()` → guard checks `SelectedProfile.IndexId`
- `Delete/Back` → `RemoveServerAsync()` → guard checks selected list
- `Ctrl+C` → `Export2ShareUrlAsync()` → guard checks selected list
- `Ctrl+F` → `ShareServerAsync()` → guard checks `SelectedProfile.IndexId`

**ConfigHandler guards** protect against any path that bypasses the ViewModel
entirely (e.g., future direct handler calls from managed runtime or tests).

## Evidence: Classic Paths Preserved

- `FmtHandler.ResolveConfig` — unchanged, parses clipboard/scan input for all protocols
- `ConfigHandler.AddBatchServers` — unchanged, processes subscription and clipboard imports
- `ConfigHandler.AddServer` — unchanged, dispatches to type-specific Add* methods
- `SubscriptionHandler` — unchanged, downloads and processes subscriptions
- All `*Fmt.Resolve` methods — unchanged, parse share URIs
- `ProfilesViewModel.EditSubAsync`, `DeleteSubAsync` — unchanged, manage SubItem CRUD
- `ProfileSourcePolicy` still records the longer-term domain preference that
  `LegacySubscription` should not expose `Share`; WP-06A does not enforce that
  in ServiceLib because Plan 16 phase 1 preserves classic behavior until Plan 17
  evaluates deprecation.

## Verification

```
dotnet build NetAccel.Managed -c Debug           → 0 warnings, 0 errors
dotnet build v2rayN/v2rayN -c Debug              → 0 warnings, 0 errors
dotnet test ManagedProfileGuardTests             → 21/21 passed
dotnet test WP-05 + WP-06A tests                → 41/41 passed
dotnet test ServiceLib.Tests --no-build          → 69/69 passed
dotnet test NetAccel.Managed.Tests (full)        → 175 passed, 14 failed (pre-existing fixture path)
git diff --check                                 → CRLF warning on CoreManager.cs (pre-existing)
```

Pre-existing failures (14): All `FileNotFoundException` for
`contracts/fixtures/managed-config-payload-vless.json` — fixture loader resolves
relative to Master repo sibling directory. Not WP-06A regressions.

## Not Executed

| Item | Reason |
|------|--------|
| WPF UI-level guard verification | No managed WPF shell yet (R3); guards verified at ViewModel/handler level |
| Backup/restore isolation | WP-06B scope |
| Hotkey/tray owner guard | WP-06B scope |
| Classic-mode launcher | WP-06B scope |
| Unified exit cleanup | WP-06B scope |

## Risks/Limits

1. **Backup/restore not guarded**: `BackupAndRestoreViewModel.LocalBackup` zips
   the entire config directory including SQLite. Since managed profiles are never
   written to SQLite, they won't appear in backups. However, if a future bug
   writes managed data to SQLite, it would be backed up. WP-06B should add
   explicit backup exclusions for managed secrets.

2. **`ProfileItem` has no `Source` field**: Detection relies on the `managed:`
   prefix convention. If a non-managed profile somehow gets an IndexId starting
   with `managed:`, it would be incorrectly blocked. This is unlikely since
   `Utils.GetGuid(false)` generates standard GUIDs without prefixes.

3. **WPF code-behind direct calls**: The guards protect against keyboard shortcut
   bypass because they're at the ViewModel method level, not at the command
   CanExecute level. However, if a future code-behind change calls a different
   ViewModel method that lacks a guard, it could bypass protection.

## Dependencies for WP-06B

- Backup/restore exclusion for managed envelope cache, tokens, instance credentials
- Classic-mode launcher with owner handoff
- Hotkey/tray owner guard
- Unified graceful shutdown and SessionEnding cleanup
