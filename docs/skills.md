# Skills (Claude Code / Codex)

This document describes how the `unity-cli` repository ships skills to both
Claude Code and Codex CLI, the contract that governs them, and how the
`unity-cli skills lint` subcommand enforces it. The full specification lives in
GitHub Issue [#160](https://github.com/akiojin/unity-cli/issues/160).

## Overview

| Concern                     | Where it lives                                                       |
| --------------------------- | -------------------------------------------------------------------- |
| Canonical skill source      | `.claude-plugin/plugins/unity-cli/skills/unity-*`                    |
| Claude Code plugin manifest | `.claude-plugin/plugins/unity-cli/plugin.json`                       |
| Claude Code marketplace     | `.claude-plugin/marketplace.json`                                    |
| Codex plugin manifest       | `.codex-plugin/plugin.json`                                          |
| Codex skills root           | `.agents/skills/` (symlinks into the canonical directory)            |
| Codex marketplace           | `.agents/plugins/marketplace.json`                                   |
| Per-tool symlinks           | `.claude/skills/unity-*` and `.agents/skills/unity-*`                |
| Development-only skills     | `dev-skills/` (excluded from both plugins)                           |
| Linter                      | `unity-cli skills lint`                                              |
| Contract SPEC               | GitHub Issue [#160](https://github.com/akiojin/unity-cli/issues/160) |
| Contributor guide           | `.claude-plugin/plugins/unity-cli/CONTRIBUTING.md`                   |

The Claude Code and Codex plugins share a single skills source of truth.
`.codex-plugin/plugin.json` references `../.claude-plugin/plugins/unity-cli/skills/`
and `.agents/skills/` symlinks point to the same directory, so editing a
SKILL.md updates both plugins atomically.

## Skill Contract v1

### Naming

- Pattern: `unity-<domain>-<action>` (kebab-case, lowercase, ≤ 64 chars)
- Reserved words `claude` / `anthropic` are forbidden
- Directory name and frontmatter `name` must match

### Required frontmatter

```yaml
---
name: unity-<domain>-<action>
description: <verb> <object> with unity-cli. Use when ... Do not use ... use `<sibling>` instead.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: <name>
  version: 0.3.0
  category: scenes
  triggers: [scene, gameobject, ...]
  siblings: [unity-gameobject-edit, ...]
---
```

`category` enum: `foundation` `scenes` `assets` `code` `editor` `input`
`testing` `prefabs` `ui`.

`unity-csharp-edit` is the only skill permitted to include `Edit, Write` in
`allowed-tools`. `unity-cli-usage` is the only skill permitted to set
`user-invocable: false` (foundation).

### Description rules

| Rule            | Limit                                                                      | Source                                                   |
| --------------- | -------------------------------------------------------------------------- | -------------------------------------------------------- |
| Hard length     | 1024 chars                                                                 | Anthropic platform limit                                 |
| Soft front-load | At least one `metadata.triggers` token in the first 250 chars              | Listing truncation; main trigger must survive truncation |
| Person          | Third-person / imperative                                                  | Anthropic best practices                                 |
| Use when        | At least one `Use when` clause                                             | Disambiguates against siblings                           |
| Do not use      | At least one `Do not use` (or `Not for`) clause                            | Boundary clarity                                         |
| Sibling mention | At least one `metadata.siblings` entry must appear in the description text | Anchors the alternative                                  |

### SKILL.md body

- ≤ 500 lines
- Required `## H2` headings in order: `Use When` → `Do Not Use When` → `Preferred Flow` → `Examples` → `References`
- `## References` must contain ≥ 1 markdown link
- Reference files are 1 level deep (no `references/foo.md` linking to `references/bar.md`)
- Reference files > 100 lines need `## Table of Contents` in the first 15 lines
- No time-sensitive vocabulary (`as of`, `until next release`, `currently`, `現時点では`)

### Required references

Every skill must include `references/runtime-checklist.md` — the shared
connection / instance / install-mode checklist. Skill-specific references live
in the same directory.

## Linter (`unity-cli skills lint`)

```bash
unity-cli skills lint [--root <path>] [--format text|json] [--severity warning|error]
```

| Flag         | Default                                               | Notes                                                    |
| ------------ | ----------------------------------------------------- | -------------------------------------------------------- |
| `--root`     | auto-detect `.claude-plugin/plugins/unity-cli/skills` | Pass an absolute path for spot-checks                    |
| `--format`   | `text`                                                | `json` returns machine-readable violations               |
| `--severity` | `warning`                                             | `error` makes the process exit non-zero on any violation |

Exit codes:

| Code | Meaning                                 |
| ---- | --------------------------------------- |
| 0    | No violations (or `--severity warning`) |
| 1    | Violations found and `--severity error` |
| 2    | I/O error (e.g. skills root missing)    |

### Rule index

| ID  | Area        | What it checks                                                                                                                                  |
| --- | ----------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| R01 | frontmatter | Required fields exist (`name`, `description`, `allowed-tools`, `metadata.author`, `metadata.version`, `metadata.category`, `metadata.triggers`) |
| R02 | frontmatter | `name` matches directory name                                                                                                                   |
| R03 | frontmatter | `allowed-tools` is a subset of the contract allow-list (rejects bare `Bash`)                                                                    |
| R04 | frontmatter | `metadata.category` ∈ enum                                                                                                                      |
| R05 | frontmatter | `metadata.triggers` non-empty, lowercase, deduplicated                                                                                          |
| R06 | frontmatter | Each `metadata.siblings` entry exists                                                                                                           |
| R07 | description | length ≤ 1024                                                                                                                                   |
| R08 | description | first 250 chars contain ≥ 1 trigger                                                                                                             |
| R09 | description | no first/second-person language                                                                                                                 |
| R10 | description | both `Use when` and `Do not use` (or `Not for`) appear                                                                                          |
| R11 | description | at least one sibling appears in the description text                                                                                            |
| R12 | description | sibling cross-listing is bidirectional                                                                                                          |
| R13 | body        | ≤ 500 lines                                                                                                                                     |
| R14 | body        | required H2 headings appear in order                                                                                                            |
| R15 | body        | `## References` has ≥ 1 markdown link                                                                                                           |
| R16 | body        | no time-sensitive vocabulary                                                                                                                    |
| R17 | references  | `references/runtime-checklist.md` exists                                                                                                        |
| R18 | references  | reference files > 100 lines have a Table of Contents                                                                                            |
| R19 | references  | references are 1 level deep                                                                                                                     |
| R20 | symlinks    | `.claude/skills/<name>` resolves to canonical dir                                                                                               |
| R21 | symlinks    | `.agents/skills/<name>` resolves to canonical dir                                                                                               |
| R22 | cross-skill | shared triggers require bidirectional sibling cross-listing                                                                                     |

### CI

`.github/workflows/ci.yml` runs the linter at `--severity error` on PRs. The
release pipeline does the same with `--severity error`. See `T-11` in
[#160](https://github.com/akiojin/unity-cli/issues/160) for the wiring detail.

## Claude Code vs Codex differences

| Aspect               | Claude Code                                                   | Codex                                                                           |
| -------------------- | ------------------------------------------------------------- | ------------------------------------------------------------------------------- |
| Plugin manifest path | `.claude-plugin/plugins/unity-cli/plugin.json`                | `.codex-plugin/plugin.json`                                                     |
| Marketplace          | `.claude-plugin/marketplace.json`                             | `.agents/plugins/marketplace.json`                                              |
| Skills lookup path   | Plugin `skills` field + `.claude/skills/` per-project symlink | Plugin `skills` field + `.agents/skills/` walked from CWD upward                |
| Display metadata     | `name`, `description`, `keywords`                             | Same fields plus `interface` object (`displayName`, `category`, `capabilities`) |
| Symlink target       | `.claude/skills/<name>` → canonical                           | `.agents/skills/<name>` → canonical                                             |

Both plugins share the same skill bodies; updating a SKILL.md updates both.

## Anthropic best-practice mapping

| Anthropic guideline                                | Where unity-cli enforces it |
| -------------------------------------------------- | --------------------------- |
| Front-load main trigger in the first 250 chars     | R08                         |
| Third-person, no first/second person               | R09                         |
| Use when / Do not use clauses                      | R10                         |
| ≤ 1024 char description                            | R07                         |
| ≤ 500 line SKILL.md body                           | R13                         |
| Progressive disclosure (1-level reference nesting) | R19                         |
| Reference TOC for long files                       | R18                         |
| Required `references/runtime-checklist.md`         | R17                         |
| Avoid time-sensitive content                       | R16                         |

## Adding a new skill

See `.claude-plugin/plugins/unity-cli/CONTRIBUTING.md` for the full checklist
and PR template fragment. The short version:

```bash
mkdir -p .claude-plugin/plugins/unity-cli/skills/unity-<domain>-<action>/references
cp .claude-plugin/plugins/unity-cli/skills/unity-cli-usage/references/runtime-checklist.md \
   .claude-plugin/plugins/unity-cli/skills/unity-<domain>-<action>/references/
$EDITOR .claude-plugin/plugins/unity-cli/skills/unity-<domain>-<action>/SKILL.md

ln -sf "../../.claude-plugin/plugins/unity-cli/skills/unity-<domain>-<action>" \
       ".claude/skills/unity-<domain>-<action>"
ln -sf "../../.claude-plugin/plugins/unity-cli/skills/unity-<domain>-<action>" \
       ".agents/skills/unity-<domain>-<action>"

cargo run -- skills lint --severity error
```

## Development-only skills

`dev-skills/gh-skills-sync/` is a `gh`-automation helper used to update
this repository itself. It is intentionally excluded from both Claude Code and
Codex plugins. The `unity-cli skills lint` linter only inspects skills under
`.claude-plugin/plugins/unity-cli/skills/unity-*`, so dev-only skills are
ignored.
