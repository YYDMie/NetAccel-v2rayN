# WP-09 Result Report

> Date: 2026-06-19
> Status: code_complete
> Branch: `codex/plan16-wp09-fix`

## Scope and result

| Task | Description | Result |
|------|-------------|--------|
| T16-R3-12 | Application icon concept | accepted |
| T16-R3-13 | Vector and multi-size icon assets | accepted |
| T16-R3-14 | Light/dark theme resources | accepted |
| T16-R3-15 | Accessibility and keyboard behavior | accepted |
| T16-R3-16 | Real WPF UI automation smoke test | accepted |
| T16-R3-17 | Beginner walkthrough | code_complete; real account/route run remains R5 evidence |

WP-09 implementation and local Windows technical acceptance are complete. The package remains
`code_complete`, rather than `accepted`, because a real managed account and authorized routes are
required to prove login, connection, route switching, network continuity, and proxy restoration.

## Completed fixes

- Restored the solution to .NET SDK `10.0.301` and `net10.0` targets.
- Loaded accessibility, application icon, and tray icon dictionaries from `App.xaml`.
- Wired `ManagedThemeService` into the production shell and applied live light/dark changes.
- Added theme-aware button, input, navigation, check box, combo box, and focus visuals.
- Corrected all critical light/dark text and status contrast pairs to WCAG AA.
- Added valid UI Automation names, startup focus, live-region events, tab cycling, and
  `Ctrl+Tab`/`Ctrl+Shift+Tab` section navigation.
- Fixed the `ManagedNavWidth` startup XAML type mismatch.
- Replaced inherited branding with NetAccel application and four tray-state ICO files.
- Added deterministic multi-size ICO generation for 16, 20, 24, 32, 40, 48, 64, 128, and 256 px.
- Added an STA WPF smoke test that constructs the production shell and scans separate login and
  settings host processes with Axe.Windows.
- Added actual WPF render evidence for login scaling, home, and light/dark settings.
- Removed four fake theme tests that only asserted a test double.

## Verification

| Check | Result |
|-------|--------|
| `dotnet test NetAccel.Managed.Tests/NetAccel.Managed.Tests.csproj` | PASS, 297/297 |
| `dotnet test NetAccel.Managed.Wpf.Tests/NetAccel.Managed.Wpf.Tests.csproj` | PASS, including two Axe.Windows scans |
| `dotnet test v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj` | PASS, 69/69 |
| `dotnet build v2rayN/v2rayN/v2rayN.csproj -c Debug` | PASS, 0 warnings/errors |
| `dotnet build v2rayN/v2rayN/v2rayN.csproj -c Release` | PASS, 0 warnings/errors |
| Multi-frame ICO inspection | PASS, all five files contain all nine sizes |
| `git diff --check` | PASS |

## Evidence

Evidence is stored under `.plan16/evidence/WP-09/`:

- `icon-matrix.png`
- `login-light-100.png`, `login-light-125.png`, `login-light-150.png`, `login-light-200.png`
- `home-light-100.png`
- `settings-light-100.png`, `settings-dark-100.png`

## Remaining external acceptance

The following claims are deliberately not inferred from an offline host or ViewModel test:

1. Login with a real NetAccel managed account.
2. Connect through an administrator-assigned Reality or Hysteria2 route.
3. Switch between at least two authorized routes while observing network continuity.
4. Exercise diagnostics against injected real Windows failures.
5. Exit while connected and verify system proxy/TUN/process restoration.

These are R5 environment checks and require controlled credentials and routes. See
`.plan16/WP-09-walkthrough.md` for the exact evidence checklist.
