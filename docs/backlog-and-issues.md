# GroupDocs.Assembly.Mcp — backlog and known issues

Product-level backlog for the server. `M#` = fix in this repo or GroupDocs.Mcp.Core, `P#` = behaviour of the
GroupDocs.Assembly engine (26.7.0) that the server works around or documents.

## Confirmed defects

*None open at first release.*

## Known issues & limitations

- [ ] **P1 · Evaluation output is altered — Info.** Every output starts with an "Evaluation Only. Created with
      GroupDocs.Assembly 26.7 …" line, a saved workbook gains an extra worksheet, and an e-mail message keeps only
      its plain-text body (the HTML body — and so an MSG template's tables — is replaced by the evaluation line,
      also when the message is saved as a document). No row, page or document-count cap was found. Documented in
      README / how-to; tests read data back only where evaluation keeps it.
- [ ] **P2 · Presentation templates need GDI+ on Linux and macOS — Med (guarded).** The presentation engine calls
      System.Drawing.Common 6.0.0: without the `System.Drawing.EnableUnixSupport` switch (set in the csproj) and
      `libgdiplus` it fails with "The type initializer for 'Gdip' threw an exception". The image and the Tests CI
      install libgdiplus; the server's message names the fix. PPTX → TIFF still fails on Linux ("Not implemented"
      in libgdiplus).
- [ ] **P3 · Strict JSON parsing cannot read Json.NET reference metadata — Low (guarded).** With
      `JsonSimpleValueParseMode.Strict`, data containing `"$id"`/`"$ref"` throws `KeyNotFoundException: The given
      key 'id' was not present`. The server reads such data Loose; everything else Strict (Loose turns "007" into 7).
- [ ] **P4 · Data source binding depends on the data's shape — Info (guarded).** A JSON object exposes its keys
      by name only with `AlwaysGenerateRootObject` (otherwise an object with one list property collapses into the
      list); an array, XML, CSV or table is reachable only by the data source name. The server sets the flag for
      objects and takes the name from the template's tags.
- [ ] **P5 · A wrong target format fails late — Info (guarded).** "The document cannot be saved to the given
      format" comes after the whole template has been assembled. `AssemblyFormats` holds the measured save matrix
      and refuses first. No family saves CSV.
- [ ] **P6 · `{{name}}` is not template syntax — Info.** The upstream QuickStart example uses `{{Name}}`, which
      26.7.0 leaves as text without an error. `get_template_fields` flags such placeholders.
- [ ] **P7 · Empty lists have no fields — Med.** A JSON list that is empty in the data becomes a table without
      columns, so `<<foreach [i in items]>><<[i.Title]>>` fails with "Can not get the value of member 'Title' on
      type 'System.Data.DataRow'" — the same text as a wrong data source name. The hint tells them apart using
      the template's tags and offers `allowMissingMembers=true`.
- [ ] **P8 · Templates can load server-side files and URLs — Med.** Image and document tags load the path or
      URI their expression returns. Expressions cannot reach .NET APIs (no `System.IO`, `Environment`, `DateTime`
      statics — measured), but an image path can be any file the process can read. README "Limits" says to run
      with access to the intended folders only.
- [ ] **M1 · Tag inspection is static — Info.** `get_template_fields` reads tags without the engine: binary
      templates (DOC, XLS, PPT, MSG) are not readable, and fields reached through `Select`/`GroupBy` projections
      are not followed. The skeleton is labelled as inferred.

## Tools & functionality

- [ ] **P1** Per-record assembly from CSV and spreadsheet rows (today: JSON arrays only).
- [ ] **P1** Several data sources in one call (a JSON object covers most cases; a file + inline JSON pair does not).
- [ ] **P2** Inline template text (`templateText` + format) for agent-written Markdown/HTML templates.
- [ ] **P2** `validate_template`: assemble into memory with `InlineErrorMessages` and return the error list.
- [ ] **P2** HTML templates with relative resources (`LoadSaveOptions.ResourceLoadBaseUri` = storage folder).
- [ ] **P3** Barcode settings (`BarcodeSettings`) and OOXML compliance as parameters.

## Testing & CI

- [ ] **P1** First docker-channel run against the published GHCR image (needs the first release).
- [ ] **P1** `MeteredLicensingTests`: add a billable `assemble_document` asserting consumption increases once a
      metered CI account exists.
- [ ] **P2** Licensed-mode run (optional `GROUPDOCS_LICENSE` secret) to confirm the evaluation line, extra worksheet
      and e-mail body replacement disappear, and to read MSG table output back.

## Documentation & discoverability

- [ ] **P1** Docs section `docs.groupdocs.com/assembly/mcp/`, announcement post, forum thread, landing-page MCP tab.
- [ ] **P2** 30-second demo recording for the README.

## Platform & infra

- [ ] **P2** `Microsoft.Build.Tasks.Git` 8.0.0 (via `Microsoft.SourceLink.GitHub` 8.0.0) is flagged by NuGet
      audit (GHSA-23fw-v26w-5fgq). Build-time only and family-wide; fix with the family SourceLink bump.
- [ ] **P2** Drop the `System.Security.Cryptography.Pkcs` pin once the engine depends on a patched version.
