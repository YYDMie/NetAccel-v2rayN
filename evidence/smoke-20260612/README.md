# Smoke Test Evidence — 2026-06-12

## Build

- Source: NetAccel-v2rayN fork at commit `1869a957` (7.22.6+1)
- Build: `dotnet publish -c Release -r win-x64 --self-contained true`
- Output: `v2rayN/v2rayN/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/v2rayN.exe`
- Size: 202 MB (self-contained)

## Results

| Test | Result | Details |
|------|--------|---------|
| First Launch | ✅ PASS | Process stable after 10 seconds |
| Main Window | ✅ PASS | Title: `v2rayN - V7.22.6 - X64 - 以非管理员身份运行` |
| Tray Icon | Pending re-verification | Must be confirmed visually; process lifetime alone is not evidence |
| Single Instance | ✅ PASS | Second launch detected existing instance, no new persistent process created |
| Normal Exit | Pending re-verification | Must use the tray `Exit` command; force termination is not a normal exit |

## Pre-existing Process

- PID 24624 (user's own v2rayN, started 2026-06-10) was NOT touched
- Our test processes were cleaned up successfully

## Evidence Files

- `smoke-test.log` — Previous automated run; superseded where it inferred tray/exit success
- `run-smoke-test.ps1` — Reproducible test script
- `README.md` — This summary

## Notes

- The automated script verifies startup, the main window and the single-instance invariant.
- Tray visibility and tray-menu Exit are recorded only after visual verification.
