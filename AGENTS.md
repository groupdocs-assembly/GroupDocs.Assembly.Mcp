# AGENTS.md — Guide for AI coding agents

Brief orientation for AI coding agents (Claude Code, Copilot, Cursor, Aider, Amp, Codex) working in this repository.

## What this repo is

`GroupDocs.Assembly.Mcp` — an MCP server that exposes [GroupDocs.Assembly for .NET](https://products.groupdocs.com/assembly/net/)
as AI-callable tools for generating documents from templates and data. Shipped as a .NET tool
(`dnx GroupDocs.Assembly.Mcp --yes`), as Docker images (`ghcr.io/groupdocs-assembly/assembly-net-mcp`,
`groupdocs/assembly-net-mcp`) and in the MCP Registry (`io.github.groupdocs-assembly/groupdocs-assembly-mcp`).
Built on the shared `GroupDocs.Mcp.Core` framework (storage, file resolution, licensing, error filter,
`get_license_status`).

Integration tests against the **published** artifact live in the sibling repo
[GroupDocs.Assembly.Mcp.Tests](https://github.com/groupdocs-assembly/GroupDocs.Assembly.Mcp.Tests).

## MCP tools exposed

| Wire name | Class | What it does |
|---|---|---|
| `assemble_document` | `AssembleDocumentTool` | Template + data (inline JSON, or a JSON/XML/CSV file or a document table) → one document, own format or PDF |
| `assemble_per_record` | `AssemblePerRecordTool` | One document per element of a JSON array; partial failures listed per record |
| `get_template_fields` | `GetTemplateFieldsTool` | Tags, data names, fields and a JSON data skeleton, read without the engine (JSON) |
| `get_document_info` | `GetDocumentInfoTool` | A file as a template (family, tags, save targets) and as data (keys, columns, tables) (JSON) |
| `get_supported_formats` | `GetSupportedFormatsTool` | Template formats, save targets per family, data formats (JSON) |
| `get_license_status` | (GroupDocs.Mcp.Core) | Licensing mode, metered consumption, engine version |

## Folder layout

```
src/GroupDocs.Assembly.Mcp/
  Program.cs                    ← host: AddGroupDocsMcp().AddLocalStorage("./Files"), stdio transport
  AssemblyLicenseManager.cs     ← licence file + metered key + consumption + engine marker
  .mcp/server.json              ← MCP Registry manifest (version in lockstep with build/dependencies.props)
  Tools/
    AssembleDocumentTool.cs  AssemblePerRecordTool.cs  GetTemplateFieldsTool.cs
    GetDocumentInfoTool.cs  GetSupportedFormatsTool.cs
    AssemblyJob.cs              ← template copied to the workspace, family, tag analysis, output target
    AssemblyData.cs             ← loads JSON/XML/CSV/table data and works out the data source name
    AssemblyFormats.cs          ← families, measured save targets, extension → engine FileFormat
    TemplateText.cs             ← template text in document order (OOXML, ODF, TXT/MD/HTML/RTF/EML)
    TemplateAnalyzer.cs         ← tags, foreach scopes, lambdas → data names, fields, JSON skeleton
    AssemblyTool.cs             ← evaluation notice, engine-error hints, output naming, assemble + store
    ToolError.cs                ← CallToolResult success/failure with the "<Op> failed for '<file>'" prefix
    TempWorkspace.cs            ← per-call temp folder; scrubs temp paths out of error text
src/GroupDocs.Assembly.Mcp.Tests/ ← unit tests (analyzer, text reader, formats, data naming, errors)
build/dependencies.props        ← SINGLE source of versions (server, engine, Core, security pins)
docker/  install/  changelog/  docs/backlog-and-issues.md  .github/workflows/
```

## Engine facts that shaped the code (GroupDocs.Assembly 26.7.0)

- Templates use LINQ Reporting tags: `<<[expr]>>`, `<<foreach [x in list]>>…<</foreach>>`, `<<if>>`,
  `<<var>>`, `<<image>>`, `<<barcode>>`, `<<link>>`, chart tags. `{{name}}` is **not** processed.
- A JSON **object** loaded with `AlwaysGenerateRootObject` exposes its keys by name with or without a
  data source name. A JSON **array**, XML, CSV or document table is reachable only by the data source
  name the template uses — `AssemblyData` takes it from `TemplateAnalyzer`.
- JSON is read `Strict` (keeps "007" a string); the engine's Strict mode cannot read Json.NET
  `$id`/`$ref`, so such data is read `Loose`.
- Tags may span paragraphs; text is matched per part, not per paragraph. Shared strings are not in
  cell order — `TemplateText` walks the worksheet.
- A target outside the template's family fails only after a full assembly ("The document cannot be
  saved to the given format"); `AssemblyFormats` refuses it first. Spreadsheets cannot save CSV.
- HTML output writes `<name>_files/`; `AssemblyTool.AssembleToStorageAsync` stores it beside the page
  under the de-duplicated name.
- Presentation templates draw through System.Drawing.Common 6.0.0: Linux needs libgdiplus plus the
  `System.Drawing.EnableUnixSupport` switch set in the csproj.
- Evaluation mode: an "Evaluation Only" line in every output, an extra worksheet in saved workbooks,
  e-mail HTML bodies replaced. No row or document-count limit.

## Commands

```bash
dotnet build src/GroupDocs.Assembly.Mcp.sln -c Release
dotnet test src/GroupDocs.Assembly.Mcp.sln -c Release
pwsh ./build.ps1                                  # asserts server.json lockstep, packs into build_out/
pwsh install/generate-install-links.ps1 -Check    # CI drift guard for install/generated + README buttons
# integration suite against this build:
MCP_SERVER_DLL=$PWD/src/GroupDocs.Assembly.Mcp/bin/Release/net10.0/GroupDocs.Assembly.Mcp.dll \
  dotnet test ../GroupDocs.Assembly.Mcp.Tests -c Release --filter "Category!=Metered"
```

## House rules

1. **Every change takes a CalVer bump** (`YY.M.N`) in `build/dependencies.props` **and** both `version`
   fields of `.mcp/server.json`, plus a `changelog/NNN-<version>-<slug>.md` entry — here and in the Tests repo.
2. **Tool descriptions are the product surface.** Each states the task, formats, "Do NOT pre-check",
   the response shape and the failure prefix. Change the integration tests when you change them.
3. **Failures return `ToolError.Failure`** (isError + prefix); never let an engine exception escape a tool
   after the file is resolved, and never put a temp path in a message.
4. **No silent success.** A per-record run lists every record that failed; an output is never claimed
   that was not written.
5. **Never commit** licence files, keys, `build_out/`, or machine paths. Run the kit's hygiene sweep first.
