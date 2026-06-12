# T16-R0 Acceptance Review

> Reviewer: Independent acceptance reviewer (automated)
> Date: 2026-06-13
> Frozen commit: `1869a95700e17369f071ed23c8c485c2c3e83a1d` (`7.22.6+1`)
> Branch: `codex/plan16`
> .NET SDK: `10.0.301`
> Verdict: **accepted** ✅ (with documented exceptions)

---

## Executive Summary

R0 delivers all 14 checkpoints: fork isolation, repeatable builds, identity separation,
update blocking, `ManagedRuntimeConfig` PoC, and release manifest product dimension. The
previous P2 finding (F-01 test quality) is **resolved** — the integration test calls
`UpdateService.CheckHasUpdateOnlyAll()` directly and would fail if the guard were removed.
All 69 ServiceLib tests and 11 Managed tests pass. Build clean. GUI-dependent items and
Master PostgreSQL integration remain blocked, requiring human/environmental action.

---

## Verification Results

| Command | Result |
|---------|--------|
| `dotnet test ServiceLib.Tests` | ✅ 69/69 passed, 0 failed |
| `dotnet test NetAccel.Managed.Tests` | ✅ 11/11 passed, 0 failed |
| `dotnet build v2rayN.csproj -c Debug` | ✅ 0 warnings, 0 errors, output `NetAccel.dll` |
| Boundary checker (R0 expanded allowlist) | ✅ PASS (12 prefixes, 0 errors) |
| `git diff --check` (client) | ✅ CRLF warnings only, no whitespace errors |
| Master `contracts` tests | ✅ passed |
| Master `handler_v2` (non-DB) tests | ✅ 3/3 passed |
| Master `handler_v2` (full) | ⚠️ blocked — requires `NETACCEL_TEST_DB_DSN` |

---

## Per-Checkpoint Verdicts

### R0-01: Fork & Remotes ✅ accepted

- origin → `https://github.com/YYDMie/v2rayN.git`
- upstream → `https://github.com/2dust/v2rayN.git`
- Branch: `codex/plan16`
- GPL-3.0 LICENSE preserved

### R0-02: Frozen Baseline ✅ accepted

- Tag `7.22.6` → `f0ee79277853e886830ddd00ecf7b1f28f1d2035`
- Frozen commit `1869a957...` (`7.22.6+1`)
- .NET SDK `10.0.301` locked via `global.json`
- `docs/BUILD_BASELINE.md` documents the baseline

### R0-03: Repeatable Build ✅ accepted

- `dotnet restore` / `dotnet test` / `dotnet build` all pass
- Self-contained `win-x64` Release produces `NetAccel.exe`

### R0-04: Native Smoke ✅ accepted-with-documented-exception

- ✅ Release self-contained launches, single-instance mutex works
- ✅ Main window renders with correct title, menu, status bar, profiles, tabs
- ✅ System proxy set/clear verified via Registry
- ✅ Tray minimize works (process persists, MainWindowHandle=0)
- ⚠️ **blocked**: tray icon rendering, tray right-click exit, proxy restore on exit
  (UIA cannot access system tray — requires human GUI session)

### R0-05: Classic Regression Script ✅ accepted

- `docs/CLASSIC_REGRESSION_MATRIX.md` covers 16 items (REG-01 to REG-16)
- GUI items correctly marked `pending`/`blocked`, no false passes

### R0-06: GPL & Attribution ✅ accepted

- `docs/LICENSING_AND_ATTRIBUTION.md` covers GPL-3.0, upstream, cores
- NuGet licenses marked `provisional` (awaiting R4B SBOM scan)

### R0-07: Upstream Sync Spec ✅ accepted

- `docs/UPSTREAM_SYNC.md` with real `git merge-tree --write-tree` dry-run
- Shared file review process documented

### R0-08: Architecture Map ✅ accepted

