# WP-06 Overall Acceptance Result

Status: accepted
Date: 2026-06-17

## Summary

WP-06 (T16-R2-16 through T16-R2-22) code-level verification passes. All protection
matrix items are verified through code inspection and unit tests. One minor test gap
was filled (Classic owner blocks Managed acquire). No blocking issues found.

## Code Changes This Session

One test added to fill a gap in the ownership matrix:

| File | Change |
|------|--------|
| `NetAccel.Managed.Tests/ConnectionOwnershipCoordinatorTests.cs` | Added `AcquireAsync_ClassicOwnerBlocksManagedAcquire` test |

All other code was already in place from WP-06A and WP-06B.

## WP-06 Protection Matrix Results

| Item | Expected | Status | Evidence |
|------|----------|--------|----------|
| 托管 ProfileItem 不写入 SubItem/ProfileItem | managed: only in memory | ✅ | `ManagedProfileAdapter` creates in-memory ProfileItem; `AddServerCommon` rejects `managed:` prefix |
| AddServerCommon 拒绝 managed: | Throws InvalidOperationException | ✅ | `ManagedProfileGuard.EnsureNotManaged` at line 1100 of ConfigHandler.cs |
| RemoveServers 拒绝 managed: | Throws InvalidOperationException | ✅ | `ManagedProfileGuard.EnsureNoneManaged` at line 324 |
| CopyServer 拒绝 managed: | Throws InvalidOperationException | ✅ | `ManagedProfileGuard.EnsureNoneManaged` at line 346 |
| EditCustomServer / ViewModel edit | 拒绝托管来源 | ✅ | `ProfilesViewModel.EditServerAsync` checks `ManagedProfileGuard.IsManaged` before loading from DB |
| Share / FmtHandler / InnerFmt | 托管来源无法生成链接 | ✅ | `FmtHandler.GetShareUri` returns null for managed; `InnerFmt.ToUri` throws for managed |
| Backup ZIP 不含 managed secrets | Files filtered before ZIP | ✅ | `BackupAndRestoreViewModel.RemoveManagedFilesFromDirectory` filters `managed-*` and `netaccel-credential-*` |
| Restore ZIP 跳过托管文件 | Files skipped + DB cleaned | ✅ | `ZipExtractToFileExcludingManaged` skips managed files; `ManagedConnectionGuard.RunPostRestoreCleanupAsync` strips managed:* from restored DB |
| Managed owner active → classic Reload blocked | Blocked with message | ✅ | `MainWindowViewModel.Reload` checks `ManagedConnectionGuard.CanPerformClassicOperation()` |
| Managed owner active → classic proxy blocked | Blocked with message | ✅ | `StatusBarViewModel.SetListenerType` checks guard |
| Managed owner active → classic TUN blocked | Blocked with message | ✅ | `StatusBarViewModel.DoEnableTun` checks guard |
| Managed owner active → classic hotkey blocked | Blocked (flows through Reload/proxy) | ✅ | F5 → Reload; global proxy → SetListenerType — both guarded |
| Classic handoff: Managed→Classic stops managed | Stops managed, releases, acquires classic | ✅ | `ClassicModeLauncher.TryHandoffToClassicAsync` calls `managedCoordinator.StopAsync` then acquires Classic |
| Classic owner held → Managed acquire blocked | Owner coordinator blocks | ✅ | `AcquireAsync_ClassicOwnerBlocksManagedAcquire` test; `AcquireAsync_AllowsOnlyOneOwnerAtATime` test |
| AppExitAsync calls unified cleanup | Managed cleanup before classic cleanup | ✅ | `AppManager.AppExitAsync` calls `ManagedConnectionGuard.RunExitCleanupAsync()` before proxy/core cleanup |
| Exit cleanup idempotent | Safe to call multiple times | ✅ | `ManagedExitCleanupTests.RunExitCleanupAsync_IsIdempotent` test |
| Exit cleanup not cancelled by caller token | Uses CancellationToken.None | ✅ | `ManagedExitCleanup.RunExitCleanupAsync` uses `CancellationToken.None` for all operations |
| 经典本地节点仍可用 | Add/edit/copy/share/export for Local | ✅ | `ProfileSourcePolicy.Can(ProfileSource.Local, ...)` returns true for all operations; `ManagedProfileGuard` only blocks `managed:` prefix |
| 通用订阅仍可用 | Add/update/import for LegacySubscription | ✅ | `ProfileSourcePolicy` allows all except Share for LegacySubscription; no guard blocks subscription operations |
| 经典备份恢复仍可用 | Works, excludes managed data | ✅ | `BackupAndRestoreViewModel` unchanged for classic paths; managed files filtered from backup/restore |

