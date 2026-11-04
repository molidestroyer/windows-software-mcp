# ADR 0004 — MCP server: discovery through instructions, safety through the domain

**Status:** accepted

## Context

An agent connecting to the server knows nothing about it. It must find out what the server is
for, which identifier each tool needs and where that identifier comes from, and that some tools
change the machine.

## Decision

- **Server instructions are the discovery text** (`ServerInstructions.Text`), sent on
  `initialize`: purpose, the two identifiers (`key` for installed apps, `packageId` for catalog
  packages) and which tool produces each, the typical flows, and the rules (confirm changes with
  the user, never retry a cancelled UAC prompt, report reboots instead of rebooting).
- **Tool annotations** (`readOnlyHint`, `destructiveHint`, `openWorldHint`) let clients ask for
  approval where it matters.
- **`--read-only`** does not register the mutating tools at all; it does not merely refuse them.
- **`--print-config`** prints the `.mcp.json` fragment; the dock's tray menu copies the same text.
- **Logging goes to stderr only.** stdout is the JSON-RPC stream.
- Input validation is not the MCP layer's job: `PackageId` and `ApplicationKey` enforce it for
  every front end.

## Consequences

- WinGet ships its own MCP server (find + install). This one adds what that does not: the full
  installed inventory reconciled with the registry, upgrades, and uninstall of software WinGet
  does not know.
- Behaviour is covered end to end with the SDK's own client over in-memory pipes.
