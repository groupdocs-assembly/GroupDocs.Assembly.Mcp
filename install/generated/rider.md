# JetBrains Rider (2025.2+)

Settings -> Tools -> AI Assistant -> Model Context Protocol (MCP) -> Add. Choose
**As JSON** and paste:

```json
{
  "name": "groupdocs-assembly",
  "command": "dnx",
  "args": ["GroupDocs.Assembly.Mcp", "--yes"],
  "env": {
    "GROUPDOCS_MCP_STORAGE_PATH": "/path/to/documents",
    "GROUPDOCS_MCP_OUTPUT_PATH": "/path/to/documents",
    "GROUPDOCS_LICENSE_PATH": ""
  }
}
```

An empty `GROUPDOCS_LICENSE_PATH` runs in evaluation mode. Pin a version by
replacing `GroupDocs.Assembly.Mcp` with `GroupDocs.Assembly.Mcp@26.11.0`.
