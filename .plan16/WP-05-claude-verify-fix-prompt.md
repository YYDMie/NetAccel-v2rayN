# WP-05 Claude Code Task - Verify/Fix Managed Runtime

You are continuing NetAccel Plan 16 WP-05. Work only on:

- T16-R2-10 `ConnectionOwnershipCoordinator`
- T16-R2-11 `ManagedConnectionCoordinator`
- T16-R2-12 managed system-proxy connection
- T16-R2-13 managed TUN connection
- T16-R2-14 selection inside the authorized assignment set
- T16-R2-15 safe switching and automatic failover

Do not enter WP-06, UI/R3, tray work, classic-mode UI cleanup, edit/copy/share/
export/backup guards, Plan 17, deployment, or any commercial package, paid
subscription, billing, sales, traffic quota, airport/proxy-service, public node
pool, or NetAccel subscription-link logic.

## Repository And Branch

Client repository:

```text
D:\AI\Claude code\NetAccel-v2rayN
```

Current branch:

```text
codex/plan16-wp05-runtime
```

Master repository for read-only reference:

```text
D:\AI\Claude code\NetAccel
```

Historical notes may mention `D:\AI_code\...`; that path does not exist on
this machine. Use `D:\AI\Claude code\...`.

## Project Positioning

NetAccel is a personal learning demo for studying managed acceleration clients,
line operations, agent communication, admin control, and client runtime
behavior. It is not a commercial proxy service, airport, paid subscription
service, package sales platform, traffic resale system, or public node pool.

Use the game-accelerator style controlled assignment model:

- The admin/server assigns the allowed acceleration line set, the recommended
  default, fallback order, and policy.
- The device uses the server-issued managed configuration as the only source of
  truth for managed lines.
- The device may use automatic selection or manually select a line only inside
  the assigned set.
- Managed line parameters are read-only and must not be edited, shared,
  exported, converted into classic local profiles, or exposed as subscription
  links.

Plan 16 compatibility policy:

- Phase 1 intentionally retains v2rayN's native local-node, generic
  subscription, and import/export capabilities as a compatibility and testing
  lane.
- These classic capabilities are not the NetAccel managed default path.
- Classic local nodes/subscriptions/import/export must remain separate from
  managed lines and managed storage.
- Do not remove or break those classic compatibility paths in WP-05.
- Hiding, disabling, or removing them should be considered only after the
  managed path is mature and stable in a later plan.

Project terms:

- `Server` means one VPS acceleration line.
- `Plan` means a technical acceleration configuration, not a package, paid
  plan, subscription tier, or sales product.
- `Device` means the endpoint using managed acceleration.

## Local Environment

The repository `global.json` requires:

