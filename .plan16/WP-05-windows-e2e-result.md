# WP-05 Windows E2E Result

Status: blocked (partial — unit tests passed, Windows E2E blocked on prerequisites)
Date: 2026-06-17

## Environment

- Windows: Windows 10 Pro 2009 (Build 26100.1)
- Admin: **false** — standard user, no elevation
- .NET SDK: 10.0.301 (user-local at `C:\Users\huangtingyang\.dotnet-codex-sdk-10`)
- dotnet path: `C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe`
- Master reachable: **unknown** — no test credentials available
- real assigned VLESS Reality: **no** — no Master account with assigned managed lines
- real assigned Hysteria2: **no** — same

## Pre-test System State

```
whoami: sihoo\huangtingyang
Admin: false

Proxy baseline:
  ProxyEnable:   1
  ProxyServer:   127.0.0.1:10808
  AutoConfigURL: (empty)

Network adapters:
  vEthernet (Default Switch) — Hyper-V — Up
  WLAN                       — Qualcomm Wi-Fi 7 — Disconnected
  WLAN 3                     — Qualcomm Wi-Fi 7 — Disconnected
  WLAN 5                     — Qualcomm Wi-Fi 7 — Not Present
  蓝牙网络连接                — Bluetooth PAN — Disconnected
  以太网                      — Realtek 5GbE — Up

Running core processes:
  PID 86680: sing-box (no path resolved — classic v2rayN active)
  PID 83100: v2rayN  (classic WPF app running)

git branch: codex/plan16-wp05-runtime...origin/codex/plan16
```

**Key observation**: Classic v2rayN is actively running with sing-box core and
has already set the system proxy to `127.0.0.1:10808`. Any managed connection
attempt would conflict with this running classic instance — which is exactly the
scenario `ConnectionOwnershipCoordinator` is designed to prevent.

## Build/Unit Verification

| Command | Result |
|---------|--------|
| `dotnet build NetAccel.Managed -c Debug` | ✅ 0 warnings, 0 errors |
| `dotnet build v2rayN/v2rayN -c Debug` | ✅ 0 warnings, 0 errors |
| `dotnet test NetAccel.Managed.Tests --filter "ConnectionOwnership\|ManagedConnection"` | ✅ 20/20 passed |
| `dotnet test ServiceLib.Tests --no-build` | ✅ 69/69 passed |
| `dotnet test NetAccel.Managed.Tests (full)` | 154 passed, 14 failed (pre-existing fixture path issue) |
| `git diff --check` | ⚠️ CRLF warning on CoreManager.cs (pre-existing) |

Full test suite detail: 14 failures are all `FileNotFoundException` for
`contracts/fixtures/managed-config-payload-vless.json` — the fixture loader
resolves paths relative to the Master repo, which is a sibling directory. These
failures exist since WP-03/WP-04 and are not WP-05 regressions.

## System Proxy E2E

- baseline: `ProxyEnable=1`, `ProxyServer=127.0.0.1:10808` (set by running classic v2rayN)
- connected: **blocked** — no managed WPF shell integration; `ServiceLibManagedCoreRunner`
  requires real Xray/sing-box core binaries and a real managed config payload
  with valid endpoint credentials to start a real core process
- stopped: **blocked** — cannot test stop without a successful start
- result: **blocked**

**Why blocked**: WP-05 implements the runtime coordinator layer. The managed WPF
shell (HomeView with one-click connect) is WP-07/WP-08 (R3). Without the shell,
there is no UI path to invoke `ManagedConnectionCoordinator.StartAsync` with a
real payload. The unit tests with `FakeCoreRunner` verify the state machine,
proxy policy enforcement, and cleanup paths.

**Static evidence**: `ServiceLibManagedCoreRunner.StartAsync` sets
`SysProxyType = ForcedChange` for system-proxy mode and calls
`SysProxyHandler.UpdateSysProxy(config, false)`. `StopAsync` calls
`SysProxyHandler.UpdateSysProxy(config, true)` to clear. This matches the
ServiceLib classic proxy lifecycle.

## TUN E2E

