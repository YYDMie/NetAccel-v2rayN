# R0-F01 Result: Background auto-check v2rayN skip for NetAccel

> Status: code_complete (F-01 acceptance finding resolved)
> Date: 2026-06-13
> Finding: F-01 [P2] from R0-acceptance.md
> Branch: codex/plan16

## Summary

Fixed `CheckHasUpdateOnlyAll()` in `UpdateService.cs` to skip `ECoreType.v2rayN` when
`NetAccelIdentity.Active.DisableUpstreamAppUpdate` is true. The 24-hour background auto-check
no longer queries GitHub for upstream v2rayN version info or publishes a notification under
NetAccel identity. Xray, sing-box, mihomo, v2fly and other core/geo updates remain unaffected.

## Modified Files

| File | Change |
|------|--------|
| `v2rayN/ServiceLib/Services/UpdateService.cs` | Added guard at line 127-132 in `CheckHasUpdateOnlyAll()` to skip `ECoreType.v2rayN` when `DisableUpstreamAppUpdate` is active |
| `v2rayN/ServiceLib.Tests/ManagedUpdateTests.cs` | Rewrote F-01 regression tests: replaced expression-copy test with integration test that calls `CheckHasUpdateOnlyAll()` |

## Code Change

```csharp
// In CheckHasUpdateOnlyAll(), before SelectedCoreTypes filter:
if (type == ECoreType.v2rayN && NetAccelIdentity.Active.DisableUpstreamAppUpdate)
{
    continue;
}
```

This mirrors the existing guard pattern in `CheckUpdateGuiN()` (line 13-17) and the UI-layer
block in `CheckUpdateViewModel.cs`. Placing it before the `SelectedCoreTypes` check ensures
the v2rayN type is skipped even if the user's config includes it.

## New Regression Tests

| Test | Purpose |
|------|---------|
| `F01_CheckHasUpdateOnlyAll_NetAccel_SkipsV2rayN` | **Integration test**: calls `UpdateService.CheckHasUpdateOnlyAll()` with SelectedCoreTypes=v2rayN under NetAccel identity, asserts empty result (guard skips v2rayN) |
| `F01_GuardCondition_Upstream_ShouldNotSkipV2rayN` | Verifies upstream (default) identity does NOT skip v2rayN |
| `F01_GuardCondition_NetAccel_MustNotSkipOtherCoreTypes` [Theory: Xray, sing_box, mihomo, v2fly] | Verifies the guard does NOT affect non-v2rayN core types under NetAccel identity |

Total: 6 test cases (1 Fact integration + 1 Fact + 4 Theory rows). Existing 6 tests unchanged. Expected total: 69.

## Verification

| Command | Result |
|---------|--------|
| `git diff --check` | ✅ CRLF warnings only, no whitespace errors |
| `dotnet build ServiceLib.Tests -c Debug` | ✅ 0 warnings, 0 errors |
| `dotnet test ServiceLib.Tests -c Debug` | ✅ 69/69 passed (including new integration test) |
| `dotnet test NetAccel.Managed.Tests -c Debug` | ✅ 11/11 passed |
| `dotnet build v2rayN.csproj -c Debug` (WPF) | ✅ 0 warnings, 0 errors |

**Integration test verification:** The new `F01_CheckHasUpdateOnlyAll_NetAccel_SkipsV2rayN`
test calls the real `UpdateService.CheckHasUpdateOnlyAll()` method with `SelectedCoreTypes = ["v2rayN"]`
under NetAccel identity. If the guard were removed, the method would proceed to
`CheckHasUpdateOnly(ECoreType.v2rayN)` which makes an HTTP request — the test would either
return a non-empty list or throw, both failures. This proves the guard is wired into production code.

## Risk Assessment

- **Regression risk:** Minimal. The guard is a single `continue` statement placed before any
  network call. It only fires when BOTH conditions are true: type is `v2rayN` AND the active
  identity disables upstream updates. Upstream v2rayN identity has `DisableUpstreamAppUpdate = false`,
  so original behavior is preserved.
- **Scope:** Only `CheckHasUpdateOnlyAll()` is affected. `CheckUpdateGuiN()` already had the guard.
  `CheckUpdateCore()` and `CheckHasUpdateOnly()` remain unchanged (they don't handle v2rayN type).
- **Other cores:** Xray, sing-box, mihomo, v2fly, geo files, and SRS updates are completely
  unaffected — the guard only matches `ECoreType.v2rayN`.

## Git State

```
 M v2rayN/ServiceLib/Services/UpdateService.cs      (modified — +7 lines for F-01 guard)
?? v2rayN/ServiceLib.Tests/ManagedUpdateTests.cs     (new — F-01 regression tests added to existing file)
```

No commits created, no pushes performed.
