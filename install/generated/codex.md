# Codex CLI (OpenAI)

```bash
codex mcp add groupdocs-assembly -- dnx GroupDocs.Assembly.Mcp --yes
```

Or add to `~/.codex/config.toml`:

```toml
[mcp_servers.groupdocs-assembly]
command = "dnx"
args = ["GroupDocs.Assembly.Mcp", "--yes"]

[mcp_servers.groupdocs-assembly.env]
GROUPDOCS_MCP_STORAGE_PATH = "/path/to/documents"
GROUPDOCS_MCP_OUTPUT_PATH = "/path/to/documents"
GROUPDOCS_LICENSE_PATH = ""   # empty = evaluation mode; set to your GroupDocs.Total.lic to lift limits
```

Pin a version by replacing `GroupDocs.Assembly.Mcp` with `GroupDocs.Assembly.Mcp@26.11.0`.