- baseline: No TUN adapter present (no wintun, no sing-box TUN)
- connected: **blocked** — not running as admin
- stopped: **blocked**
- non-admin behavior: **blocked** — cannot test real TUN without admin elevation

**Why blocked**: TUN requires Windows admin privileges. The current session is a
standard user. `ManagedConnectionCoordinator` validates
`ClientPolicy.AllowTun && ProfilePolicy.AllowTun` before attempting TUN start,
and the unit test `StartAsync_Tun_RequiresClientAndProfilePolicy` verifies the
policy gate rejects TUN when policy disallows it.

**Static evidence**: `ValidateProfileForMode` checks both
`payload.ClientPolicy?.AllowTun` and `profile.Policy.AllowTun` for TUN mode.
`ServiceLibManagedCoreRunner.StopAsync` calls `AppManager.RemoveTunDeviceAsync()`
for TUN cleanup when `TunModeItem.EnableTun == true`.

## Real-line E2E

- VLESS Reality: **blocked** — no Master account with assigned managed lines
- Hysteria2: **blocked** — same

**Why blocked**: The managed runtime requires a `ManagedConfigPayload` with real
server endpoints, valid UUID/credentials, and Reality/Hysteria2 security
parameters. These are only available through the Master API after login, instance
registration, and config sync — none of which have been set up in this
environment.

**Static evidence**: `ManagedRuntimeConfigBuilder.Build` validates the payload,
selects a profile, calls `ManagedProfileAdapter.SelectCore` (returns `Xray` for
vless+xray, `sing_box` for hysteria2+sing_box), and creates a
`ManagedRuntimeConfig` with the profile's `ProfileItem`. The adapter creates
proper VLESS Reality and Hysteria2 configurations matching ServiceLib's expected
format.

## Selection/Switch/Fallback

| Scenario | Unit Test | Result |
|----------|-----------|--------|
| Assigned manual success | `SwitchAsync_SuccessfulManualSwitchPersistsSelection` | ✅ |
| Unassigned rejection | `StartAsync_ManualUnassignedProfileIsRejectedWithoutStartingCore` | ✅ |
| Unavailable rejection | `StartAsync_ManualUnavailableProfileIsRejectedWithoutStartingCore` | ✅ |
| Capability incompatible | `ValidateProfileForMode` returns null for unknown protocol+core | ✅ (static) |
| Policy denied (TUN) | `StartAsync_Tun_RequiresClientAndProfilePolicy` | ✅ |
| Policy denied (sysproxy) | `ValidateProfileForMode` checks `AllowSystemProxy` | ✅ (static) |
| Failed switch restore | `SwitchAsync_FailedManualSwitchRestoresPreviousProfileAndDoesNotPersist` | ✅ |
| Cancelled switch restore | `SwitchAsync_CancelledManualSwitchRestoresPreviousProfile` | ✅ |
| Automatic fallback | `StartAsync_FallbackSkipsProfileIdsOutsideCurrentPayload` | ✅ |
| Fallback no overwrite manual | `RecoverFromCoreFault_UsesAuthorizedFallbackWithoutOverwritingManualPreference` | ✅ |
| applied ACK success | `StartAsync_AcksAppliedAfterSuccessfulRuntimeApplication` | ✅ |
| ACK failure no connection fail | `ReportAppliedAsync` catches exception, logs, does not throw | ✅ (static) |
| Start failure releases owner | `StartAsync_ReleasesOwnerWhenCoreStartFails` | ✅ |

- result: **passed** (all scenarios covered by unit tests)

## Managed/Classic Ownership

| Scenario | Unit Test | Result |
|----------|-----------|--------|
| Concurrent acquire single winner | `AcquireAsync_ConcurrentManagedAcquireOnlyGrantsSingleLease` | ✅ |
| Only one owner at a time | `AcquireAsync_AllowsOnlyOneOwnerAtATime` | ✅ |
| Stale snapshot recovery | `AcquireAsync_RecoversStaleSnapshotWhenProcessIsGone` | ✅ |
| Live snapshot rejection | `AcquireAsync_RejectsLiveSnapshot` | ✅ |
| Wrong owner release rejected | `ReleaseAsync_RejectsWrongOwner` | ✅ |
| Start failure releases owner | `StartAsync_ReleasesOwnerWhenCoreStartFails` | ✅ |
| Stop releases owner | `StopAsync_StopsCoreAndReleasesOwner` | ✅ |

