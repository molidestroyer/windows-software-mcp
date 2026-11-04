# Distribution

Every release goes out on its own: CI creates the GitHub release, then calls
[`distribute.yml`](../.github/workflows/distribute.yml). To distribute a release that already exists
(for example v0.3.0, released before this workflow existed): *Actions › Distribute › Run workflow*
with the version, without the `v`.

| Channel | What happens | Needs |
| --- | --- | --- |
| GitHub release | `Setup.exe`, portable zip, Velopack deltas (installed docks update from here) | nothing |
| MCP bundle | `SoftwareDock-mcp-win-x64.mcpb` attached to the release: double-click installs the server in Claude Desktop | nothing |
| [MCP Registry](https://registry.modelcontextprotocol.io) | publishes `io.github.molidestroyer/software-dock`, pointing at the bundle | nothing: GitHub OIDC |
| [WinGet](https://github.com/microsoft/winget-pkgs) | opens a PR with `MiguelMolina.SoftwareDock`; Microsoft's bots validate it and a moderator merges it | `WINGET_TOKEN` secret |

## One-time setup

### WinGet

1. Create a **classic** personal access token with only the `public_repo` scope
   (GitHub › Settings › Developer settings › Tokens (classic)). wingetcreate forks
   `microsoft/winget-pkgs` into your account and opens the PR from there.
2. Save it as the repository secret `WINGET_TOKEN` (Settings › Secrets and variables › Actions).
3. Run *Distribute* for the current version.

[`Submit-WinGet.ps1`](../packaging/winget/Submit-WinGet.ps1) renders the templates in
`packaging/winget/` for the first submission. Once the package is in winget-pkgs, later releases use
`wingetcreate update`, which starts from the accepted manifests. It never opens a second PR while
one is still open. `-DryRun` renders the manifests without submitting them.

Review usually takes a few days. Watch the PR: the validation bot comments when something fails
(installer not silent, Apps & Features entry not matched, antivirus false positive). Fix the
template here and comment on the PR, or let a moderator fix the manifest.

Then users install with:

```bash
winget install MiguelMolina.SoftwareDock
```

Things to check on the first PR:

- `ProductCode: SoftwareDock` assumes Velopack registers `HKCU\…\Uninstall\SoftwareDock` (its
  `--packId`). If the bot reports an ARP mismatch, correct it from the registry of an installed PC.
- The dock updates itself with Velopack. `winget upgrade` reads the version from that same
  Apps & Features entry, so both stay in step as long as Velopack updates `DisplayVersion`.

### MCP Registry

Nothing to set up: the job logs in with the workflow's GitHub identity, which owns the
`io.github.molidestroyer/*` namespace. A version can be published only once; re-running the
workflow for the same version fails at that step and changes nothing.

## Code signing (pending)

The installer is not signed, so SmartScreen warns on first run and antivirus engines are quicker
to flag it, which can also hold up the WinGet validation. Free option for open source:
[SignPath Foundation](https://signpath.org) (apply with the repository; AGPL-3.0 qualifies). Once
accepted, sign the published binaries and `Setup.exe` in CI (Velopack: `vpk pack --signTemplate`).
[Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/) is the paid
alternative (about USD 10/month), if available in your country.

## Not automated

These need your own accounts:

- **Microsoft Store**: free developer account for individuals; submit `Setup.exe` as an EXE app.
  It must be signed first.
- **Listings**: [awesome-mcp-servers](https://github.com/punkpeye/awesome-mcp-servers) (PR under
  *OS Automation*), [mcp.so](https://mcp.so), [Glama](https://glama.ai/mcp/servers),
  [AlternativeTo](https://alternativeto.net) (as an alternative to UniGetUI, Revo Uninstaller and
  Patch My PC).
- **Repository topics**: `windows`, `winget`, `mcp`, `mcp-server`, `uninstaller`, `avalonia`,
  `dotnet` (repository page › About › ⚙️).
