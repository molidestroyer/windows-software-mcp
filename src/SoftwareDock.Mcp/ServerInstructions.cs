namespace SoftwareDock.Mcp;

/// <summary>
/// The text an MCP client reads on connect. It is how an agent discovers what this server is
/// for and how to use it safely — write it for a model that has never seen it before.
/// </summary>
internal static class ServerInstructions
{
    public const string Text = """
        Software Dock manages the software installed on this Windows PC: it can list it, find
        new software in the WinGet catalog, install it, upgrade it and uninstall it.

        Identifiers — never invent them, always take them from a previous tool result:
        - `key` identifies an INSTALLED application (from list_installed_software). Use it for
          get_software_details, upgrade_software and uninstall_software.
        - `packageId` identifies a package in the CATALOG (from search_software_catalog). Use it
          for install_software. Copy it exactly; ids ending in "…" were truncated and are unusable.

        Typical flows:
        - "What do I have installed?" / "Is X installed?" → list_installed_software with a filter.
        - "What can I update?" → list_installed_software with onlyUpdates=true.
        - "Install X" → search_software_catalog, pick the result whose publisher/name clearly match
          (ask the user when several plausible ones exist), then install_software.
        - "Remove X" → list_installed_software with a filter, then uninstall_software with its key.

        Rules:
        - Install, upgrade and uninstall change the user's machine. State exactly what you are about
          to change and get the user's confirmation first, unless they already asked for that exact
          change.
        - Operations run one at a time. Windows may show a UAC or installer window the user has to
          answer; a "Cancelled" result means they declined — do not retry on your own.
        - A result with status SucceededRebootRequired means the change completes after a restart.
          Tell the user; never restart the machine yourself.
        - The inventory is cached for a few minutes. Pass refresh=true after the user says they
          changed something outside this server.
        """;
}
