---
name: unity-csharp-edit
description: Edit, create, rename, and refactor C# code in Unity projects using unity-cli.
allowed-tools: Bash, Read, Grep, Glob
---

# C# Change Workflow

Plan and verify C# changes with unity-cli local tools, then apply file edits in the repository.

## Commands

```bash
# Index
unity-cli raw build_index --json '{}'
unity-cli raw update_index --json '{"paths":["Assets/Scripts/Player.cs"]}'

# Locate target symbols before editing
unity-cli raw get_symbols --json '{"path":"Assets/Scripts/Player.cs"}'
unity-cli raw find_symbol --json '{"name":"Player","kind":"class","scope":"assets"}'
unity-cli raw find_refs --json '{"name":"Player","kind":"class","scope":"assets"}'

# Read/search context
unity-cli raw read --json '{"path":"Assets/Scripts/Player.cs","startLine":1,"endLine":120}'
unity-cli raw search --json '{"pattern":"public void Jump","include":"Assets/**/*.cs"}'

# Compilation
unity-cli raw get_compilation_state --json '{}'
```

## Tips

- Run `update_index` after edits for accurate symbol lookup.
- Use `get_compilation_state` to check for errors after changes.
- Apply code edits via repository patching, then validate with `get_compilation_state`.