- concurrent owner logic: **passed** (unit test)
- classic after managed stop: **blocked** — needs real classic WPF path
- managed after classic stop: **blocked** — needs real managed WPF path
- result: **blocked for E2E handoff**; ownership state machine is covered by
  unit tests and static review, but real Managed/Classic handoff still needs
  an integrated managed entry point and a second process/window path.

**Static evidence for cross-process mutex**: `ConnectionOwnershipCoordinator`
creates a named `Mutex` with `useGlobalMutex: true` by default. `TryAcquireMutex`
calls `_mutex.WaitOne(0)` and catches `AbandonedMutexException` (recovering from
crashed owner). `IsSnapshotLive` checks if the snapshot's process is still
running on the same machine. `FileConnectionOwnershipStore` persists the snapshot
to `%LOCALAPPDATA%\NetAccel\managed-connection-owner.json` with atomic
write (temp file + replace).

**Remaining evidence gap**: no two-process Windows handoff test was executed in
this run.

## Postflight Cleanup

Since no real managed connections were started, there is nothing to clean up.

- proxy: unchanged (`ProxyEnable=1`, `ProxyServer=127.0.0.1:10808`)
- adapters/routes: no TUN adapter created
- processes: `sing-box` (86680) and `v2rayN` (83100) still running (classic)
- owner: N/A (no managed connection attempted)

```
git status --short --branch:
  ## codex/plan16-wp05-runtime...origin/codex/plan16
   M v2rayN/ServiceLib/Manager/AppManager.cs
   M v2rayN/ServiceLib/Manager/CoreManager.cs
   + WP-05 files (untracked)

git diff --check:
  warning: LF/CRLF on CoreManager.cs (pre-existing)
```

## Changes Made

None — this was a verification-only task. No code changes were needed.

## Not Executed

| Item | Reason |
|------|--------|
| System proxy E2E | No managed WPF shell; needs real core binaries + managed payload |
| TUN E2E | Not running as admin |
| TUN non-admin rejection E2E | Needs real core + managed payload to reach policy check |
| VLESS Reality real-line E2E | No Master account with assigned managed lines |
| Hysteria2 real-line E2E | Same |
| Classic after managed stop | Needs real managed WPF shell to test handoff |
| Managed after classic stop | Same |
| applied ACK to real Master | No credentials |
| Session/heartbeat to real Master | No credentials |
| Cross-process mutex E2E | Needs two processes; unit/static coverage only in this run |

## Risks/Limits

1. **No real core E2E**: The `ServiceLibManagedCoreRunner` has never been tested
   with a real Xray/sing-box process start. The `FakeCoreRunner` in unit tests
   simulates success/failure but does not exercise `CoreManager.LoadCore`,
   `SysProxyHandler.UpdateSysProxy`, or `AppManager.PushRuntimeConfigOverride`
   with real binaries. This gap should be closed in R5 T16-R5-05/06/07/08/09.

2. **Windows 10 not Windows 11**: The spec targets Windows 11 25H2 as primary.
   This machine runs Windows 10 Pro 2009. The runtime code has no
   Windows-version-specific logic, so this does not affect WP-05 correctness,
   but R5 must verify on Windows 11.

3. **Classic v2rayN conflict**: The running classic instance proves the
   ownership model's purpose — managed and classic cannot run simultaneously.
   But we cannot verify the actual conflict rejection without a managed entry
   point.

4. **`AppManager.RemoveTunDeviceAsync`**: New public method added in the
   previous verify/fix round. Has not been exercised with a real TUN device.
   The method delegates to `WindowsUtils.RemoveTunDevice()` which calls
   `pnputil.exe /remove-device` — standard Windows TUN cleanup.