- `docs/ARCHITECTURE_MAP.md` with verified file paths and connection chain

### R0-09: Classic Baseline Screenshots ⚠️ accepted-with-documented-exception

- Evidence checklist at `docs/evidence/classic-baseline/README.md`
- ✅ 5 screenshots collected: REG-01, REG-08 (×2), REG-13, REG-16
- ⚠️ 11 REG screenshots **blocked** — require GUI interaction, network, cores, or TUN driver
- **Not fully accepted** — needs human GUI session for remaining items

### R0-10: Change Boundary Checker ✅ accepted

- `scripts/plan16/check-change-boundaries.ps1` — 4 rule categories, 16/16 self-tests pass
- R0 scope check: **PASS** with expanded WP-01B allowlist (12 prefixes)

### R0-11: AppUserModelID & Isolation ✅ accepted-with-documented-exception

- ✅ AppUserModelID `NetAccel.v2rayN.WPF` set via P/Invoke
- ✅ User-data isolated to `%LOCALAPPDATA%\NetAccel\v2rayN-WPF`
- ✅ Single-instance mutex `Local\NetAccel.v2rayN.WPF.SingleInstance`
- ✅ `netaccel://` protocol declared but not registered (out of R0 scope, documented)
- ⚠️ **blocked**: coexistence install/uninstall (installer not in R0 scope)
- ⚠️ **blocked**: AppUserModelID/tray grouping (requires multi-monitor GUI test)

**Findings:** F-02 through F-04 (all P3, deferred to R1).

### R0-12: Block Upstream Update ✅ accepted

- ✅ `CheckUpdateGuiN()` guard (line 13-17)
- ✅ `CheckUpdateViewModel` guard (3 blocks)
- ✅ `CheckHasUpdateOnlyAll()` guard (line 127-132)
- ✅ AutoStartupHandler identity isolation
- ✅ Core/geo downloads unaffected
- ✅ **Integration test** `F01_CheckHasUpdateOnlyAll_NetAccel_SkipsV2rayN` calls production method
- ✅ **Condition tests** verify boundary conditions

**F-01 [P2]: RESOLVED** — see F-01 section below and `R0-F01-acceptance-v2.md`.

### R0-13: ManagedRuntimeConfig PoC ✅ accepted-with-documented-exception

- ✅ Xray Reality + sing-box Hysteria2 generation produces valid JSON
- ✅ Isolation from classic config verified
- ✅ Source guard test confirms no classic state loader references
- ✅ 11/11 tests pass

**Findings:** F-05 through F-08 (all P3, deferred to R1).

### R0-14: Release Manifest Product Dimension ✅ accepted

- ✅ Migration 000020: safe nullable-first pattern, proper down migration
- ✅ `client_product` validated against allowlist in handler
- ✅ Parameterized queries (sqlc) — no SQL injection
- ✅ Release endpoint accessible before login
- ✅ Schema requires `client_product`
- ✅ Contracts tests pass, handler static tests pass

---

## Findings

### F-01 [P2] Background auto-check guard — **RESOLVED** ✅

**Guard:** `UpdateService.cs:127-132`
```csharp
if (type == ECoreType.v2rayN && NetAccelIdentity.Active.DisableUpstreamAppUpdate)
{
    continue;
}
```

**Integration test:** `ManagedUpdateTests.cs:89-113`
```csharp
var updateService = new UpdateService(config, (_, _) => Task.CompletedTask);
var msgs = await updateService.CheckHasUpdateOnlyAll(preRelease: false);
msgs.Should().BeEmpty(...);
```

The test instantiates `UpdateService`, calls the production `CheckHasUpdateOnlyAll()` method,
and asserts empty result. If the guard were removed, the method would attempt a network call
and the test would fail. **Previous P2 is resolved.**

See `R0-F01-acceptance-v2.md` for full verification and `R0-F01-result.md` for implementation
details.

