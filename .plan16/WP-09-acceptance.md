# WP-09 Acceptance Record

**Reviewer:** Codex

**Date:** 2026-06-19

**Decision:** implementation accepted; package remains `code_complete` pending R5 real-environment evidence

## Closed findings

1. Restored .NET 10 after the earlier unapproved net9 downgrade.
2. Connected production theme resources and live theme switching.
3. Replaced inherited application/tray branding with reproducible multi-frame ICO assets.
4. Replaced fake theme assertions with production WPF construction and behavior checks.
5. Added external-process Axe.Windows scans for login and settings surfaces.
6. Fixed focus, live-region, automation-name, keyboard, contrast, and startup XAML defects found by those checks.
7. Captured real WPF visual evidence at four login scaling levels and both themes.

## Acceptance boundary

The technical UI and accessibility scope passes. A real managed account and authorized routes were
not available in this workspace, so network behavior is not claimed from mocks or screenshots.
T16-R3-17 remains `code_complete` until the R5 evidence in `WP-09-walkthrough.md` is recorded.

## Result

- No open code-level WP-09 finding remains.
- No critical contrast pair remains below WCAG AA.
- Axe.Windows reports no errors for the tested login and settings trees.
- Debug and Release builds, managed tests, WPF tests, and ServiceLib tests pass on .NET 10.