```json
{
  "sdk": {
    "version": "10.0.301",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

Do not modify `global.json`.

First verify whether .NET SDK 10.0.301 or a compatible latest patch is already
available. If it is not available, you may install it into a user-level path
such as:

```text
%USERPROFILE%\.dotnet-codex-sdk-10
```

Do not write SDK files into the repository. If SDK setup cannot be completed in
reasonable time, report:

```text
blocked: missing .NET SDK 10.0.301
```

In that case, still complete static review and do not claim tests passed.

## Read First

Master repository:

- `AGENTS.md`
- `docs/16_00_*`
- `docs/16_01_*`
- `docs/16_02_*`
- `docs/16_04_*`
- `docs/16_06_*`

Client repository:

- `.plan16/WP-05-codex-prompt.md`
- `.plan16/WP-05-result.md`
- `NetAccel.Managed/Runtime/ConnectionOwnershipCoordinator.cs`
- `NetAccel.Managed/Runtime/ManagedConnectionCoordinator.cs`
- `NetAccel.Managed.Tests/ConnectionOwnershipCoordinatorTests.cs`
- `NetAccel.Managed.Tests/ManagedConnectionCoordinatorTests.cs`
- `v2rayN/ServiceLib/Manager/AppManager.cs`
- `v2rayN/ServiceLib/Manager/CoreManager.cs`

## Task

Codex has implemented the first WP-05 pass. Your task is to verify, fix, and
bring WP-05 to `code_complete` for Codex acceptance.

Required:

1. Resolve or clearly block on .NET SDK 10.0.301.
2. Build and test the current WP-05 code.
3. Fix compile errors, test failures, and clear behavior gaps.
4. Stay inside WP-05 scope.
5. Do not modify `global.json`.
6. Do not commit or push.

## Required Commands

If .NET 10 is available, run:

```powershell
dotnet build .\NetAccel.Managed\NetAccel.Managed.csproj -c Debug
dotnet build .\v2rayN\v2rayN\v2rayN.csproj -c Debug
dotnet test .\NetAccel.Managed.Tests\NetAccel.Managed.Tests.csproj --filter "FullyQualifiedName~ConnectionOwnershipCoordinatorTests|FullyQualifiedName~ManagedConnectionCoordinatorTests"
dotnet test .\v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj
git diff --check
git status --short --branch
```

If using a user-level SDK path, report the exact command, for example:

```powershell
& "$env:USERPROFILE\.dotnet-codex-sdk-10\dotnet.exe" build .\NetAccel.Managed\NetAccel.Managed.csproj -c Debug
```

## Required Behavior Coverage

Static review or automated tests must cover:

- Only one owner can acquire connection ownership at a time.
- Stale owner snapshots can recover; live owner snapshots are rejected.
- Managed and Classic cannot both own connection control at the same time.
- `ManagedConnectionCoordinator` transitions through
  `Starting -> Connected -> Stopping -> Ready`.
- System-proxy mode does not enable TUN.
- TUN mode starts only when client policy and profile policy both allow it.
- Automatic selection uses only current payload assigned profiles, recommendation
  state, fallback order, availability, capability, and policy.
- Manual selection can choose only currently assigned and available profiles.
- Unassigned, unavailable, capability-incompatible, or policy-denied profiles
  must not start the core.
- Failed or cancelled manual switching must restore the previous profile and
  system network state.
- Automatic fallback can use only the authorized fallback set.
- Automatic fallback must not call selection API in a way that overwrites the
  user's manual preference.
- Successful runtime application may send `applied` ACK; ACK failure must not
  turn an already-successful local connection into a connection failure.
- All start-failure paths stop the core runner and release ownership.

## Forbidden

- Do not call classic `MainWindowViewModel.Reload()` as the managed entrypoint.
- Do not write managed profiles into SQLite `ProfileItem` or `SubItem`.
- Do not call `ConfigHandler.AddBatchServers()` for managed lines.
- Do not use subscription/import/export paths to create, publish, or mutate
  NetAccel managed lines.
- Do not remove or break v2rayN native local-node, generic subscription, or
  import/export compatibility in WP-05; keep it separate from managed runtime.
- Do not implement WP-06 edit/delete/copy/share/export/backup guards.
- Do not build final UI, tray, icons, R3 pages, or classic-window cleanup.
- Do not add package sales, paid subscription tiers, billing, payment, traffic
  quota sales, public node pools, airport/proxy-service flows, or NetAccel
  subscription link export.
- Do not hide failures or report unrun tests as passing.
- Do not modify real production servers, real account data, or deployment
  configuration.

## Existing Design Notes

- `AppManager.PushRuntimeConfigOverride(Config)` temporarily lets `CoreManager`,
  `SysProxyHandler`, and port lookup read the managed in-memory config; disposing
  the scope restores classic config behavior.
- `CoreManager.IsCoreRunning` lets the managed coordinator determine whether
  core startup succeeded and whether fallback is needed.
- `ServiceLibManagedCoreRunner` is the production runtime adapter. Unit tests
  should prefer fake runners for state-machine coverage.
- `ManagedConnectionCoordinator` must not depend on WPF and must not directly
  operate UI.
- `WindowsUtils` should remain internal. If TUN cleanup is needed outside
  ServiceLib internals, expose only a minimal public wrapper on a ServiceLib
  manager such as `AppManager`.

## Return Format

When complete, update `.plan16/WP-05-result.md` and reply with:

```text
Task: WP-05 verify/fix
Status: code_complete / blocked

Environment:
- .NET SDK:
- dotnet path:
- global.json changed: no

Changes:
- path: summary

Design decisions:
- ...

Verification:
- command -> result

Not executed:
- item: reason

Risks/limits:
- ...

Workspace:
- git diff --check:
- git status --short:
```
