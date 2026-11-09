# Claude Code

```bash
claude mcp add groupdocs-assembly -- dnx GroupDocs.Assembly.Mcp --yes
```

With storage folder and license:

```bash
claude mcp add groupdocs-assembly -e GROUPDOCS_MCP_STORAGE_PATH=/path/to/documents -e GROUPDOCS_LICENSE_PATH=/path/to/GroupDocs.Total.lic -- dnx GroupDocs.Assembly.Mcp --yes
```

Pin a version by replacing `GroupDocs.Assembly.Mcp` with `GroupDocs.Assembly.Mcp@26.11.0`.