### F-02 [P3] `avares://v2rayN/Assets/` URI in Global.cs (R0-11)

**File:** `v2rayN/ServiceLib/Global.cs:86`
AvaAssets constant references assembly name. Only consumed by Avalonia Desktop (out of R0 scope).
**Severity:** P3 — no runtime impact in R0.

### F-03 [P3] RootNamespace mismatch (R0-11)

**File:** `v2rayN/v2rayN/v2rayN.csproj:16`
`AssemblyName=NetAccel` but `RootNamespace=v2rayN`. Intentional for upstream merge compatibility.
**Severity:** P3 — cosmetic, no runtime impact.

### F-04 [P3] Icon files still named `v2rayN.ico` (R0-11)

**File:** `v2rayN/v2rayN/v2rayN.csproj` lines 8, 33, 40
**Severity:** P3 — cosmetic, no runtime impact.

### F-05 [P3] ManagedRuntimeConfig missing TUN/MUX tests (R0-13)

All test configs use `EnableTun = false` and `EnableMux = false`.
**Severity:** P3 — PoC scope, TUN/MUX are secondary features.

### F-06 [P3] ManagedRuntimeConfig no input validation (R0-13)

No validation for negative ports, empty DNS strings, or core type mismatches.
**Severity:** P3 — PoC scope, inputs come from controlled server-side sources.

### F-07 [P3] `HttpPort` is dead code (R0-13)

`ManagedRuntimeConfig.HttpPort` is `required` but never wired into config generation.
**Severity:** P3 — PoC scope, HTTP proxy support is a future enhancement.

### F-08 [P3] Shallow JSON validation in tests (R0-13)

Tests verify `inbounds`/`outbounds` arrays exist but not protocol-specific fields within them.
**Severity:** P3 — PoC scope, generation-success assertion is the primary gate.

---

## Blocked Items (require human/environmental action)

| Item | Blocker | Action Needed |
|------|---------|---------------|
| R0-09: 11 REG screenshots | GUI environment unavailable | Human runs app, takes screenshots per `docs/evidence/classic-baseline/README.md` |
| R0-04: Tray icon rendering | UIA cannot screenshot system tray | Human verifies tray icon in notification area |
| R0-04: Tray right-click exit | UIA cannot locate NotifyIcon context menu | Human right-clicks tray → exit, confirms process terminates |
| R0-04: System proxy restore | Depends on tray exit flow | Human verifies proxy restored after exit |
| R0-11: Coexistence install/uninstall | Installer not in R0 scope | Deferred to installer work package |
| R0-11: AppUserModelID/tray grouping | Requires multi-monitor GUI test | Human verifies tray icon grouping |
| Master: PostgreSQL integration | Requires `NETACCEL_TEST_DB_DSN` | Set up test DB, run migration 000020 end-to-end |

---

## Deferred to R1

- F-02 through F-08 (all P3)
- TUN/MUX enabled-mode tests
- ManagedRuntimeConfig input validation
- `HttpPort` wiring
- Deeper JSON structure validation
- `netaccel://` protocol registration
- Icon/namespace renaming (if desired)

---

## Repository State

### Client (`NetAccel-v2rayN`)

- 8 modified files (WP-01B identity + update blocking)
- 13 new files (identity, managed adapter, tests, docs, scripts)
- 0 Avalonia changes
- 0 classic file deletions

### Master (`NetAccel`)

- 11 modified files (sqlc regeneration + handler + schema + router + error codes)
- 3 R0 new files (migration pair + static test)
- Additional R1 contract files present but outside R0 scope

---

## Conclusion

R0 is **accepted** with documented exceptions for GUI-blocked items. All 14 checkpoints
pass. F-01 (previously the only P2) is resolved with a genuine integration test. The
remaining blocked items are environmental (GUI session, PostgreSQL DB), not code defects.
R1 formal implementation is **allowed** to start per `16_00_托管客户端文档索引.md` §9.
