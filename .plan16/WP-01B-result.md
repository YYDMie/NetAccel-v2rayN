# WP-01B Result: T16-R0-11 through T16-R0-14

> Status: code_complete
> Date: 2026-06-12
> Final local review pass: 2026-06-12 22:20 CST

## Summary

WP-01B is code-complete after local review hardening.
The WPF application identity is isolated, NetAccel defaults to its own
LocalAppData user-data directory, upstream v2rayN application update is blocked,
the root-level `NetAccel.Managed` adapter can generate Xray and sing-box JSON
from managed inputs, and the server release manifest API is product-scoped with
`client_product`.

## Client Changes

| File | Change |
|------|--------|
| `v2rayN/ServiceLib/Common/NetAccelIdentity.cs` | New identity abstraction with fixed NetAccel constants, precise `IsNetAccel`, and test reset hook |
| `v2rayN/ServiceLib/Common/WindowsIdentityHelper.cs` | New Windows AppUserModelID helper |
| `v2rayN/ServiceLib/Common/Utils.cs` | NetAccel now defaults startup/user-data path to `%LOCALAPPDATA%\\NetAccel\\v2rayN-WPF` without requiring the legacy env switch |
| `v2rayN/ServiceLib/Handler/AutoStartupHandler.cs` | Auto-start name uses the active NetAccel identity base name |
| `v2rayN/ServiceLib/Services/UpdateService.cs` | Defense-in-depth guard blocks upstream GUI app update for NetAccel |
| `v2rayN/ServiceLib/ViewModels/CheckUpdateViewModel.cs` | UI update entry blocks `ECoreType.v2rayN` when NetAccel identity is active |
| `v2rayN/v2rayN/App.xaml.cs` | Configures NetAccel identity, fixed single-instance name, and AppUserModelID during WPF startup |
| `v2rayN/v2rayN/GlobalUsings.cs` | Adds required Windows/versioning global using |
| `v2rayN/v2rayN/v2rayN.csproj` | Publishes as `NetAccel` product/assembly |
| `v2rayN/v2rayN.slnx` | Adds root-level `../NetAccel.Managed` and `../NetAccel.Managed.Tests` projects |
| `NetAccel.Managed/` | New root-level managed runtime adapter library, no WPF dependency |
| `NetAccel.Managed.Tests/` | New root-level xUnit tests with hard JSON generation checks |
| `v2rayN/ServiceLib.Tests/NetAccelIdentityTests.cs` | Identity defaults, NetAccel constants, and LocalAppData data-path tests |
| `v2rayN/ServiceLib.Tests/ManagedUpdateTests.cs` | Upstream app-update blocking and manifest contract tests |
| `v2rayN/ServiceLib.Tests/NetAccelIdentityCollection.cs` | Serializes identity tests that mutate process-wide identity |
| `scripts/plan16/check-change-boundaries.ps1` | Hardened Git output handling so stderr warnings are never treated as changed paths |

## Master Changes

| File | Change |
|------|--------|
| `migrations/000020_release_product_dimension.up.sql` | Adds `client_product`, backfills legacy rows as `netaccel-tauri`, updates unique constraint and lookup index |
| `migrations/000020_release_product_dimension.down.sql` | Rollback for product dimension |
| `sqlc/sqlc.yaml` | Includes migration 000020 in schema list |
| `sqlc/queries/client_tokens.sql` | Adds `client_product` to create/latest release queries |
| `internal/repository/client_tokens.sql.go` | Regenerated sqlc code |
| `internal/repository/models.go` | Regenerated model with `ClientProduct` |
| `internal/handler_v2/client.go` | Validates/normalizes `client_product` and returns it in release manifest response |
| `internal/handler_v2/client_test.go` | Updates normalize test for product-scoped requests |
| `internal/handler_v2/client_release_static_test.go` | New no-DB tests for migration/query/schema product isolation |
| `api/router_v2.go` | Release manifest endpoint is readable before login |
| `contracts/client-release-manifest.schema.json` | Requires `client_product` |
| `contracts/fixtures/client-release-manifest-windows.json` | Uses `netaccel-v2rayn-wpf` |
| `contracts/contract_test.go` | Checks release manifest product field |

## Verification

### Client

```powershell
dotnet test NetAccel.Managed.Tests\NetAccel.Managed.Tests.csproj --verbosity minimal
# PASS: 11/11

dotnet test v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj --no-restore --verbosity minimal
# PASS: 63/63

powershell -NoProfile -ExecutionPolicy Bypass -File scripts\plan16\tests\test-boundary-checker.ps1
# PASS: 16/16

.\scripts\plan16\check-change-boundaries.ps1 -AllowedPathPrefixes <WP-01B allowlist>
# PASS: 43 files analyzed, 0 errors, 0 warnings

.\scripts\plan16\check-change-boundaries.ps1 -AllowedPathPrefixes <WP-01B allowlist>
# PASS without GIT_CONFIG_GLOBAL, proving Git stderr noise is ignored

dotnet build v2rayN\v2rayN\v2rayN.csproj -c Debug --no-restore --verbosity minimal
# PASS: output NetAccel.dll, 0 warnings, 0 errors

dotnet publish v2rayN\v2rayN\v2rayN.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --verbosity minimal
# PASS: output publish\NetAccel.exe, 211683068 bytes

git diff --check
# PASS: no whitespace errors; only CRLF warnings
```

### Master

```powershell
sqlc generate -f sqlc\sqlc.yaml
# PASS

cd contracts; go test . -v
# PASS

go test ./internal/handler_v2 -run "TestNormalizeClientReleaseRequest|TestClientReleaseManifest" -v
# PASS

go test ./...
# BLOCKED only in 8 handler_v2 PostgreSQL integration tests because NETACCEL_TEST_DB_DSN is not set.
# Other packages passed.

git diff --check
# PASS
```

## Remaining Gates

1. PostgreSQL integration tests require `NETACCEL_TEST_DB_DSN` and a dedicated test database to execute migration 000020 end to end.
2. Windows manual checks remain for coexistence install/uninstall, AppUserModelID/tray grouping, protocol registration, and auto-start registry/task behavior.
3. `netaccel://` protocol registration is not implemented in R0.

## Notes

- `NetAccel.Managed` and `NetAccel.Managed.Tests` are intentionally root-level projects, referenced from `v2rayN/v2rayN.slnx` via `../`.
- The managed config tests no longer fall back to structural validation on generation failure; Xray Reality and sing-box Hysteria2 generation must succeed and produce parseable JSON.
- The boundary checker now redirects Git stderr and normalizes only string stdout lines, preventing host Git warning objects from entering path analysis.
