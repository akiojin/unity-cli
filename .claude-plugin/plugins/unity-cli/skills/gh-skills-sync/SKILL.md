---
name: gh-skills-sync
description: Sync gh-* automation skills from a GitHub repository into global and project skill directories for Codex/Claude workflows.
allowed-tools: Bash, Read, Grep, Glob
---

# gh-skills-sync

Use this skill when asked to update or synchronize GitHub-related skills:

- `gh-fix-ci`
- `gh-fix-issue`
- `gh-pr`
- `gh-pr-check`

## Command

Run from repository root:

```bash
scripts/sync-gh-skills.sh
```

## Typical Options

```bash
# check only (no write)
scripts/sync-gh-skills.sh --check

# update project skills only
scripts/sync-gh-skills.sh --target project

# pin a specific ref
scripts/sync-gh-skills.sh --ref main
```

## Behavior

- Default source repo: `akiojin/skills`
- Sync targets:
  - Global: `${CODEX_HOME:-~/.codex}/skills`
  - Project: `./.codex/skills`
- Existing destination skill directories are backed up under `.backup/gh-sync-<timestamp>/`.
- Fetch method is automatic:
  - Prefer `skill-installer` if available.
  - Fallback to `git sparse-checkout` so it also works in Claude Code environments.

## Post Sync

- Verify consistency with `scripts/sync-gh-skills.sh --check`.
- Restart Codex/Claude Code if updated skills are not reflected immediately.
