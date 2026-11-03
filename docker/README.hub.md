# GroupDocs.Assembly MCP Server

Generate Word, Excel, PowerPoint, PDF and e-mail documents from templates and data with Claude,
Cursor, GitHub Copilot and any other MCP client — fill a template's `<<[field]>>`, `<<foreach>>`,
table, chart, image and barcode tags with JSON, XML, CSV or spreadsheet data, one document or one per
record. Runs locally: templates and data never leave your machine.

```bash
docker run --rm -i -v "$(pwd)/documents:/data" ghcr.io/groupdocs-assembly/assembly-net-mcp:latest
```

On a **Linux host**, run the container as your own user so it can write the generated documents into
the folder you mounted (the image runs as a non-root user, uid 1654):

```bash
docker run --rm -i --user "$(id -u):$(id -g)" -v "$(pwd)/documents:/data" \
  ghcr.io/groupdocs-assembly/assembly-net-mcp:latest
```

JSON client configs run no shell, so put the numbers in: `"--user", "1000:1000"`. Docker Desktop on
Windows and macOS does not need this.

## Tools

- **assemble_document** — fill a template with data (inline JSON, or a JSON/XML/CSV file or a document's table) and save it as its own format or PDF
- **assemble_per_record** — one document per record of a JSON array (mail merge)
- **get_template_fields** — a template's tags, data names, fields and a JSON skeleton of the data to fill in
- **get_document_info** — a file as a template (tags, save targets) and as data (keys, columns, tables)
- **get_supported_formats** — template formats, save targets per family, data formats
- **get_license_status** — licensing mode, metered consumption, engine version

## Configuration

| Variable | Purpose |
|---|---|
| `GROUPDOCS_MCP_STORAGE_PATH` | Folder for templates, data and generated documents inside the container (default `/data`) |
| `GROUPDOCS_MCP_OUTPUT_PATH` | Optional separate output folder |
| `GROUPDOCS_LICENSE_PATH` | Path to a mounted GroupDocs licence file; omit for evaluation mode |
| `GROUPDOCS_METERED_PUBLIC_KEY` / `GROUPDOCS_METERED_PRIVATE_KEY` | Metered (pay-per-use) licensing; needs outbound access to GroupDocs servers |

Without a licence every tool works; generated documents start with an "Evaluation Only" line, saved
workbooks gain an extra worksheet, and e-mail messages keep only their plain-text body. The image
includes libgdiplus and fonts, so presentation templates and PDF output work out of the box.

- Tags: `latest` + an immutable version tag per release matching NuGet (e.g. `26.10.0`).
- Source and docs: https://github.com/groupdocs-assembly/GroupDocs.Assembly.Mcp
- NuGet (`dnx GroupDocs.Assembly.Mcp --yes`): https://www.nuget.org/packages/GroupDocs.Assembly.Mcp
