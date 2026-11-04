<p align="center">
  <img src="docs/images/icon.png" width="72" alt="" />
</p>

<h1 align="center">Software Dock</h1>

<p align="center">
  The software on your Windows PC, one click from the taskbar — and one tool call from your AI agent.
</p>

<p align="center">
  <a href="https://github.com/molidestroyer/windows-software-mcp/actions/workflows/ci.yml"><img src="https://github.com/molidestroyer/windows-software-mcp/actions/workflows/ci.yml/badge.svg" alt="CI" /></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Avalonia-12-8B5CF6" alt="Avalonia 12" />
  <img src="https://img.shields.io/badge/MCP-server-111827" alt="MCP" />
</p>

---

Windows makes managing installed software tedious: *Settings › Installed apps* cannot update
anything, WinGet is a console, and the Store only knows its own apps. **Software Dock** lives in
the notification area and opens a panel on the right edge of the screen with everything that is
installed, what can be updated, and a search box over the WinGet and Microsoft Store catalogs.

The same capabilities are exposed as an **MCP server**, so Claude Code, Copilot or any MCP client
can answer *"what do I have installed?"*, *"update everything"* or *"install VLC"*.

| Installed | Updates | Discover |
| --- | --- | --- |
| ![Installed apps](docs/images/dock-installed.png) | ![Updates](docs/images/dock-updates.png) | ![Discover](docs/images/dock-discover.png) |

*Rendered by the headless UI tests from the real window, in demo mode.*

## Features

- **Tray icon as a status light** — green: up to date · amber: updates waiting · blue: working ·
  red: the inventory could not be read. Click it, or press **Win+Shift+I**, to toggle the panel.
- **📌 Anchored / 🪟 floating** — anchored, the panel hugs the right edge at full height and hides
  when it loses focus; floating, it is a normal draggable window.
- **One inventory** — the `Uninstall` registry keys (what *Installed apps* shows) reconciled with
  WinGet (ids, available updates) and MSIX/Store apps.
- **Update one or all**, **install** from the catalog, **uninstall** anything — through WinGet when
  it knows the app, otherwise through the app's own uninstaller (silently for MSI and
  `QuietUninstallString`, never with guessed switches).
- **Start with Windows**, single instance (a second launch just opens the panel), per-monitor DPI.
- **MCP server** with discovery instructions, tool annotations and a `--read-only` mode.

## Getting started

Requirements: Windows 10 1809+ / Windows 11, [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0),
and WinGet (*App Installer*, preinstalled on current Windows).

```bash
dotnet run --project src/SoftwareDock.App
```

Without touching your machine (in-memory demo PC):

```bash
dotnet run --project src/SoftwareDock.App -- --demo
```

Build, test and publish:

```bash
dotnet test SoftwareDock.slnx
```

```bash
dotnet publish src/SoftwareDock.App -c Release -r win-x64 --self-contained false -o publish
```

```bash
dotnet publish src/SoftwareDock.Mcp -c Release -r win-x64 --self-contained false -o publish
```

`appsettings.json` next to the executable configures the hotkey, panel width and cache duration.

## Connect an AI agent (MCP)

From the tray menu choose **Copy MCP config**, or print it:

```bash
SoftwareDock.Mcp.exe --print-config
```

and paste it into your client's `.mcp.json`:

```json
{
  "mcpServers": {
    "software-dock": {
      "type": "stdio",
      "command": "C:\\Tools\\SoftwareDock\\SoftwareDock.Mcp.exe",
      "args": []
    }
  }
}
```

On connect, the server sends **instructions** that tell the agent what it is for, which identifier
each tool takes and where it comes from, and to confirm changes with the user first.

| Tool | Kind | What it does |
| --- | --- | --- |
| `list_installed_software` | read-only | Installed apps with version, available update and `key`; filter, only-updates, refresh |
| `get_software_details` | read-only | Location, date, size and how it would be uninstalled |
| `search_software_catalog` | read-only | WinGet + Microsoft Store search, returns `packageId` |
| `install_software` | changes the PC | Silent install of a `packageId` |
| `upgrade_software` | changes the PC | Upgrade an installed app by `key` |
| `uninstall_software` | destructive | Remove an installed app by `key` |

Add `"args": ["--read-only"]` to expose only the first three.

## Architecture

```
src/
  SoftwareDock.Domain          pure model and rules — no I/O
  SoftwareDock.Application     use cases over ports (inventory, operations)
  SoftwareDock.Infrastructure  Windows adapters: registry, WinGet CLI, uninstallers
  SoftwareDock.Mcp             MCP server (stdio)
  SoftwareDock.App             Avalonia tray dock
tests/                         one test project per layer, incl. headless UI and MCP end to end
docs/adr/                      architecture decisions
```

Decisions worth reading before changing things:

1. [Ports and adapters around a pure domain](docs/adr/0001-ports-and-adapters.md)
2. [WinGet through its CLI first, behind a port](docs/adr/0002-winget-cli-first.md)
3. [A tray app in Avalonia, not a PowerToys module](docs/adr/0003-tray-app-not-powertoys-module.md)
4. [MCP: discovery through instructions, safety through the domain](docs/adr/0004-mcp-discovery-and-safety.md)

## How it compares

| | Software Dock | [UniGetUI](https://github.com/marticliment/UniGetUI) | WinGet's [built-in MCP server](https://learn.microsoft.com/en-us/windows/package-manager/winget/mcp-server-overview) | PowerToys [Command Palette](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/overview) |
| --- | --- | --- | --- | --- |
| Lives in the tray | ✅ panel docked to the screen edge | ✅ window + tray menu | — | launcher (Win+Alt+Space) |
| Installed apps WinGet does not know | ✅ registry reconciled | partial | — | — |
| Update / uninstall | ✅ | ✅ (+ Scoop, Chocolatey, npm…) | — (find, install) | install |
| MCP server for agents | ✅ inventory + install/upgrade/uninstall | — | ✅ find + install | — |

UniGetUI is the most complete GUI if you want many package managers. Software Dock is deliberately
smaller: Windows software only, one glance from the taskbar, and an agent-friendly API.

## License

[AGPL-3.0](LICENSE) © Miguel Molina
