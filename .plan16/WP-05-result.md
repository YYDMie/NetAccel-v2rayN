# WP-05 Result - Connection Ownership And Managed Runtime

Status: code_complete_codex_verified
Date: 2026-06-17

## Scope

This work advances T16-R2-10 through T16-R2-15 only. It does not enter WP-06,
UI/R3, tray behavior, export/share/backup guards, or classic-mode removal.

## Project Positioning Alignment

WP-05 stays aligned with NetAccel's controlled-assignment learning-demo model:

- NetAccel is treated as a personal learning demo for managed acceleration
  clients, line operations, agent communication, admin control, and client
  runtime behavior.
- The managed runtime follows a game-accelerator style model: server/admin
  policy assigns the allowed line set, recommendation, fallback order, and
  runtime permissions; the device can use automatic selection or manual
  selection only inside that assigned set.
- `Server` means one VPS acceleration line. `Plan` means a technical
  acceleration configuration, not a package, paid plan, subscription tier, or
  sales product.
- Managed configuration is the source of truth for managed lines. WP-05 does
  not edit, share, export, convert, sell, or publish managed lines as
  subscription links.
- Plan 16 phase 1 keeps v2rayN's native local-node, generic subscription, and
  import/export capabilities as a compatibility and testing lane. WP-05 does
  not remove or break those classic capabilities; it keeps them separate from
  the NetAccel managed path. Hiding, disabling, or removing them belongs to a
  later phase after the managed path is mature and stable.
- WP-05 does not add airport/proxy-service behavior, payment, billing, package
  sales, traffic quota sales, public node pools, public line pools, or
  user-facing sales flows.

Implemented:

- `ConnectionOwnershipCoordinator` for Managed/Classic mutual exclusion,
  named-mutex protection, lease release, in-process concurrency control, and
  stale snapshot recovery.
- `ManagedConnectionCoordinator` with Ready, Starting, Connected, Stopping,
  and Faulted states.
- `ServiceLibManagedCoreRunner` that starts managed `CoreConfigContext` through
  `CoreManager` and applies system proxy through `SysProxyHandler`, without
  calling classic UI reload paths.
- Runtime system-proxy and TUN mode policy checks.
- Authorized-set automatic and manual profile resolution from the current
  managed payload only.
- Selection persistence through optional `IManagedSelectionService`.
- Successful runtime `applied` ACK through optional `IManagedConfigService`.
- Safe manual line switching with previous-profile restore on failed or
  cancelled switch.
- Automatic fallback inside the authorized fallback set, without overwriting a
  user's manual preference.
- Failure cleanup paths that stop the core runner and release the owner.

## Codex Verify/Fix Addendum

Claude Code fixed the ServiceLib visibility issue by keeping
`WindowsUtils` internal and adding `AppManager.RemoveTunDeviceAsync()` as the
public wrapper used by managed runtime TUN cleanup.

Codex then found and fixed one safety issue in the switch/cleanup path:

- Core stop and restore operations no longer inherit a caller cancellation
  token after a managed stop/switch has begun.
- A cancelled manual switch now performs best-effort cleanup and restores the
  previous profile before returning a cancelled result.
- Added regression tests for cancellation-safe stop cleanup and cancelled
  manual switch restore.

## Files Changed

- `.plan16/WP-05-codex-prompt.md`
- `.plan16/WP-05-claude-verify-fix-prompt.md`
- `.plan16/WP-05-windows-e2e-verify-prompt.md`
- `.plan16/WP-05-result.md`
- `NetAccel.Managed/Runtime/ConnectionOwnershipCoordinator.cs`
- `NetAccel.Managed/Runtime/ManagedConnectionCoordinator.cs`
- `NetAccel.Managed.Tests/ConnectionOwnershipCoordinatorTests.cs`
- `NetAccel.Managed.Tests/ManagedConnectionCoordinatorTests.cs`
- `v2rayN/ServiceLib/Manager/AppManager.cs`
- `v2rayN/ServiceLib/Manager/CoreManager.cs`

## Verification

Environment:

- .NET SDK: 10.0.301
- dotnet path: `C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe`
- `global.json` changed: no

Commands:

