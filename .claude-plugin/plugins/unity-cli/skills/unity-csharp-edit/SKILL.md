---
name: unity-csharp-edit
description: Edit, create, rename, and refactor C# code in Unity projects using unity-cli.
allowed-tools: Bash, Read, Grep, Glob
---

# C# Code Editing & Refactoring

Create, edit, and refactor C# source files.

## Commands

```bash
# Snippet editing
unity-cli raw edit_snippet --json '{"filePath":"Assets/Scripts/Player.cs","startLine":10,"endLine":15,"newContent":"    public float speed = 5f;"}'

# Structured editing
unity-cli raw edit_structured --json '{"filePath":"Assets/Scripts/Player.cs","edits":[{"type":"add_method","className":"Player","code":"public void Jump() { }"}]}'

# Create new class
unity-cli raw create_class --json '{"className":"EnemyAI","namespace":"Game.AI","baseClass":"MonoBehaviour","savePath":"Assets/Scripts/AI/"}'

# Refactoring
unity-cli raw rename_symbol --json '{"oldName":"Health","newName":"HitPoints","filePath":"Assets/Scripts/Player.cs"}'
unity-cli raw remove_symbol --json '{"symbolName":"UnusedMethod","filePath":"Assets/Scripts/Player.cs"}'

# Index
unity-cli raw build_index --json '{}'
unity-cli raw update_index --json '{"filePath":"Assets/Scripts/Player.cs"}'

# Compilation
unity-cli raw get_compilation_state --json '{}'
```

## Tips

- Run `update_index` after edits for accurate symbol lookup.
- Use `get_compilation_state` to check for errors after changes.
- `edit_structured` supports: `add_method`, `add_field`, `add_using`, `remove_method`.
