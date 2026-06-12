# R0-F01 Acceptance Review v2

> Reviewer: Independent verifier (not implementer)
> Date: 2026-06-13
> Branch: codex/plan16
> Verdict: **accepted** ✅

---

## Scope

Re-review F-01 fix after v1 `changes_requested` verdict. The previous review found that all
F-01 tests were expression-copy tests (copying the guard boolean into the test without calling
production code). Required fix: at least one test must call `CheckHasUpdateOnlyAll()` directly.

---

## Verification Results

| Command | Result |
|---------|--------|
| `git diff --check` | ✅ CRLF warnings only, no whitespace errors |
| `dotnet test ServiceLib.Tests --filter ManagedUpdateTests` | ✅ 12/12 passed, 0 failed |
| `dotnet test NetAccel.Managed.Tests` | ✅ 11/11 passed, 0 failed |
| `dotnet build v2rayN.csproj -c Debug` | ✅ 0 warnings, 0 errors |
| Cross-dependency check (ServiceLib → WPF/Managed refs) | ✅ clean — no foreign references |

---

## Previous P2 Finding: Expression-Copy Tests → RESOLVED

### v1 Finding (R0-F01-acceptance.md lines 70-137)

> F-01 tests copy the guard boolean expression into the test without instantiating `UpdateService`
> or calling `CheckHasUpdateOnlyAll()`. Removing the guard from production would not break tests.

### v2 Resolution

**New integration test:** `F01_CheckHasUpdateOnlyAll_NetAccel_SkipsV2rayN` (lines 89-113)

```csharp
var config = new Config
{
    CheckUpdateItem = new CheckUpdateItem
    {
        SelectedCoreTypes = [nameof(ECoreType.v2rayN)]
    }
};
var updateService = new UpdateService(config, (_, _) => Task.CompletedTask);
var msgs = await updateService.CheckHasUpdateOnlyAll(preRelease: false);
msgs.Should().BeEmpty(...);
```

✅ This test instantiates `UpdateService` with a real `Config` and calls the production
`CheckHasUpdateOnlyAll()` method directly.

✅ Config selects **only** `ECoreType.v2rayN` — if the guard were removed, the loop would
attempt a network call and the test would fail (non-empty result or exception).

✅ Uses `NetAccelIdentity.Configure(CreateNetAccel())` to set the identity, and cleans up
with `ResetForTests()` in a `finally` block.

✅ The `Func<bool, string, Task>` callback is a no-op, proving the method works without UI.

**Old expression-copy test removed:** `F01_GuardCondition_NetAccel_ShouldSkipV2rayN` is gone.
The remaining two expression-copy tests (`Upstream_ShouldNotSkipV2rayN`,
`NetAccel_MustNotSkipOtherCoreTypes`) are supplementary — they verify boundary conditions
but are no longer the only F-01 coverage.

**Verdict on P2: FIXED.** The integration test exercises the production code path.

---

## Guard Placement Review (unchanged from v1)

**File:** `v2rayN/ServiceLib/Services/UpdateService.cs:127-132`

```csharp
if (type == ECoreType.v2rayN && NetAccelIdentity.Active.DisableUpstreamAppUpdate)
{
    continue;
}
```

✅ Guard is in `CheckHasUpdateOnlyAll()`, the background auto-check method.

✅ Placed before `SelectedCoreTypes` filter (line 134) and before network call (line 139).

✅ Mirrors existing patterns: `CheckUpdateGuiN()` (line 13) and `CheckUpdateViewModel` (3 blocks).

✅ Only `ECoreType.v2rayN` matched — Xray, sing-box, mihomo, v2fly unaffected.

✅ `DisableUpstreamAppUpdate` defaults to `false` — upstream v2rayN identity preserved.

---

## Cross-Dependency Check

| Boundary | Status |
|----------|--------|
| `ServiceLib/Common/NetAccelIdentity.cs` → WPF refs | ✅ none |
| `ServiceLib/Services/UpdateService.cs` → WPF/Managed refs | ✅ none |
| `ServiceLib/ViewModels/CheckUpdateViewModel.cs` → WPF refs | ✅ none |
| `NetAccelIdentity` uses only `System` + `ServiceLib` namespaces | ✅ clean |

---

## Test Summary

| Test File | Total | Pass | Notes |
|-----------|-------|------|-------|
| `ServiceLib.Tests/ManagedUpdateTests.cs` | 12 | 12 | 4 F-01 tests (1 integration + 3 condition), 8 identity/UI tests |
| `NetAccel.Managed.Tests/` | 11 | 11 | runtime config, isolation, assembly boundary |
| **Total** | **23** | **23** | |

---

## Positive Observations

- Integration test `F01_CheckHasUpdateOnlyAll_NetAccel_SkipsV2rayN` is a genuine regression
  test — deleting the guard breaks it.
- 3-layer blocking pattern (service → background → UI) is consistent.
- Test collection `"NetAccelIdentity"` with `DisableParallelization = true` correctly
  serializes tests that mutate the process-wide identity.
- All 23 tests pass. No regressions. Build clean.

---

## Conclusion

Previous P2 finding (expression-copy tests) is **resolved** with a proper integration test
that calls `CheckHasUpdateOnlyAll()` directly. Guard code, test coverage, build, and
cross-dependency boundaries all verified. Verdict: **accepted** ✅.