```powershell
& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' build .\NetAccel.Managed\NetAccel.Managed.csproj -c Debug
# Passed: 0 warnings, 0 errors

& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' build .\v2rayN\v2rayN\v2rayN.csproj -c Debug
# Passed: 0 warnings, 0 errors

& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' test .\NetAccel.Managed.Tests\NetAccel.Managed.Tests.csproj -c Debug --filter "FullyQualifiedName~ConnectionOwnershipCoordinatorTests|FullyQualifiedName~ManagedConnectionCoordinatorTests"
# Passed: 20/20

& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' test .\v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj -c Debug --no-build
# Passed: 69/69

git diff --check
# Passed with only the known CoreManager.cs LF/CRLF warning.

& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' test .\NetAccel.Managed.Tests\NetAccel.Managed.Tests.csproj -c Debug --no-build
# Existing fixture-path failures: 14 failed; latest Windows verification observed 154 passed.
```

## Required Behavior Coverage

| Required behavior | Coverage |
|---|---|
| Managed/Classic ownership is exclusive | `ConnectionOwnershipCoordinatorTests` |
| stale owner snapshot can recover | `AcquireAsync_RecoversStaleSnapshotWhenProcessIsGone` |
| live owner snapshot is rejected | `AcquireAsync_RejectsLiveSnapshot` |
| state machine publishes start/connect/stop/ready | `StateMachine_PublishesStartConnectStopReadySequence` |
| system proxy mode does not enable TUN | `StartAsync_SystemProxy_AcquiresOwnerAndConnectsRecommendedProfile` |
| TUN requires client and profile policy | `StartAsync_Tun_RequiresClientAndProfilePolicy` |
| TUN starts when both policies allow it | `StartAsync_Tun_StartsWithTunEnabledWhenAllowed` |
| manual selection rejects unassigned profiles | `StartAsync_ManualUnassignedProfileIsRejectedWithoutStartingCore` |
| manual selection rejects unavailable profiles | `StartAsync_ManualUnavailableProfileIsRejectedWithoutStartingCore` |
| failed manual switch restores previous profile | `SwitchAsync_FailedManualSwitchRestoresPreviousProfileAndDoesNotPersist` |
| cancelled manual switch restores previous profile | `SwitchAsync_CancelledManualSwitchRestoresPreviousProfile` |
| stop cleanup is not cancelled after stop begins | `StopAsync_CleansUpCoreWithNonCancellableStopToken` |
| fallback skips profiles outside current payload | `StartAsync_FallbackSkipsProfileIdsOutsideCurrentPayload` |
| fallback does not overwrite manual preference | `RecoverFromCoreFault_UsesAuthorizedFallbackWithoutOverwritingManualPreference` |
| applied ACK is sent after successful runtime application | `StartAsync_AcksAppliedAfterSuccessfulRuntimeApplication` |
| start failure stops core and releases owner | `StartAsync_ReleasesOwnerWhenCoreStartFails` |

## Boundary Check

- No runtime references to `Reload`, `SubItem`, `AddBatchServers`,
  `MainWindowViewModel`, or SQLite local-node mutation paths.
- `NetAccel.Managed` has no WPF dependency.
- Managed runtime uses managed payload data only; it does not import managed
  profiles into classic local server storage.
- Existing v2rayN native local-node, generic subscription, and import/export
  compatibility remains outside the managed runtime path for Plan 16 phase 1
  testing and migration.

## Not Executed

- Windows real-machine system proxy/TUN behavior validation was attempted and
  recorded in `.plan16/WP-05-windows-e2e-result.md`, but remains blocked by
  missing managed shell integration, missing administrator elevation for TUN,
  and no real managed payload/assigned lines.
- Real VLESS Reality / Hysteria2 E2E remains blocked by no Master credentials
  with assigned managed lines.
- Real Managed/Classic handoff and cross-process mutex E2E remain blocked by
  no integrated managed entry point / two-process harness in this run.

## Acceptance Note

Code-level verification is complete for WP-05, and the Windows E2E verification
attempt is documented as `blocked (partial)`. Final WP-05 acceptance should not
be granted until the blocked Windows behavior/E2E items in
`.plan16/WP-05-windows-e2e-result.md` are re-run with the required prerequisites.