## Classic Compatibility Regression Summary

| Capability | Status | Notes |
|------------|--------|-------|
| 经典本地节点添加 | ✅ 保留 | `ConfigHandler.AddServer` unchanged for non-managed profiles |
| 经典本地节点编辑 | ✅ 保留 | `ProfilesViewModel.EditServerAsync` allows non-managed |
| 经典本地节点复制 | ✅ 保留 | `CopyServer` allows non-managed |
| 经典本地节点分享 | ✅ 保留 | `ShareServerAsync` allows non-managed |
| 经典本地节点导出 | ✅ 保留 | Export methods allow non-managed |
| 通用订阅添加 | ✅ 保留 | `ConfigHandler.AddSubItem` unchanged |
| 通用订阅更新 | ✅ 保留 | `SubscriptionHandler` unchanged |
| 剪贴板导入 | ✅ 保留 | `ConfigHandler.AddBatchServers` unchanged for non-managed |
| 经典备份 | ✅ 保留 | `LocalBackup` works; managed files excluded from ZIP |
| 经典恢复 | ✅ 保留 | `LocalRestore` works; managed files skipped; DB cleaned |
| 经典系统代理 | ✅ 保留 | `SetListenerType` works when no managed owner |
| 经典 TUN | ✅ 保留 | `DoEnableTun` works when no managed owner |
| 经典 Reload/F5 | ✅ 保留 | `Reload` works when no managed owner |

## Code Audit Findings

### ManagedConnectionGuard Registration

- **Location**: `ManagedConnectionCoordinator` constructor calls `ManagedExitCleanup.Register(_ownership, this)`
- **When**: At managed coordinator creation time (when managed shell initializes)
- **Default**: When no managed layer registered, `IsClassicOperationAllowed` defaults to `true` (backward compatible)

### ClassicModeLauncher Lease Handling

- **Finding**: `ClassicModeLauncher` holds the Classic lease in `_classicLease` field
- **Release**: Via `ReleaseClassicAsync()` or `DisposeAsync()`
- **Rationale**: Classic ownership must be held to prevent Managed from starting while classic core owns system state
- **Test**: `TryHandoffToClassic_WhenNoOwner_AcquiresClassic` verifies ownership state after handoff

### ManagedExitCleanup Idempotency

- **Finding**: All operations wrapped in try/catch, uses `CancellationToken.None`
- **Test**: `RunExitCleanupAsync_IsIdempotent` verifies double-call safety
- **Test**: `RunExitCleanupAsync_WhenNoOwner_DoesNotThrow` verifies no-op safety

### Backup/Restore Filtering Consistency

- **Backup filter**: `managed-*` and `netaccel-credential-*` prefixes in `BackupAndRestoreViewModel`
- **Restore filter**: Same prefixes via `ManagedExitCleanupFileFilter` predicate
- **DB cleanup**: `DELETE FROM ProfileItem WHERE IndexId LIKE 'managed:%'` and `DELETE FROM SubItem WHERE Id LIKE 'managed:%'`
- **Design assumption**: Managed cache files are in `guiConfigs/` (same as classic config). If future versions move managed cache to a separate NetAccel app data directory, the backup filter still applies to any managed files that end up in `guiConfigs/`.

### Static Callback Test Cleanup

- **Finding**: All tests that set `ManagedConnectionGuard.*` callbacks reset them to `null` in the test body
- **No test pollution**: Verified by running full test suite (206 passed, 14 pre-existing failures)

