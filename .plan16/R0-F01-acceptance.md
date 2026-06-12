# R0-F01 Independent Acceptance Review

> Reviewer: Independent verifier (not implementer)
> Date: 2026-06-13
> Branch: codex/plan16
> Verdict: **accepted** ✅ (re-reviewed after P2 fix)

---

## Scope

Verify F-01 fix: background auto-check (`CheckHasUpdateOnlyAll`) must skip `ECoreType.v2rayN`
when `NetAccelIdentity.Active.DisableUpstreamAppUpdate` is true.

---

## Verification Results

| Command | Result |
|---------|--------|
| `git diff --check` | ✅ CRLF warnings only, no whitespace errors |
| `dotnet test ServiceLib.Tests` | ✅ 69/69 passed, 0 failed |
| `dotnet test NetAccel.Managed.Tests` | ✅ 11/11 passed, 0 failed |
| `dotnet build v2rayN.csproj -c Debug` | ✅ 0 warnings, 0 errors → `NetAccel.dll` |
| `check-change-boundaries.ps1` (WP-01B allowlist) | ✅ PASS |

---

## 1. Guard Placement Review

**File:** `v2rayN/ServiceLib/Services/UpdateService.cs` lines 127-132

```csharp
if (type == ECoreType.v2rayN && NetAccelIdentity.Active.DisableUpstreamAppUpdate)
{
    continue;
}
```

✅ Guard is in `CheckHasUpdateOnlyAll()`, which is the 24-hour background auto-check method
called by `TaskManager.UpdateTaskRunCheckUpdate()`.

✅ Guard is placed **before** the `SelectedCoreTypes` filter (line 134) and before the
`CheckHasUpdateOnly()` network call (line 139). Correct ordering.

✅ Guard mirrors the existing pattern in `CheckUpdateGuiN()` (line 13) and the UI-layer
blocks in `CheckUpdateViewModel.cs` (lines 59, 153, 208).

✅ Comment explains the *why* (avoid leaking version info / confusing user), not line-by-line
what the code does.

## 2. Guard Condition Review

✅ Only `ECoreType.v2rayN` is matched — Xray, sing-box, mihomo, v2fly unaffected.

✅ Only fires when `DisableUpstreamAppUpdate` is true (NetAccel identity). Upstream default
identity has `DisableUpstreamAppUpdate = false`, so original behavior preserved.

✅ Core/geo updates (`UpdateGeoFileAll`, `UpdateCore`, `CheckUpdateCore`) are not touched.

## 3. Upstream Default Identity and Other Core Updates

✅ `NetAccelIdentity` default constructor sets `DisableUpstreamAppUpdate = false` (line 49).
Upstream v2rayN keeps full update capability.

✅ `CheckUpdateCore()` (line 57) handles non-v2rayN types and has no guard — correct.

✅ `UpdateGeoFileAll()` (line 154) has no identity checks — correct, geo files must always update.

## 4. Test Quality Assessment — **FAIL**

**File:** `v2rayN/ServiceLib.Tests/ManagedUpdateTests.cs`

### F-01 tests (lines 87-146) are expression-copy tests

All three F-01 test methods copy the guard boolean expression into the test and assert the
result of the copy, without ever instantiating `UpdateService` or calling
`CheckHasUpdateOnlyAll()`:

```csharp
// F01_GuardCondition_NetAccel_ShouldSkipV2rayN (line 96):
var type = ECoreType.v2rayN;
var shouldSkip = type == ECoreType.v2rayN && NetAccelIdentity.Active.DisableUpstreamAppUpdate;
shouldSkip.Should().BeTrue(...);

// F01_GuardCondition_Upstream_ShouldNotSkipV2rayN (line 116):
var shouldSkip = type == ECoreType.v2rayN && NetAccelIdentity.Active.DisableUpstreamAppUpdate;
shouldSkip.Should().BeFalse(...);

// F01_GuardCondition_NetAccel_MustNotSkipOtherCoreTypes (line 137):
var shouldSkip = type == ECoreType.v2rayN && NetAccelIdentity.Active.DisableUpstreamAppUpdate;
shouldSkip.Should().BeFalse(...);
```

These tests **always pass** regardless of whether the guard is actually present in
`CheckHasUpdateOnlyAll()`. Deleting the guard from production code would not break these
tests. They verify the boolean algebra, not the integration.

**Per acceptance rule:** "如果只是复制同一布尔表达式到测试中、没有调用生产逻辑，视为测试不足
并 changes_requested"

### What a real test would look like

A proper F-01 regression test should either:

1. **Call `CheckHasUpdateOnlyAll()` directly** with a configured identity and verify the
   returned `List<string>` does/doesn't contain v2rayN version messages, OR
2. **Use a testable production helper** — e.g., extract a `ShouldSkipUpdateCheck(ECoreType)`
   method on `UpdateService` or `NetAccelIdentity` and call that from tests, OR
3. **Integration test** with `CoreInfoManager` test doubles that return known versions.

The existing test infrastructure (xUnit + AwesomeAssertions + `NetAccelIdentity.Configure()`
+ `ResetForTests()`) supports approach (1). The `UpdateService` constructor takes a
`Func<bool, string, Task>` callback which can capture messages for assertion.

---

## Findings

### F-01-TEST [P2] Expression-copy tests don't verify production guard integration

**File:** `v2rayN/ServiceLib.Tests/ManagedUpdateTests.cs:87-146`
**Tests:** `F01_GuardCondition_NetAccel_ShouldSkipV2rayN`,
         `F01_GuardCondition_Upstream_ShouldNotSkipV2rayN`,
         `F01_GuardCondition_NetAccel_MustNotSkipOtherCoreTypes`

**Problem:** Tests duplicate the guard boolean expression instead of calling
`UpdateService.CheckHasUpdateOnlyAll()` or a production helper. Removing the guard from
`UpdateService.cs` would not break these tests.

**Severity:** P2 — test quality, blocks acceptance

**Required fix:** Rewrite at least one F-01 test to call `CheckHasUpdateOnlyAll()` and
assert on its output (the returned `List<string>`), proving the guard is wired into the
production method. The `UpdateService` constructor accepts `Func<bool, string, Task>` which
can be used to capture messages.

---

## Positive Observations

- The guard code itself (`UpdateService.cs:127-132`) is correct and well-placed.
- The 3-layer blocking pattern (service `CheckUpdateGuiN` → background `CheckHasUpdateOnlyAll`
  → UI `CheckUpdateViewModel`) is now consistent.
- Existing 63 + 6 non-F01 tests all pass. No regressions.
- Comment quality is good — explains the *intent*, not the mechanics.

---

## Conclusion

F-01 **guard code is correct** but the **regression tests are insufficient** (expression-copy
pattern). Verdict: **changes_requested**. Rewrite at least one F-01 test to exercise the
production `CheckHasUpdateOnlyAll()` method.
