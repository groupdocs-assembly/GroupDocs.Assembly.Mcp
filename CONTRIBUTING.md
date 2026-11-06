# Contributing

Thanks for your interest in improving the GroupDocs.Assembly MCP server.

## Build & test

Requires the .NET 10 SDK (see `global.json`).

```bash
dotnet restore
dotnet build src/GroupDocs.Assembly.Mcp.sln -c Release
dotnet test  src/GroupDocs.Assembly.Mcp.sln -c Release

# Local pack (writes to ./build_out) — also validates that .mcp/server.json
# matches build/dependencies.props
pwsh ./build.ps1

# Run the server locally over stdio
dotnet run --project src/GroupDocs.Assembly.Mcp
```

`.vscode/mcp.json` contains a `groupdocs-assembly-dev` entry that runs the server from
source — handy for exercising tools from VS Code Copilot while developing.

See [AGENTS.md](AGENTS.md) for the repo layout, house rules, and versioning scheme —
it applies to human contributors as much as AI agents.

## Pull request expectations

1. **One logical change per PR**, conventional-commit style title (`fix:`, `feat:`, `docs:`, `ci:`).
2. **Changelog entry required** for behaviour changes: add `changelog/NNN-<slug>.md`
   (see `changelog/README.md` for the format).
3. **Don't bump versions in a feature PR** — maintainers bump `build/dependencies.props` +
   `.mcp/server.json` together at release time (`build.ps1` enforces they match).
4. **Never edit `install/generated/` or the README install-buttons block by hand** — change
   `install/config.json` and run `pwsh install/generate-install-links.ps1`. CI fails on drift.
5. Tool description strings (`[Description(...)]`) are user-facing API for AI agents — write
   them task-oriented and test how they read in an MCP client.
6. Integration tests against the *published* package live in the companion
   [GroupDocs.Assembly.Mcp.Tests](https://github.com/groupdocs-assembly/GroupDocs.Assembly.Mcp.Tests)
   repo — new tools or changed response shapes need a matching PR there.

## Reporting bugs / requesting features

Use the [issue templates](https://github.com/groupdocs-assembly/GroupDocs.Assembly.Mcp/issues/new/choose).
For security reports, see [SECURITY.md](SECURITY.md) — do not open a public issue.
