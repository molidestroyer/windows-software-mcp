# CLAUDE.md — Software Dock

Tray dock (Avalonia) + MCP server to list, update, install and uninstall the software on a
Windows PC. .NET 10, ports & adapters. Repo language: **English** (code, comments, UI, docs,
commits). Conventional commits.

## Commands

```bash
dotnet build SoftwareDock.slnx
dotnet test SoftwareDock.slnx
dotnet run --project src/SoftwareDock.App -- --demo        # in-memory PC, safe anywhere
SOFTWAREDOCK_SCREENSHOTS=$PWD/docs/images dotnet test tests/SoftwareDock.App.Tests   # regenerate README screenshots (absolute path)
# release: bump <Version> in Directory.Build.props and push to main; CI tags vX.Y.Z and publishes with Velopack
```

Everything builds and tests on Linux too (`EnableWindowsTargeting`); the real adapters only run
on Windows.

## Layout

| Project | Responsibility |
| --- | --- |
| `Domain` | `InstalledApplication`, `ApplicationKey`, `PackageId`, `UninstallCommand`, `InventoryReconciler`. Pure. |
| `Application` | `SoftwareInventory` (cached snapshot), `SoftwareOperations` (serialized install/upgrade/uninstall), `UninstallPlan`. Ports in `Abstractions/`. |
| `Infrastructure` | `WinGetPackageManager` + `WinGetTableParser`, `RegistryInstalledApplicationSource`, `ShellUninstallerLauncher`, `ProcessRunner`. |
| `Mcp` | Tools, `ServerInstructions`, `McpServerRegistration`, `--print-config`, `--read-only`. |
| `App` | `DockViewModel` (+ `SettingsViewModel`, `RestoreViewModel`), `DockWindow` (panel), `DockShell` (tray, hotkey, background checks), `Services/` (auto-backup, self-update, shell), `Settings/` (per-user JSON), `Platform/` (Win32), `Controls/GaugeRing`, `Demo/`. |

## Decisions worth not undoing

- **`ApplicationKey` has WinGet's ARP shape** (`ARP\Machine\X64\<subkey>`). That is what makes
  registry ↔ WinGet matching exact. Changing it breaks reconciliation silently.
- **WinGet tables are parsed by column position, never by header name** — headers are localized.
  Widths are terminal cells (CJK = 2).
- **Ids are validated in the domain** (`PackageId`): no leading `-`, no quotes, no control chars.
  WinGet arguments always go through `ArgumentList` and named options (`--id`, `--query`).
- **Never guess silent switches** for EXE uninstallers. Silent = package manager, MSI `/qn`, or
  `QuietUninstallString`; otherwise interactive.
- **MSI `/I` is rewritten to `/X`** — `/I` opens the repair dialog.
- **Use cases are singletons** shared by UI and MCP: one cache, one operation queue.
- **MCP logs go to stderr only.** Anything on stdout corrupts the protocol.
- **Uninstall confirmation is inline**, not a modal: a modal steals focus and the anchored panel
  hides on deactivate.
- **The hotkey has its own message-only window** (`GlobalHotkey`), independent of the panel's.
- **Process ↔ app attribution is by install folder** (`ResourceAttribution`), deepest root wins,
  generic folders (Program Files, AppData…) are never roots. Do not "improve" it with name matching.
- **Resources are sampled only while the panel is visible** (`DockWindow` IsVisible → Start/StopMonitoring).
- **Batches stop on Cancelled**, continue on Failed. Resolve all keys from one snapshot.
- **Auto-backup skips partial snapshots** and rewrites only when content changed (`HasSameContentAs`).
- **`VelopackApp.Build().Run()` must stay the first line of `Main`.** User settings never go in the
  install folder: Velopack replaces it on update.
- **UI text uses `CurrentCulture`** (`Format`): tests must build expected numbers with the current
  culture, never hardcode `8.9` (fails on es-ES).
- **Self-update applies only on *Restart to update*** (checked 1 min after start, then twice a day;
  downloads delta). Manual equivalent: `%LOCALAPPDATA%\SoftwareDock\Update.exe apply -- --background`.
- **Anchored panel + dialogs:** wrap any picker/dialog in `DockWindow.SuspendAutoHide()`, or the
  panel hides when the dialog takes focus.

## Known limits

- Inno Setup / some NSIS uninstallers relaunch from `%TEMP%` and exit at once; the inventory may
  still list the app until the next refresh.
- Silent uninstall of a per-machine MSI from a non-elevated dock fails with 1603 (same with plain
  `winget uninstall --silent`): quiet MSI cannot raise UAC. Interactive mode or an elevated shell works.
- Some Inno uninstallers run via WinGet's `/SILENT` take minutes (TreeSize Free: 6.5 min); the MCP
  call blocks meanwhile.
- WinGet truncates long ids in its tables; such apps fall back to their registered uninstaller
  and cannot be upgraded from the dock until the COM adapter exists (ADR 0002).
