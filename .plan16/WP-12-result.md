# WP-12 Result - Signed Update And Supply Chain

Status: accepted by Codex for local technical scope
Date: 2026-06-19

## Scope

WP-12 implements T16-R4-11 through T16-R4-17:

- product/channel/architecture-scoped signed release manifests;
- ECDSA P-256/SHA-256 manifest verification and artifact SHA-256 verification;
- pinned and signed Xray/sing-box core manifest policy;
- bounded download, safe ZIP extraction, external updater, health confirmation,
  and file-level rollback;
- SPDX 2.3 SBOM generation and third-party notice policy;
- existing allowlist-only diagnostic ZIP retained and covered by managed tests;
- Windows CI security boundary, secret scan, test, Release build, SBOM, and
  signed release workflows.

## Security Decisions

- The release signature uses `ecdsa-p256-sha256` with IEEE P1363 64-byte
  signatures. Go/.NET already use P-256 and no new cryptographic package is
  introduced.
- Canonical signature input is fixed-order length-prefixed UTF-8 fields. The
  release signer links the exact client verification source file so signing
  and verification cannot drift independently.
- Release private keys are read only from
  `NETACCEL_RELEASE_SIGNING_KEY_PEM`; no private key is stored in the repo.
- Public keys are packaged under `release-trust/<key_id>.pem`. Unknown key ids,
  missing keys, non-HTTPS URLs, cross-origin artifacts, malformed hashes, and
  invalid signatures fail closed.
- Downloads are streamed with a 512 MiB default limit. ZIP extraction rejects
  traversal, symbolic links, excessive entries, and excessive expanded size.
- The updater runs outside the application directory, backs up only files
  replaced by the new package, starts the new client, and restores changed/new
  files when the health marker is missing.

## Verification

- Managed tests: 322/322 pass, including 9 update security tests.
- ServiceLib tests: 69/69 pass.
- WPF Release build: pass, 0 warnings and 0 errors.
- `NetAccel.Updater` Release build: pass, 0 warnings and 0 errors.
- Updater process fault injection: `UPDATER_ROLLBACK_PASS`.
- Release signer end-to-end test: 64-character SHA, 88-character signature,
  expected algorithm and key id.
- Plan 16 cumulative boundary checker: pass.
- Secret-pattern gate: pass.
- SPDX SBOM generation: pass.
- Release package structure gate: pass for the WPF executable, external
  updater, and public trust anchor; private signing material is rejected.
- Client `git diff --check`: pass.

## R5 Evidence Remaining

- Provision the real release public key as a repository variable and its
  private counterpart as a CI secret.
- Produce and publish a real signed WPF artifact and Master manifest.
- Execute successful update plus forced health-timeout rollback on a clean
  Windows machine while connected through system proxy and TUN.
- Publish the generated SPDX SBOM and review `NOASSERTION` package licenses.

These are release-environment evidence, not missing local implementation.
