# ADR 0002 — Drive WinGet through its CLI first, behind a port

**Status:** accepted

## Context

WinGet offers three integration surfaces:

| Surface | Pros | Cons |
| --- | --- | --- |
| `winget.exe` | Always present on Windows 10/11; same behaviour the user sees | Tables, not data: fixed-width, localized headers, truncated cells |
| COM API (`Microsoft.Management.Deployment`) | Typed, progress events, no parsing | WinRT projections, out-of-proc server activation quirks, no Linux build |
| PowerShell `Microsoft.WinGet.Client` | Objects | Module may be absent; PowerShell startup cost per call |

## Decision

Start with the CLI, isolated in `WinGetPackageManager` behind `IPackageManager`, and make the
parser robust instead of clever:

- Columns by **position and count**, never by localized header text.
- Widths in **terminal cells** (CJK names are two cells wide).
- Rows stop at blank lines and at lines whose words straddle a column boundary (footers).
- Truncated ids (`…`) are kept and flagged; the domain refuses to act on them and the uninstall
  policy falls back to the registered uninstaller.
- Every call: `--disable-interactivity`, agreements accepted, `--id`/`--query` named options,
  arguments through `ProcessStartInfo.ArgumentList`.

## Consequences

- Works on any machine with App Installer, no extra setup.
- The parser is the most fragile component; it has the densest tests (fixtures generated with
  WinGet's exact layout).
- Moving to the COM API is a new adapter, not a refactor.
