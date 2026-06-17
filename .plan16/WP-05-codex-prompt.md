# WP-05 Codex Prompt - Connection Ownership And Managed Runtime

Task package: WP-05, covering T16-R2-10 through T16-R2-15.

Repository: `D:\AI\Claude code\NetAccel-v2rayN`

Baseline:

- Master Plan 16 progress records WP-02A/B/C/D, WP-03, WP-04 and WP-10 as accepted.
- Client baseline is `origin/codex/plan16`, containing accepted WP-03, WP-04 and WP-10 client work.
- Do not enter WP-06, UI/R3, export/share/backup guards, tray, or classic-mode removal.

Project positioning:

- NetAccel is a personal learning demo for studying managed acceleration
  clients, line operations, agent communication, admin control, and client
  runtime behavior.
- Treat the managed client like a game-accelerator style controlled assignment
  client: the server assigns the allowed line set, default recommendation, and
  policy; the device can use automatic selection or manually select only inside
  that assigned set.
- In project terminology, `Server` means one VPS acceleration line, `Plan`
  means a technical acceleration configuration, and `Device` means the endpoint
  using the managed configuration. `Plan` must not be treated as a commercial
  package or paid subscription tier.
- Managed configuration from NetAccel is the single source of truth for managed
  lines. Managed lines are read-only, cannot be edited/shared/exported, and
  must not be converted into classic local profiles or subscription data.
- Plan 16 phase 1 intentionally retains v2rayN's native local-node, generic
  subscription, and import/export capabilities as a compatibility and testing
  lane. Those classic capabilities are not the NetAccel managed default path,
  must stay separate from managed lines, and should only be considered for
  hiding/removal after the managed path is mature and stable.
- This work must not introduce airport/proxy-service behavior, payment,
  billing, package sales, traffic quota sales, public node pools, subscription
  link export, or any user-facing sales flow.

Scope:

1. Implement `ConnectionOwnershipCoordinator` for Managed/Classic mutual exclusion, including in-process concurrency, cross-process mutex support, lease release, and stale snapshot recovery.
2. Implement `ManagedConnectionCoordinator` as the single managed connection state machine for `Ready`, `Starting`, `Connected`, `Stopping`, and `Faulted`.
3. Reuse accepted WP-04 `ManagedRuntimeConfigBuilder` and short-lived managed profile adapters.
4. Add a production runner that uses `CoreManager`, `CoreConfigContext`, and `SysProxyHandler` without calling classic `MainWindowViewModel.Reload`.
5. Support managed system-proxy mode and TUN mode through runtime policy checks.
6. Enforce authorized-set selection from the current managed payload only: automatic, manual, available, capability-compatible, and policy-compatible profiles.
7. Persist successful user selection through `IManagedSelectionService` when requested, using the current `selection_revision`.
8. Implement safe manual line switching: failed switch restores the previous profile and system network state.
9. Implement policy-gated automatic fallback inside the authorized fallback set, without silently overwriting manual preference.
10. Ensure all failure paths stop core, restore system proxy/TUN through the runner, and release the owner.

Constraints:

- Do not use `SubItem`, `AddBatchServers`, subscription import, or classic
  SQLite profile persistence for NetAccel managed lines.
- Do not remove or break v2rayN native local-node, generic subscription, or
  import/export compatibility in WP-05; keep it separate from managed runtime.
- No final WPF shell, page, tray, or visual work.
- No WP-06 edit/copy/share/export/backup guard work.
- No packages, billing, public node pool, sales, paid subscription tiers,
  traffic quota sales, or subscription-link export.
- Tests must cover owner concurrency, state transitions, authorized-set selection, failed manual switch rollback, automatic fallback, and owner release on failure.

Completion report should include modified files, key decisions, commands run, unrun tests and limitations.
