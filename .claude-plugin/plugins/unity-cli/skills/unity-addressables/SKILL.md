---
name: unity-addressables
description: Manage Unity Addressables groups and content with unity-cli. Use when the user asks to list or create a group, add or remove an entry, build or clean addressables content, or analyze and fix addressables issues before a build. Do not use for generic import settings or material edits; use `unity-asset-management` instead.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.3.0
  category: assets
  triggers:
    - addressables
    - group
    - entry
    - bundle
    - content-build
  siblings:
    - unity-asset-management
---

# Addressables

With `--output json`, results use `{success, command, data, errors, warnings}`. Check the exit status and envelope `success` first; tool-result fields in this skill are relative to `data`. Read failure codes from `errors[0].code`; see `unity-cli-usage` for exit-code recovery.

Manage Addressable asset groups, build content, and analyse bundles. This skill is the content-delivery sibling of `unity-asset-management`.

## Use When

- The user wants to manage Addressables groups or entries.
- The task involves building or cleaning Addressables content.
- The user wants an analysis pass or automatic issue fix before shipping content.

## Do Not Use When

- The task is about general asset import settings or dependency analysis outside Addressables; use `unity-asset-management`.
- The request is about scene object setup rather than content delivery; use `unity-gameobject-edit` or `unity-scene-create`.

## Preferred Flow

1. Inspect existing groups before creating or moving entries.
2. Apply group or entry changes with `addressables_manage`.
3. Run `addressables_analyze` before a build, and use `fix_issues` only when the reported changes are acceptable.
4. Build with `addressables_build`, using `clean_build` when structure changed substantially.

```bash
unity-cli raw addressables_manage --json '{"action":"list_groups"}'
unity-cli raw addressables_manage --json '{"action":"create_group","groupName":"Characters"}'
unity-cli raw addressables_manage --json '{"action":"add_entry","groupName":"Characters","assetPath":"Assets/Prefabs/Hero.prefab","address":"hero"}'
unity-cli raw addressables_analyze --json '{"action":"analyze"}'
unity-cli raw addressables_build --json '{"action":"clean_build"}'
```

## Examples

- "Create an Addressables group for character prefabs and add the hero prefab."
- "Analyze Addressables issues and then build content."
- "Run a clean Addressables build after reorganising groups."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [addressables-build-loop.md](references/addressables-build-loop.md): safer order for group changes, analysis, and clean builds.
