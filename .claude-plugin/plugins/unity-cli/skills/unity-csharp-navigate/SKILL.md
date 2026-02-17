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
unity-cli raw read --json '{"filePath":"Assets/Scripts/Player.cs"}'
unity-cli raw read --json '{"filePath":"Assets/Scripts/Player.cs","startLine":10,"endLine":30}'

# Search code
unity-cli raw search --json '{"query":"OnCollisionEnter","filePattern":"*.cs"}'

# Symbol navigation
unity-cli raw get_symbols --json '{"filePath":"Assets/Scripts/Player.cs"}'
unity-cli raw find_symbol --json '{"symbolName":"PlayerController"}'
unity-cli raw find_refs --json '{"symbolName":"Health","filePath":"Assets/Scripts/Player.cs","line":15}'

# Index management
unity-cli raw get_index_status --json '{}'

# Packages
unity-cli raw list_packages --json '{}'
```

## Tips

- These commands run locally (no Unity Editor connection required).
- Build the index first with `unity-cli raw build_index` for fast symbol lookups.
- Use `filePattern` in `search` to narrow scope.
