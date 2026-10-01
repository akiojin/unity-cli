---
name: unity-csharp-edit
description: Implement, fix, and refactor Unity C# code with unity-cli write tools. Use when the user wants to change behavior in .cs files, create or rewrite scripts, update multiple C# files together, rename a symbol, add or remove members, or change project or package settings as part of a code change. Do not use for read-only inspection with no planned edit; use `unity-csharp-navigate` instead.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob, Edit, Write
metadata:
  author: akiojin
  version: 0.3.0
  category: code
  triggers:
    - csharp
    - script
    - rename
    - refactor
    - symbol
    - setting
  siblings:
    - unity-ui-toolkit-build
    - unity-csharp-navigate
    - unity-csharp-reference
    - unity-editor-tools
    - unity-cli-usage
    - unity-gameobject-edit
---

# Unity C# Edit

Implement Unity C# changes with `unity-cli` as the primary write path. Prefer the smallest write primitive that keeps the change correct, and preserve Unity serialization, UI binding paths, and editor/runtime boundaries.

## Use When

- The user intends to change C# behavior in `.cs` files.
- The user wants to create or rewrite scripts (single or coordinated multi-file).
- The user asks for a symbol rename, member add/remove, or refactor.
- The user changes project or package settings as part of a code change.

## Do Not Use When

- Build a complete UXML/USS UI Toolkit screen and verify its interactions: use `unity-ui-toolkit-build`.

- The task is read-only investigation; use `unity-csharp-navigate`.
- The work targets Editor state inspection (console, profiler, packages); use `unity-editor-tools`.

## Preferred Flow

1. Decide the write shape: symbol edit, full-file write, multi-file write, or settings change.
2. Read only the minimum context with `read`, `get_symbols`, `find_symbol`, or `find_refs`.
3. Preview risky symbol edits with `apply: false`.
4. Apply with `refresh`, `waitForCompile`, and `updateIndex` when later steps depend on clean diagnostics.
5. Inspect `changedFiles`, `changedSymbols`, and `diagnostics`; fix compilation issues before unrelated follow-up.

```bash
unity-cli raw get_symbols --json '{"path":"Assets/Scripts/Player.cs"}'
unity-cli raw rename_symbol --json '{"relative":"Assets/Scripts/Player.cs","namePath":"Player/Jump","newName":"Leap","apply":false}'
unity-cli raw apply_csharp_edits --json '{"files":[{"relative":"Assets/Scripts/IPlayerMover.cs","newText":"public interface IPlayerMover { void Move(); }\n"}],"validate":true,"apply":true,"waitForCompile":true,"updateIndex":true}'
unity-cli raw get_compilation_state --json '{}'
```

## Examples

- "Implement dash cooldown in `PlayerController.cs` and update the related tests."
- "Rename `Jump` to `Leap` across the player package without breaking serialized field names."
- "Create a new editor utility script and change the matching project setting."
- "Refactor an interface, its implementation, and the tests in one coordinated write."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [file-write-recipes.md](references/file-write-recipes.md): choosing between symbol edits, file writes, and multi-file writes.
- [lsp-write-safety.md](references/lsp-write-safety.md): preview-first edits, `namePath`, and write-result checks.
- [unity-implementation-guidelines.md](references/unity-implementation-guidelines.md): serialized fields, UI bindings, editor code, ScriptableObjects, SettingsProviders.
- [settings-write-recipes.md](references/settings-write-recipes.md): project and package settings safe-write recipes.
