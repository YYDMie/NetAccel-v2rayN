# WP-11A Result Report

> Date: 2026-06-19
> Status: accepted by Codex for local technical scope
> Integrated branch: `codex/plan16-wp11a-integrated`
> Baseline: `3b81f402` (WP-09 acceptance fixes)

## Scope

WP-11A is the client half of WP-11:

| Task | Client scope | Result |
|------|--------------|--------|
| T16-R4-06 | `ManagedSessionReporter` create/activate/heartbeat/close/fail | code_complete |
| T16-R4-07 | Durable ordered session outbox and replay | code_complete |
| T16-R4-08 | Real latency/stability mapping and honest unknown speed | code_complete |
| T16-R4-09 | Client lifecycle and failure event codes | code_complete |

T16-R4-10 through T16-R4-10B remain in WP-11B because they change the Master,
database transaction boundaries, timeout control, emergency stop, and management UI.

## Implementation

- Added a production session transport that only calls `/api/v1/client/runtime/sessions...`.
- Runtime session calls use the restricted instance credential and never send an account JWT.
- Added create-before-core, activate-after-route, heartbeat, normal close, failed start,
  cancellation, route switch, and core-exit reporting to `ManagedConnectionCoordinator`.
- Session create uses a UUID idempotency key. Replaying the same create is safe against the
  existing Master idempotency behavior.
- Added an atomic JSON outbox with ordered events and local-to-server session ID mappings.
- The outbox writes before sending, replays in order, coalesces stale heartbeats, and retains
  transient network/auth failures for retry.
- Outbox events align with `run-event.schema.json`, including `fallback_session_id`,
  `session_id`, `instance_id`, stable terminal codes, and metrics.
- The outbox never contains access tokens, instance credentials, profile config, host, port,
  password, endpoint, or unsafe error detail.
- Added a controlled replay task which is owned and awaited by `ManagedSessionReporter`.
- Added a 30-second active-session heartbeat with assigned-endpoint TCP measurement.
- A bounded quality window maps measured latency, jitter, and loss. Upload/download speed is
  serialized only when a real provider supplies it; absent values remain null/unknown.
- Runtime shutdown stops the connection first, queues/sends terminal session state, then awaits
  the reporter before disposing API and vault resources.

## Tests

Sixteen tests cover:

- online create/activate/heartbeat/close ordering;
- offline persistence and ordered replay;
- heartbeat coalescing without lifecycle loss;
- permanent rejection and dependent event cleanup;
- corrupted outbox recovery and atomic temp-file cleanup;
- outbox secret/config/endpoint exclusion;
- Master error-code contract membership;
- unknown metrics and measured latency/stability/speed mapping;
- background heartbeat start and stop;
- runtime-only URL and instance-credential-only headers;
- coordinator success, failure, rejection, and route-switch lifecycle wiring.

Current verification:

| Check | Result |
|-------|--------|
| `dotnet test NetAccel.Managed.Tests/NetAccel.Managed.Tests.csproj` | PASS, 313/313 |
| `dotnet test v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj` | PASS, 69/69 |
| `dotnet test NetAccel.Managed.Wpf.Tests/NetAccel.Managed.Wpf.Tests.csproj` | PASS, 1/1 including Axe.Windows |
| WPF Debug and Release builds | PASS, 0 warnings/errors |
| Master `contracts` Go tests | PASS |
| `git diff --check` | PASS |

## Codex acceptance

WP-11B closed the Master-side fallback validation, strict payload validation, timeout/revoke/
emergency-stop convergence, revision publication, and management diagnostics dependencies.
The post-WP-11A interop fix also preserves selection revision, preferred Plan, and
`automatic_failover` through immediate send and durable outbox replay.

The complete client diff was synchronized from the isolated clone to the formal client worktree
on `codex/plan16-wp11a-integrated`. Formal-worktree verification passed 313 managed tests, 69
ServiceLib tests, and a WPF Debug build with zero warnings and errors. Contract discovery now
supports an explicit `NETACCEL_MASTER_REPO`, nested test clones, and the normal sibling-repository
layout.

Real-account, real-route, proxy/TUN restoration, and emergency-stop timing evidence remain R5
environment acceptance. They do not block local technical acceptance of WP-11A.
