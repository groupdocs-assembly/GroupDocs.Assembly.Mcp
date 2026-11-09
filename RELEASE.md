# Release Process — GroupDocs.Assembly.Mcp

End-to-end checklist for releasing a new version to NuGet.org + ghcr.io + Docker Hub + MCP Registry.

## Versioning — CalVer `YY.MM.N`

- `YY` — 2-digit year (e.g. `26` = 2026)
- `MM` — month without leading zero (e.g. `5` = May)
- `N` — patch increment starting at `0`; increment for hotfixes within the same month

Example: `26.10.0`, `26.10.1`, `26.11.0`.

---

## Day-to-day work (no release)

Just push to `master`:

```bash
git add <files>
git commit -m "…"
git push
```

`build_packages.yml` and `run_tests.yml` run on every push — `publish_prod.yml` and `publish_docker.yml` do **not**. Commits never create a tag, a NuGet release, a GitHub Release, or a Docker image tag. Changelog edits, code changes, and prop bumps are all free actions — you can commit them whenever without triggering a release.

---

## Releasing a new version

### 1. Prepare the release commit

All edits below go in **one commit on `master`**:

1. **Bump the package version** in [build/dependencies.props](build/dependencies.props):

   ```xml
   <GroupDocsAssemblyMcp>{NEW_VERSION}</GroupDocsAssemblyMcp>
   ```

2. **Bump both `.mcp/server.json` versions** — [src/GroupDocs.Assembly.Mcp/.mcp/server.json](src/GroupDocs.Assembly.Mcp/.mcp/server.json) has **two** `version` fields that must both match:

   ```json
   {
     "version": "{NEW_VERSION}",          ← top-level
     "packages": [
       {
         "version": "{NEW_VERSION}",      ← packages[0].version
         ...
       }
     ]
   }
   ```

   > `build.ps1` refuses to build if any of these drift from `dependencies.props`. The release workflow also double-checks at the start and fails fast.

3. **Update pinned versions in `README.md`** — search for the old version and replace every occurrence (`dnx GroupDocs.Assembly.Mcp@…`, Claude Desktop config, VS Code `mcp.json`). Typically 4 places.

4. **Add a changelog entry** — new file `changelog/NNN-short-slug.md` with `version: {NEW_VERSION}` in the frontmatter. Format in [changelog/README.md](changelog/README.md).

5. *(Optional, rare)* Bump external dependency versions in the same props file — `GroupDocsMcpCore`, `GroupDocsAssembly`, `MicrosoftExtensionsHosting`, `ModelContextProtocol`, `MicrosoftSourceLinkGithub`.

6. Commit + push:

   ```bash
   git add build/dependencies.props src/GroupDocs.Assembly.Mcp/.mcp/server.json README.md changelog/NNN-*.md
   git commit -m "Release {NEW_VERSION}"
   git push
   ```

### 2. Verify locally (optional but recommended)

```powershell
./build.ps1                                                   # runs server.json ↔ dependencies.props check, packs .nupkg
dotnet test src/GroupDocs.Assembly.Mcp.sln -c Release       # runs tests
docker build -f docker/Dockerfile -t assembly-net-mcp:test .    # Dockerfile sanity check
```

### 3. Wait for CI green on `master`

`build_packages.yml` + `run_tests.yml` must be green before releasing.

### 4. Trigger the release

Two ways to release — **pick one, not both**.

#### Option A — UI dispatch (no git-CLI required)

The release consists of **two workflows** — `publish_prod` (NuGet) and `publish_docker` (container images). Run both:

1. GitHub → **Actions** → **Publish Prod** → **Run workflow** → leave branch on `master` → enter `{NEW_VERSION}` → Run.
2. GitHub → **Actions** → **Publish Docker Image** → **Run workflow** → leave branch on `master` → enter `{NEW_VERSION}` → Run.

Both workflows validate the input matches `dependencies.props` (and `.mcp/server.json`), run their respective pipelines, and create the `{NEW_VERSION}` tag + GitHub Release at the end of `publish_prod`. If anything fails — no tag, no release, no Docker image tagged `:latest`.

#### Option B — tag push (fires both workflows automatically)

```bash
git tag {NEW_VERSION}
git push origin {NEW_VERSION}
```

The tag push fires both workflows simultaneously with `github.ref_name = {NEW_VERSION}`. Validation still runs.

> Tag must be `YY.MM.N` — no `v` prefix, no suffix. Workflows reject anything else.

### 5. CI takes over (either trigger)

**`publish_prod.yml` — NuGet + GitHub Release:**

1. **Verify required secrets** (fails in seconds if any missing).
2. **Resolve + validate version** — confirms input/tag matches `dependencies.props` AND both `server.json` version fields.
3. Build with `BUILD_TYPE=PROD` (stable version, no `-prod-xxx` suffix).
4. Pack `.nupkg` + `.snupkg` (includes `.mcp/server.json` and `tools/net10.0/any/` layout).
5. Sign `.nupkg` with SSL.com eSigner. Signing failures detected via exit code **and** file count.
6. Push to NuGet.org via **Trusted Publishing**: `NuGet/login` exchanges the job's GitHub OIDC token for a 1-hour API key right before the push (no long-lived key stored).
7. Create the GitHub Release (+ tag if needed) with changelog body + `.nupkg` attached.

**`publish_docker.yml` — container images:**

1. **Verify Docker Hub secrets** (fails early if missing).
2. **Resolve + validate version** — confirms matches `dependencies.props`.
3. Build multi-arch image (`linux/amd64`, `linux/arm64`).
4. Push `ghcr.io/groupdocs-assembly/assembly-net-mcp:{NEW_VERSION}` + `:latest`.
5. Push `docker.io/groupdocs/assembly-net-mcp:{NEW_VERSION}` + `:latest`.

