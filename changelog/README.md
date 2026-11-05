# Changelog

Per-change notes for GroupDocs.Assembly.Mcp, captured **per commit or logical change set**.
Each entry is a separate file named `NNN-short-slug.md` where `NNN` is a zero-padded
sequential number.

## Naming convention

```
001-initial-commit.md
002-add-xmp-support.md
003-fix-password-protected-pdf-handling.md
```

- `NNN` — increments strictly (never reused, even if a change is reverted)
- `short-slug` — kebab-case, imperative, ≤ 6 words

## Per-entry structure

```markdown
---
id: 001
date: 2026-04-18
version: 26.4.0           # optional — only for released changes
type: feature | fix | refactor | docs | chore | breaking
---

# Short human title

## What changed
- Bullet list of user-visible changes.

## Why
Short rationale. Skip if obvious from the title.

## Migration / impact
Only when consumers need to do something. Omit otherwise.
```

## Release aggregation

On release, the workflow (or release script) aggregates all entries since the previous
tag into the GitHub Release body. Entries with `version:` set are considered "already
released." Entries without `version:` are unreleased and roll into the next tag.
