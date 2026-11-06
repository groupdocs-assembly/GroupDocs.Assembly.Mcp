# Security Policy

## Supported versions

Only the **latest released version** of `GroupDocs.Assembly.Mcp` (NuGet / GHCR / Docker Hub)
receives security fixes. Older CalVer releases (`YY.MM.N`) are not patched — upgrade to the
newest release to pick up fixes.

## Data handling

This MCP server processes documents **entirely locally**:

- Files are read from and written to `GROUPDOCS_MCP_STORAGE_PATH` on your machine (or the
  mounted volume in Docker). No document content is uploaded anywhere.
- The only network access is NuGet.org fetching the package itself when run via `dnx` or
  `dotnet tool install` (not applicable to the Docker image, which is self-contained).
- No telemetry is collected by this server.

## Reporting a vulnerability

Please **do not** open a public issue for security reports.

- Use GitHub's [private vulnerability reporting](https://github.com/groupdocs-assembly/GroupDocs.Assembly.Mcp/security/advisories/new)
  for this repository, or
- Report through the [GroupDocs support forum](https://forum.groupdocs.com/) marking the topic private.

Include the package version, install method (dnx / dotnet tool / Docker), OS, and a minimal
reproduction. You can expect an acknowledgement within **5 business days**.

## Scope notes

- Vulnerabilities in the underlying `GroupDocs.Assembly` engine (a closed-source NuGet
  dependency) are triaged with the GroupDocs product team; report them here as well and they
  will be routed.
- The server is designed for **local, single-user stdio use**. Running it exposed to untrusted
  input (e.g., wrapping it in a network service that accepts arbitrary documents from third
  parties) is out of the threat model — add your own sandboxing/validation in that case.
