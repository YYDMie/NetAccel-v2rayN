# WP-10 Result - Windows Client

Status: accepted by Codex
Date: 2026-06-17

## Scope

WP-10 completes the client side of stable instance-bound config sync:

| Task | Result |
| --- | --- |
| T16-R2-04 stable encryption ADR | Client crypto matches the Master ADR and length-prefixed AAD preimage. |
| T16-R2-05 device-key lifecycle | Added P-256 device key generation, vault persistence, registration, and reuse. |
| T16-R2-06 `managed-envelope/v1` | Added decrypt/verify path with audience, expiry, revision, signature, and ECDH checks. |
| T16-R2-06A encrypted cache | Added atomic raw-envelope cache; decrypted payload is not persisted. |
| T16-R2-06B offline rules | Added cache eligibility checks for schema, expiry, instance, key, and offline policy. |
| T16-R2-06C sync/ack | Startup fetches v1 envelope, validates, caches, sends fetched/validated/failed ACK stages, and can fall back to eligible offline cache. |

## Implemented

- `NetAccel.Managed/Crypto/DeviceKeyManager.cs`
- `NetAccel.Managed/Crypto/ManagedEnvelopeV1Crypto.cs`
- `NetAccel.Managed/Cache/EnvelopeCacheManager.cs`
- `NetAccel.Managed/Cache/OfflineConfigRules.cs`
- `NetAccel.Managed/Cache/ServerKeyProvider.cs`
- `NetAccel.Managed/Dto/DeviceKey.cs`
- `NetAccel.Managed/Dto/ManagedEnvelopeV1.cs`
- Startup orchestration now ensures a device key, fetches
  `/client/managed/envelope`, validates/decrypts/caches v1 envelopes, and uses
  cache fallback only through offline rules.

## Cache / Offline Behavior

- Only `managed-envelope/v1` is accepted by the disk cache.
- Spike-v0 cannot be written to disk and is not accepted for offline startup.
- Cache writes are temp-file then replace.
- Offline use requires matching instance/key metadata and unexpired envelope
  metadata.
- Selection changes while offline remain policy-gated and are not converted
  into classic v2rayN subscriptions.

## Boundaries

- `NetAccel.Managed` does not reference `AppManager`, `SQLiteHelper`,
  `ConfigHandler`, `CoreConfigContextBuilder`, `AddBatchServers`, or `SubItem`.
- Managed lines remain short-lived managed runtime config, not native node
  imports.
- Sync ACK in WP-10 does not send `applied`; that remains for later real
  connection/application work.
- `.claude-dispatch/` is a local Claude runtime artifact and is not part of the
  WP-10 deliverable.

## Verification

- `dotnet test .\NetAccel.Managed.Tests\NetAccel.Managed.Tests.csproj`: pass,
  148/148. Existing xUnit1051 analyzer warnings remain.
- `dotnet test .\v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj`: pass, 69/69.
- `dotnet build .\v2rayN\v2rayN\v2rayN.csproj -c Debug`: pass, 0 warnings,
  0 errors.
- `rg -n "AppManager|SQLiteHelper|ConfigHandler|CoreConfigContextBuilder|AddBatchServers|SubItem" NetAccel.Managed -S`: no matches.
- `git diff --check`: pass, with LF/CRLF conversion warnings only.

## Notes

Claude Code completed most implementation work but did not produce this report
because the workflow failed after its API quota expired. Codex fixed the stable
fetch path from spike `/client/managed/config` to
`/client/managed/envelope`, aligned AAD with the Master length-prefix rule, and
reran the acceptance commands above.
