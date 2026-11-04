# ADR 0003 — A tray app in Avalonia, not a PowerToys module

**Status:** accepted

## Context

The goal is "something that lives in the Windows taskbar and opens like our dock" to manage the
software installed on the PC. PowerToys was considered as a host.

- **PowerToys utilities** (FancyZones, Run, …) are built inside the PowerToys repository: a C++
  module interface plus settings pages, shipped with PowerToys itself. There is no supported way
  to ship a third-party utility as a PowerToys module.
- **PowerToys Command Palette** *does* accept third-party extensions (.NET, out-of-process,
  distributed through WinGet with the `windows-commandpalette-extension` tag). It is a launcher:
  type, pick, run. Its built-in WinGet extension already installs packages from it.
- A standalone tray app has no host dependency and can look and behave exactly like
  CasaAdminDock's panel.

UI stack: WPF, WinUI 3 and Avalonia were weighed. WinUI 3 is the most native but cannot build or
render outside Windows. Avalonia 12 with the Fluent theme gives a modern look, a built-in tray
icon, and — decisive for quality — the same XAML renders headless on Linux CI.

## Decision

A standalone Avalonia tray application: panel anchored to the right edge (📌) or floating (🪟),
opened from the tray icon or a global hotkey, tray icon as a status light.

## Consequences

- No dependency on PowerToys being installed.
- A Command Palette extension remains a cheap addition later: it would be another front end over
  the same `Application` layer (`ISoftwareInventory`, `ISoftwareOperations`).
- Win32-specific pieces (hotkey, icons, Run key) are isolated in `App/Platform`.