**`publish_prod.yml → publish_mcp_registry` job** (runs on every release):

1. Runs after `publish_prod` succeeds.
2. Uses GitHub OIDC to authenticate with the MCP Registry — namespace `io.github.groupdocs-assembly/*` is auto-verified because the repo lives under `github.com/groupdocs-assembly/*`.
3. Publishes `src/GroupDocs.Assembly.Mcp/.mcp/server.json` to `https://registry.modelcontextprotocol.io`.

### 6. Post-release verification

- [ ] **NuGet**: package listed at new version, signed-badge visible, "MCP Server" tab on the package page shows the generated `mcp.json`
- [ ] **GitHub Release** created with nupkg assets + changelog body
- [ ] `ghcr.io/groupdocs-assembly/assembly-net-mcp:{NEW_VERSION}` pullable
- [ ] `docker.io/groupdocs/assembly-net-mcp:{NEW_VERSION}` pullable
- [ ] Smoke test from a clean machine: `dnx GroupDocs.Assembly.Mcp@{NEW_VERSION} --yes`
- [ ] MCP Registry: `https://registry.modelcontextprotocol.io/v0/servers/io.github.groupdocs-assembly%2Fgroupdocs-assembly-mcp/versions/latest` returns 200 with `version` = `{NEW_VERSION}` and `_meta.io.modelcontextprotocol.registry/official.isLatest` = `true` (note: the slash in the server name must be URL-encoded as `%2F`, and the route requires the `/versions/latest` suffix — there is no bare `/v0/servers/{name}` endpoint)

### 7. Re-running a failed release

- **Via UI**: Actions → pick the failed run → **Re-run all jobs**. Or start a fresh Run workflow with the same version input — `dotnet nuget push --skip-duplicate` and `docker push` of an identical image are idempotent.
- **Via tag**: if the tag was never created (i.e. the failure happened before the Create GitHub Release step), re-push:

  ```bash
  git push origin :refs/tags/{VERSION}    # delete from remote if it got created
  git tag -d {VERSION}                    # delete locally
  git tag {VERSION}                       # re-tag HEAD
  git push origin {VERSION}               # fires both workflows again
  ```

  **Do not re-push a tag that already has a successful release pointing at it** — that rewrites history for downstream consumers.

---

## Required GitHub secrets & variables

**Secrets** (`Settings → Secrets and variables → Actions → Secrets`):

| Secret | Purpose |
|---|---|
| `NUGET_USER` | nuget.org username (profile name, not e-mail) of the account that created the Trusted Publishing policy for `GroupDocs.Assembly.Mcp` |
| `ES_USERNAME` | SSL.com eSigner username |
| `ES_PASSWORD` | SSL.com eSigner password |
| `ES_TOTP_SECRET` | SSL.com eSigner TOTP 2FA secret |
| `CODE_SIGN_CLIENT_ID` | SSL.com eSigner OAuth CLIENT_ID |
| `DOCKERHUB_USERNAME` | Docker Hub username for `groupdocs` org |
| `DOCKERHUB_TOKEN` | Docker Hub access token scoped to `groupdocs/assembly-net-mcp` |


> **NuGet.org Trusted Publishing** — the push uses a temporary API key obtained from the job's GitHub OIDC token, so there is no `NUGET_API_KEY_PROD` secret any more. A policy must exist on nuget.org (user menu → *Trusted Publishing* → *Add*): Package owner `groupdocs`, Repository owner `groupdocs-assembly`, Repository `GroupDocs.Assembly.Mcp`, Workflow file `publish_prod.yml`, environment empty, scope *Push only new package versions* for `GroupDocs.Assembly.Mcp`. `NUGET_USER` is the nuget.org username of the account that created that policy; it must stay a member of the `groupdocs` organization, otherwise nuget.org deactivates the policy.

**Variables** (`Settings → Secrets and variables → Actions → Variables`):

| Variable | Default | Purpose |
|---|---|---|
| `CODE_SIGN_TOOL_VERSION` | `1.3.0` | CodeSignTool release tag from github.com/SSLcom/CodeSignTool |

> `GITHUB_TOKEN` is provisioned automatically — no setup needed for ghcr.io pushes or MCP Registry OIDC auth.

Both `publish_prod.yml` and `publish_docker.yml` have a secrets precheck as their first step — they fail in seconds if any required secret is missing, before burning CI minutes on builds.

---

## MCP Registry namespace verification

The `publish_mcp_registry` job authenticates via GitHub OIDC. The Registry auto-verifies the `io.github.groupdocs-assembly/*` namespace because the workflow runs inside `github.com/groupdocs-assembly/*` — no DNS records, no manual tokens, no opt-in variable required. First-time publish claims the namespace automatically; subsequent releases update it.

If you ever fork the repo to a different org (e.g. `github.com/someone-else/*`), the Registry publish will fail until either the `server.json` `name` field is updated to match the new org's namespace or DNS verification is added — but for the canonical `groupdocs-assembly` org, it just works.

---

## Yanking a bad release

Never re-upload the same version — NuGet.org rejects replays, and the MCP Registry treats server.json versions as immutable.

1. **NuGet.org**: unlist (don't delete) the bad package.
2. **ghcr.io / Docker Hub**: delete the affected tag. Keep `:latest` pointing at the previous good release.
3. **MCP Registry**: submit a deprecation request via the registry's issue tracker if you need to mark the bad version withdrawn.
4. Bump `N` in `dependencies.props` + `.mcp/server.json` (e.g. `26.10.0` → `26.10.1`).
5. Add a `type: fix` changelog entry.
6. Commit + push + release the patch using the normal flow above.
