# Windsurf

Windsurf has no MCP CLI - add the server via its config file:

1. Open Windsurf -> Settings -> Cascade -> Manage MCP servers -> View raw config
   (or edit `~/.codeium/windsurf/mcp_config.json` directly).
2. Merge in the contents of `windsurf.json` from this folder.
3. Refresh the MCP server list.

An empty `GROUPDOCS_LICENSE_PATH` in the pasted config runs in evaluation mode.
Pin a version by replacing `GroupDocs.Assembly.Mcp` with `GroupDocs.Assembly.Mcp@26.10.0`.