### Direct Handler/ViewModel Bypass

- **Keyboard shortcuts** (Ctrl+D, Delete, Ctrl+C, Ctrl+F, F5): All flow through ViewModel methods that have guards
- **Global hotkeys** (proxy change): Flow through `SetListenerType` which is guarded
- **Event-based paths** (AppEvents.ReloadRequested): Flow through `MainWindowViewModel.Reload` which is guarded
- **Direct ConfigHandler calls**: `AddServerCommon`, `RemoveServers`, `CopyServer` all have guards

## Verification Commands and Results

| Command | Result |
|---------|--------|
| `dotnet build NetAccel.Managed -c Debug` | ✅ 0 warnings, 0 errors |
| `dotnet build v2rayN/v2rayN -c Debug` | ✅ 0 warnings, 0 errors |
| `dotnet test --filter "ConnectionOwnership\|ManagedConnection\|ManagedProfileGuard\|ClassicModeLauncher\|ManagedExitCleanup"` | ✅ 72/72 passed |
| `dotnet test ServiceLib.Tests` | ✅ 69/69 passed |
| `dotnet test NetAccel.Managed.Tests (full)` | 206 passed, 14 failed (pre-existing) |
| `git diff --check` | CRLF warning on CoreManager.cs (pre-existing) |

Pre-existing failures (14): All `FileNotFoundException` for
`contracts/fixtures/managed-config-payload-vless.json` — fixture loader resolves
relative to Master repo sibling directory. Not WP-06 regressions.

## Not Executed

| Item | Reason |
|------|--------|
| Real Windows admin TUN E2E | Requires admin elevation; verified via unit tests |
| Real Master / VLESS Reality / Hysteria2 E2E | Requires real credentials and assigned lines |
| WPF UI-level guard verification | No managed WPF shell yet (R3); guards verified at ViewModel/handler level |
| Cross-process ownership E2E | Requires two processes; single-process tests verify logic |
| Real backup/restore ZIP audit | Requires running app with guiConfigs; logic verified via unit tests |

## Risks/Limits

1. **Backup filter is prefix-based**: Files matching `managed-*` or
   `netaccel-credential-*` are excluded. Unlikely collision with non-managed files.

2. **Post-restore SQLite cleanup uses direct SQL**: Schema changes in future
   v2rayN versions may need query updates.

3. **ClassicModeLauncher holds Classic lease**: Must be disposed when leaving
   classic mode. If not disposed, ownership persists until process exit
   (handled by `ConnectionOwnershipCoordinator.DisposeAsync`).

4. **ManagedConnectionGuard defaults to allowed**: When no managed layer is
   registered (early startup, pure classic mode), all operations are allowed.
   This is correct for backward compatibility.

5. **guiConfigs/ is the backup scope**: If future versions move managed cache
   to a separate NetAccel app data directory, the backup filter still covers
   any managed files in `guiConfigs/`. The separate directory would need its
   own backup exclusion strategy.

## Git Status Summary

```
Modified (10 files):
  M v2rayN/ServiceLib/Common/FileUtils.cs
  M v2rayN/ServiceLib/Handler/ConfigHandler.cs
  M v2rayN/ServiceLib/Handler/Fmt/FmtHandler.cs
  M v2rayN/ServiceLib/Handler/Fmt/InnerFmt.cs
  M v2rayN/ServiceLib/Manager/AppManager.cs
  M v2rayN/ServiceLib/Manager/CoreManager.cs
  M v2rayN/ServiceLib/ViewModels/BackupAndRestoreViewModel.cs
  M v2rayN/ServiceLib/ViewModels/MainWindowViewModel.cs
  M v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs
  M v2rayN/ServiceLib/ViewModels/StatusBarViewModel.cs

New (18 files):
  ?? .plan16/ (6 prompt/result files)
  ?? NetAccel.Managed.Tests/ (5 test files)
  ?? NetAccel.Managed/Runtime/ (4 runtime files)
  ?? v2rayN/ServiceLib/Common/ (2 guard files)

git diff --check: CRLF warning on CoreManager.cs (pre-existing)
```
