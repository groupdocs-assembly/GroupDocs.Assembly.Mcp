---
id: 001
date: 2026-10-30
version: 26.11.0
type: feature
---

# Initial release of GroupDocs.Assembly.Mcp

## What changed

- New MCP server for GroupDocs.Assembly for .NET **26.7.0** on **GroupDocs.Mcp.Core 26.9.0**, cloned in
  shape from GroupDocs.Editor.Mcp 26.10.0 (workflows, `install/` generator, Docker, build and release scripts,
  `ToolError`/`TempWorkspace` helpers).
- Five tools, plus Core's `get_license_status`:
  - `assemble_document` — fills a template with data and saves the result in the template's format, another
    format of its family, or PDF. Data inline (`dataJson`) or as a `.json`, `.xml`, `.csv`/`.tsv` file or a
    spreadsheet/Word/presentation table (`data`, `dataTable`, `hasHeaders`, `csvDelimiter`); options
    `allowMissingMembers`, `removeEmptyParagraphs`, `updateFields`, `inlineErrors`. HTML output stores its
    `<name>_files/` image folder beside the page.
  - `assemble_per_record` — one document per element of a JSON array (`recordsPath` for an array inside an
    object), files named by number or by a record field (`fileNameField`, dotted paths allowed), at most
    `maxDocuments` (default 100, up to 1000); failed records are listed and do not stop the others.
  - `get_template_fields` — every tag, the data names, the fields read and a JSON skeleton of the data, read
    from the template without the engine (OOXML, ODF, TXT, Markdown, HTML, RTF, EML). Flags `{{…}}` placeholders.
  - `get_document_info` — a file as a template (family, tag count, data names, save targets) and as data (JSON
    keys and lengths, CSV header and row count, the tables of a document with their columns).
  - `get_supported_formats` — template formats and save targets per family (measured), data formats.
- The data source name is worked out from the template's tags: a JSON object's keys are the names; an array,
  XML, CSV or table takes the one collection name the template uses. JSON is read Strict ("007" stays text),
  except data with Json.NET `$id`/`$ref`, which only Loose reads.
- Tools return `CallToolResult`: every failure sets `isError` **and** starts with the tool's prefix
  (`Document assembly failed for '<file>': …`). Engine errors get a plain-words hint: the missing field or data
  name, an unclosed block, the libgdiplus fix, the Docker `--user` fix. Temp paths are scrubbed.
- Licensing: `AssemblyLicenseManager` implements licence file, metered key and consumption reading, and
  declares the engine for `get_license_status`.
- Package: nuget-first, `McpServer` package type, native PDBs stripped.

## Decisions

- **Native dependencies:** the presentation engine calls System.Drawing.Common 6.0.0, which on Linux throws
  `PlatformNotSupportedException` unless `System.Drawing.EnableUnixSupport` is set — 6.0.0 still honours it —
  and `libgdiplus` is installed (measured in the `dotnet/runtime:10.0` image: Word, spreadsheet and e-mail
  templates work without either). The csproj sets the switch; the Docker image installs `libgdiplus`,
  `libfontconfig1`, `fontconfig` and `ttf-mscorefonts-installer`. No manual SkiaSharp pin: the engine declares
  `SkiaSharp.NativeAssets.Linux.NoDependencies` itself.
- **Security pin:** `System.Security.Cryptography.Pkcs` 10.0.13 overrides the engine's transitive 9.0.0
  (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7).
- **Spreadsheet data types** are always on, so numbers and dates land in cells as numbers and dates.
- **Docker on Linux:** README and Docker Hub README document `--user "$(id -u):$(id -g)"`; a denied write fails
  with that fix in the message.

## Why

Document generation from templates is the job agents are most often asked to script by hand ("fill this
contract", "a letter per customer"). GroupDocs.Assembly does it with a real template engine; the server adds
what an agent needs on top — learning a template's data shape, inline JSON, and naming that just works.

## Verification

- Unit tests: 125/125 (`dotnet test src/GroupDocs.Assembly.Mcp.sln`).
- Integration suite (GroupDocs.Assembly.Mcp.Tests, 64 tests) in evaluation mode: 64/64 against this build via
  `MCP_SERVER_DLL` on Windows, and 64/64 against the locally built image via `MCP_SERVER_IMAGE` (Linux container,
  presentation templates included).
- Installer verifier (`verify-groupdocs-mcp.ps1`, handshake + auto) against the local image; a sequenced dnx
  session against the packed `.nupkg` (6 tools, a real `assemble_document` to PDF).
- `build.ps1`: server.json lockstep holds; nupkg 99.0 MB; `install/generate-install-links.ps1 -Check` clean.

## Migration / impact

None — first release.
