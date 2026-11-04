# ADR 0006 — PC hygiene: startup apps, security alerts, leftovers, unused apps, schedules, notifications

**Status:** accepted

## Context

Beyond install/update/uninstall, the things most Windows users never get round to are: trimming what
starts with Windows, knowing which updates actually matter, cleaning what uninstallers leave behind,
spotting software they no longer use, and updating without thinking about it. Each needs a data
source Windows does not expose as an API, and each can do harm if it guesses.

## Decisions

| Feature | Source of truth | Why this one | Safety rule |
| --- | --- | --- | --- |
| Startup apps | Run keys + Startup folders, `Explorer\StartupApproved` | It is what Task Manager reads and writes | Disable, never delete; HKLM entries say they need admin |
| Security alerts | CISA Known Exploited Vulnerabilities catalog | Free, curated, "exploited now" rather than "a CVE exists" | Flag only apps with an update pending; strict name matching + small alias list |
| Leftovers | Install folder + exact-name folders in well-known locations | Predictable, explainable | Recycle Bin only; offered only once the app is really gone |
| Unused apps | Explorer's UserAssist launch history | Per user, no admin, already maintained by Windows | Informational badge and sort; never acts on its own |
| Pin / skip version | Per-user settings | A user preference, not machine state | Pinned apps are excluded from Update all, schedules and notifications |
| Scheduled updates | In-process clock check every minute | Survives sleep (a missed run is due on wake) without a scheduled task | Records the run before starting it; batch stops on a dismissed UAC prompt |
| Notifications | Toasts via the Windows Community Toolkit compat layer | Works unpackaged (portable and Velopack) | Each version announced once; nothing while the user can see the panel |

Path → application attribution for startup entries, usage history, leftovers and resource usage is
one rule (`ApplicationRoots`): the deepest specific install folder wins.

## Consequences

- The app now targets `net10.0-windows10.0.19041.0` for WinRT toasts (minimum supported Windows 10 1809).
- The KEV catalog names products, not versions: "exploited + you are behind" is the strongest claim
  it supports, and the UI words it that way ("Exploited", "under active attack"), not "vulnerable".
- UserAssist only records programs started from Explorer, the Start menu or the taskbar; apps run
  some other way are "unknown", never "unused".
- Pins and skipped versions are dock preferences; the MCP server reports raw updates.
