# ADR 0001 — Ports and adapters around a pure domain

**Status:** accepted

## Context

The same capabilities — list installed software, find, install, upgrade, uninstall — are
consumed by two very different front ends: a tray UI driven by a person and an MCP server driven
by a language model. The data comes from two sources that disagree (the `Uninstall` registry keys
and WinGet), and both are Windows-only, slow, and awkward to test.

## Decision

```
Domain          pure model + rules (reconciliation, uninstall command parsing, id validation)
  ↑
Application     use cases (SoftwareInventory, SoftwareOperations) over ports
  ↑                IInstalledApplicationSource · IPackageManager · IUninstallerLauncher
Infrastructure  Windows adapters: registry, WinGet CLI, shell-launched uninstallers
  ↑
App / Mcp       composition roots and front ends
```

- **Domain has no I/O.** Reconciling the registry with WinGet is the hardest rule in the system,
  so it is a pure function (`InventoryReconciler`) with its own tests.
- **Failures are values.** An installer returning 1603 is an expected outcome both front ends
  must explain; `OperationResult` carries it. Exceptions are kept for cancellation and for a
  package manager that is not installed at all.
- **Use cases are singletons shared by every front end.** One inventory cache, one operation
  queue: the dock and an agent can never run two installers at once.
- **Untrusted input is validated in the domain.** Ids arrive from a console table and from a
  model; `PackageId` rejects anything that could become a command-line switch.

## Consequences

- The UI runs end to end on an in-memory machine (`--demo`), which is also how the headless
  rendering tests and the README screenshots work — on Linux.
- Replacing WinGet's CLI with its COM API (ADR 0002) touches one adapter.
- More projects than a single WinForms app would have. Worth it at two front ends.
