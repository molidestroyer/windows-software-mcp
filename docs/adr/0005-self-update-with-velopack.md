# ADR 0005 — Releases and self-update with Velopack

**Status:** accepted

## Context

The dock should install like any Windows app and keep itself up to date from this repository's
GitHub releases, without the user ever downloading a zip again.

Options considered: a hand-written updater (download zip, swap files from a script), MSIX with
App Installer auto-update, Squirrel.Windows (unmaintained) and Velopack (its successor).

## Decision

**Velopack**, behind an `IAppUpdater` port in the app:

- CI, on every `v*` tag: self-contained publish of the dock and the MCP server into one folder,
  `vpk pack` (Setup.exe, portable zip, full + delta `.nupkg`), `vpk upload github` to the release.
  The previous release is downloaded first so deltas can be computed.
- Install goes to `%LOCALAPPDATA%\SoftwareDock` — per user, no admin — with a Start-menu
  shortcut and an entry in *Installed apps* (so the dock lists itself).
- The app checks one minute after start and then every 12 h, downloads in the background and
  shows **Restart to update**; the user decides when. A toggle in Settings disables it.
- `VelopackApp.Build().Run()` is the first line of `Main`: Velopack starts the app with hook
  arguments during install/update/uninstall and expects it to exit at once.
- User settings live in `%LOCALAPPDATA%\SoftwareDock\settings.json`, outside the folder an update
  replaces.

## Consequences

- Updates are small (deltas) and atomic; a failed download leaves the running version intact.
- Running from source or in demo mode `IsInstalled` is false and the updater is inert
  (`NoAppUpdater`), so development never tries to update itself.
- The installer is not code-signed yet: SmartScreen will warn on first install. Signing slots into
  `vpk pack --signTemplate` when a certificate exists.
