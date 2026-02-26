---
name: unity-csharp-navigate
description: Explore C# code in Unity projects — read files, search symbols, find references, and list packages.
allowed-tools: Bash, Read, Grep, Glob
---

# C# Code Exploration

Navigate and search C# source using the unity-cli local tools (LSP-backed).

## Commands

```bash
# Read source files
unity-cli raw read --json '{"path":"Assets/Scripts/Player.cs"}'
unity-cli raw read --json '{"path":"Assets/Scripts/Player.cs","startLine":10,"maxLines":20}'

# Search code
unity-cli raw search --json '{"pattern":"OnCollisionEnter","path":"Assets/Scripts"}'

# Symbol navigation
unity-cli raw get_symbols --json '{"path":"Assets/Scripts/Player.cs"}'
unity-cli raw find_symbol --json '{"name":"PlayerController","kind":"class","scope":"assets"}'
unity-cli raw find_refs --json '{"name":"Health","scope":"assets","pageSize":20}'

# Index management
unity-cli raw build_index --json '{}'

# Packages
unity-cli raw list_packages --json '{}'
```

## Tips

- These commands run locally (no Unity Editor connection required).
- Build the index first with `unity-cli raw build_index` for fast symbol lookups.
- Use `path` in `search` to narrow scope.
