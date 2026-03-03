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

# Rename symbol (LSP)
unity-cli raw rename_symbol --json '{"relative":"Assets/Scripts/Player.cs","namePath":"Player/Jump","newName":"Leap","apply":true}'

# Replace method body (LSP)
unity-cli raw replace_symbol_body --json '{"relative":"Assets/Scripts/Player.cs","namePath":"Player/Jump","body":"{ Debug.Log(\"jump\"); }","apply":true}'

# Insert before/after symbol (LSP)
unity-cli raw insert_before_symbol --json '{"relative":"Assets/Scripts/Player.cs","namePath":"Player/Jump","text":"public void Crouch() { }","apply":true}'
unity-cli raw insert_after_symbol --json '{"relative":"Assets/Scripts/Player.cs","namePath":"Player/Jump","text":"public void Dash() { }","apply":true}'

# Remove symbol (LSP)
unity-cli raw remove_symbol --json '{"relative":"Assets/Scripts/Player.cs","namePath":"Player/Jump","apply":true,"failOnReferences":true}'

# Validate C# text (LSP)
unity-cli raw validate_text_edits --json '{"relative":"Assets/Scripts/Player.cs","newText":"using UnityEngine;\npublic class Player : MonoBehaviour { }"}'

# Create a new class (local)
unity-cli raw create_class --json '{"name":"EnemyAI","namespace":"Game.AI","inherits":"MonoBehaviour","folder":"Assets/Scripts/AI"}'

# Read/search context
unity-cli raw read --json '{"path":"Assets/Scripts/Player.cs","startLine":1,"maxLines":120}'
unity-cli raw search --json '{"pattern":"public void Jump","path":"Assets/Scripts"}'

# Compilation
unity-cli raw get_compilation_state --json '{}'
```

## LSP Write Tools

All LSP write tools accept `apply` (boolean, default false):
- `apply: false` — preview mode, returns diff without modifying files
- `apply: true` — applies changes to disk

The `namePath` parameter navigates nested symbols with `/` separators:
- `"Player"` — top-level class
- `"Player/Jump"` — method Jump inside class Player
- `"Player/Config/MaxSpeed"` — field in nested type

## Tips

- Run `update_index` after edits for accurate symbol lookup.
- Use `get_compilation_state` to check for errors after changes.
- Preview changes with `apply: false` before applying.
- Use `validate_text_edits` to check C# syntax before writing files.
- `create_class` creates the file and directory structure atomically.
