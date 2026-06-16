# WP-04 Result Report

## Status

`accepted` - WP-04 implementation passed local Codex acceptance review.

## Scope

Implemented against Plan 16 WP-04:

| Task | Description | Status | Evidence |
|------|-------------|--------|----------|
| T16-R2-01 | `ProfileSource` and source operation policy | code_complete | `NetAccel.Managed/Domain/ProfileSourcePolicy.cs`; managed profiles allow connect/select/diagnose and reject edit/delete/copy/share/export/backup. |
| T16-R2-02 | Strong typed managed profile model | code_complete | `ManagedConfigPayload` parses VLESS Reality and Hysteria2 credentials/security into typed accessors while preserving compatibility with existing callers. |
| T16-R2-03 | `ManagedProfileValidator` | code_complete | `ManagedProfileValidator` validates schema-aligned host/port/revision/transport/security/credential/capability constraints and delegates final node compatibility to upstream `NodeValidator`. |
| T16-R2-07 | VLESS Reality adapter | code_complete | `ManagedProfileAdapter.ToProfileItem` creates short-lived VLESS Reality `ProfileItem` for Xray without `Subid`. |
| T16-R2-08 | Hysteria2 adapter | code_complete | `ManagedProfileAdapter.ToProfileItem` creates short-lived Hysteria2 `ProfileItem` for sing-box without `Subid`. |
| T16-R2-09 | Core capability selection | code_complete | `ManagedProfileAdapter.SelectCore` maps VLESS to Xray and Hysteria2 to sing-box, rejecting mismatches. |
| T16-R2-09A | Managed runtime builder | code_complete | `ManagedRuntimeConfigBuilder` builds `ManagedRuntimeConfig` from managed payload DNS/routing/client policy and fixed safe defaults. |

## Files Changed

- `NetAccel.Managed/Dto/ManagedConfigPayload.cs`
- `NetAccel.Managed/Domain/ProfileSourcePolicy.cs`
- `NetAccel.Managed/Domain/ManagedProfileValidator.cs`
- `NetAccel.Managed/Domain/ManagedProfileAdapter.cs`
- `NetAccel.Managed/Runtime/ManagedRuntimeConfigBuilder.cs`
- `NetAccel.Managed.Tests/ManagedProfileDomainTests.cs`

## Contract Alignment

- Tests read Master contract fixtures from
  `D:\AI_code\NetAccel\contracts\fixtures\managed-config-payload-vless.json`
  and
  `D:\AI_code\NetAccel\contracts\fixtures\managed-config-payload-hysteria2.json`.
- Validator is aligned with
  `D:\AI_code\NetAccel\contracts\managed-config-payload.schema.json` for:
  protocol, core preference, transport, revision lower bound, endpoint port,
  VLESS flow, Reality fingerprint enum, Reality short ID length, and required
  credentials/security fields.

## Acceptance Review

- `available=false` is treated as assignment/runtime state rather than invalid
  configuration. The validator accepts structurally valid unavailable profiles,
  automatic runtime selection skips them, and manual profile selection rejects
  them with a stable not-available failure.
- `core_preference`, VLESS `flow`, Reality `fingerprint`, and transport values
  are constrained to the frozen `managed-config/v1` schema enums.
- `routing_policy` and `dns_policy` retain compatibility `object?` storage for
  existing WP-03 callers, with typed accessors used by WP-04 runtime code.

## Boundary Checks

- `NetAccel.Managed` does not reference classic state loaders:
  `AppManager`, `SQLiteHelper`, `ConfigHandler`, `CoreConfigContextBuilder`,
  `AddBatchServers`, or `SubItem`.
- Managed profiles are converted only to short-lived `ProfileItem` instances.
- Managed profile `Subid` is empty.
- Managed runtime `AllProxiesMap` is empty.
- Classic `AppManager` config mutation does not change generated managed Xray
  config output.

## Verification

```powershell
dotnet test .\NetAccel.Managed.Tests\NetAccel.Managed.Tests.csproj
# Passed: 102/102

dotnet test .\v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj
# Passed: 69/69

dotnet build .\v2rayN\v2rayN\v2rayN.csproj -c Debug
# Build succeeded: 0 warnings, 0 errors

git diff --check
# Passed with LF/CRLF warning only
```

## Known Limitations

- This is not WP-05. It does not start cores, claim connection ownership,
  write system proxy, enable TUN, or perform real-line E2E.
- Stable `managed-envelope/v1` remains WP-10/R2 crypto work and is not
  implemented here.
- `routing_policy` and `dns_policy` retain `object?` storage for compatibility
  with existing WP-03 tests, with typed accessors used by WP-04 runtime code.
