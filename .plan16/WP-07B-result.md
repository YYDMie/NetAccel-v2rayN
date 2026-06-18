# WP-07B Result - Managed Login And Startup State Wiring

Status: code_complete
Date: 2026-06-18

## Scope

This slice advances T16-R3-04 and the startup-state portion of T16-R3-03:

- Final managed login card for the current account/password contract.
- Friendly loading, bad-credential, network, no-assignment, revoked, disabled,
  mandatory-update, sync-failure, and ready states.
- Real startup phase notifications from `ManagedStartupOrchestrator`.
- WPF composition for AuthService, instance binding, device key, encrypted
  cache, selection/status, and config sync.
- Managed shell switches from login to home only when a decrypted managed
  config is actually available.

The classic window remains the application startup entry until WP-07C connects
the home page to `ManagedConnectionCoordinator` and classic-mode handoff is
available from the managed shell.

## Security And Product Boundaries

- Password is read from WPF `PasswordBox`, passed directly to `LoginAsync`, and
  immediately cleared. It is not exposed as a ViewModel property.
- Tokens, instance credentials, installation identity, and device private key
  remain in `WindowsCredentialVault`.
- The managed shell does not import profiles into classic SQLite and does not
  call subscription/import/export paths.
- The production API defaults to `https://netaccel.jklsp.dynv6.net`; a
  development override uses `NETACCEL_API_BASE_URL`.
- Stable envelope verification requires a server signing public key supplied by
  `NETACCEL_SERVER_SIGNING_PUBLIC_KEY` or
  `NETACCEL_SERVER_SIGNING_PUBLIC_KEY_PATH`. Without it the UI remains in a
  safe sync-unavailable state instead of showing a false ready state.
- Current Master client login treats captcha as optional and the C# login
  contract has no captcha fields, so this slice does not add a non-functional
  captcha control.

## Verification

- `dotnet build v2rayN/v2rayN/v2rayN.csproj -c Debug`: passed with
  0 warnings and 0 errors.
- Login and startup tests: 22/22 passed.
- `dotnet test v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj -c Debug`:
  passed, 69/69.
- Full `NetAccel.Managed.Tests`: 215 passed, 14 failed. All 14 failures are the
  pre-existing missing sibling Master fixtures
  `managed-config-payload-vless.json` and
  `managed-config-payload-hysteria2.json`; no WP-07B test failed.
- Managed UI boundary scan: no `SubItem`, `AddBatchServers`,
  `MainWindowViewModel`, `CoreManager`, system proxy, TUN, or token/instance
  credential properties in the presentation layer.
- Login copy scan: no protocol, subscription, proxy, port, UUID, or core jargon.
- `git diff --check`: no whitespace errors; line-ending conversion warnings
  remain on existing tracked files.

## Remaining Work

- WP-07C home state mapping and one-click start/stop.
- Real managed login against the Master with a configured signing public key.
- 100/125/150/200% screenshots and keyboard-focus evidence.
- Default startup switch after the managed home and classic handoff are ready.
