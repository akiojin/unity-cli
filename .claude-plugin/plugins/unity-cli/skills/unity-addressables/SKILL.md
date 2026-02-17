---
name: unity-addressables
description: Manage, build, and analyze Unity Addressables groups and content using unity-cli.
allowed-tools: Bash, Read, Grep, Glob
---

# Addressables Operations

Manage Addressable Asset groups, build content, and analyze bundles.

## Commands

```bash
# Group management
unity-cli raw addressables_manage --json '{"action":"list_groups"}'
unity-cli raw addressables_manage --json '{"action":"create_group","groupName":"Characters"}'
unity-cli raw addressables_manage --json '{"action":"add_entry","groupName":"Characters","assetPath":"Assets/Prefabs/Hero.prefab","address":"hero"}'
unity-cli raw addressables_manage --json '{"action":"remove_entry","address":"hero"}'

# Build
unity-cli raw addressables_build --json '{"action":"build"}'
unity-cli raw addressables_build --json '{"action":"clean_build"}'

# Analysis
unity-cli raw addressables_analyze --json '{"action":"analyze"}'
unity-cli raw addressables_analyze --json '{"action":"fix_issues"}'
```

## Tips

- Run `analyze` before `build` to catch dependency issues.
- Use `clean_build` when changing group structure.
