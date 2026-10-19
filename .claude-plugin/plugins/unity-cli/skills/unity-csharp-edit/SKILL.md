---
name: unity-csharp-edit
description: Implement, fix, and refactor Unity C# code with unity-cli write tools. Use when the user wants to change behavior in .cs files, create a new script, class, MonoBehaviour or ScriptableObject, rewrite scripts, fix compile errors, update multiple C# files together, rename a symbol, add or remove members, or change project or package settings as part of a code change. Do not use for read-only inspection with no planned edit; use `unity-csharp-navigate` instead.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob, Edit, Write
metadata:
  author: akiojin
  version: 0.3.2
  category: code
  triggers:
    - csharp
    - script
    - new script
    - class
    - monobehaviour
    - scriptableobject
    - compile error
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

With `--output json`, results use `{success, command, data, errors, warnings}`. Check the exit status and envelope `success` first; tool-result fields in this skill are relative to `data`. Read failure codes from `errors[0].code`; see `unity-cli-usage` for exit-code recovery.

Implement Unity C# changes with `unity-cli` as the primary write path. Prefer the smallest write primitive that keeps the change correct, and preserve Unity serialization, UI binding paths, and editor/runtime boundaries.

## Use When

- The user intends to change C# behavior in `.cs` files.
- The user wants to create a new script, class, `MonoBehaviour`, or `ScriptableObject`, or rewrite scripts (single or coordinated multi-file).
- The user wants to fix C# compile errors.
- The user asks for a symbol rename, member add/remove, or refactor.
- The user changes project or package settings as part of a code change.

## Do Not Use When

- Build a complete UXML/USS UI Toolkit screen and verify its interactions: use `unity-ui-toolkit-build`.

- The task is read-only investigation; use `unity-csharp-navigate`.
- The work targets Editor state inspection (console, profiler, packages); use `unity-editor-tools`.

## Preferred Flow

1. Decide the write shape: new file, symbol edit, full-file write, multi-file write, or settings change. Always write `.cs` files through the LSP write tools (`create_csharp_file`, `write_csharp_file`, `apply_csharp_edits`, symbol edits) so the text is validated before it lands; do not hand-write `.cs` files with direct file edits unless the LSP write path fails (see LSP Write Failure Fallback).
2. Read only the minimum context with `read`, `get_symbols`, `find_symbol`, or `find_refs`.
3. Preview risky symbol edits with `apply: false`.
4. Apply with `refresh`, `waitForCompile`, and `updateIndex` when later steps depend on clean diagnostics.
5. Inspect `changedFiles`, `changedSymbols`, and `diagnostics`; fix compilation issues before unrelated follow-up.

```bash
unity-cli raw create_csharp_file --json '{"relative":"Assets/Scripts/EnemyAI.cs","text":"using UnityEngine;\n\npublic class EnemyAI : MonoBehaviour\n{\n}\n","validate":true,"apply":true,"waitForCompile":true,"updateIndex":true}'
unity-cli raw get_symbols --json '{"path":"Assets/Scripts/Player.cs"}'
unity-cli raw rename_symbol --json '{"relative":"Assets/Scripts/Player.cs","namePath":"Player/Jump","newName":"Leap","apply":false}'
unity-cli raw apply_csharp_edits --json '{"files":[{"relative":"Assets/Scripts/IPlayerMover.cs","newText":"public interface IPlayerMover { void Move(); }\n"}],"validate":true,"apply":true,"waitForCompile":true,"updateIndex":true}'
unity-cli raw get_compilation_state --json '{}'
```

## LSP Write Failure Fallback

Use this only when an LSP write tool fails: the LSP server fails to start, the call times out, or the tool returns an unsupported-edit error. Retry once if the failure looks transient; do not stop the task.

1. Make the same change with your own `Edit` (existing file) or `Write` (new file). Keep it identical in scope to the intended LSP write.
2. Refresh and confirm compilation with `unity-cli`:
   ```bash
   unity-cli raw refresh_assets --json '{}'
   unity-cli raw get_compilation_state --json '{}'
   ```
   Fix any reported errors before continuing; if the Editor is unreachable, say so and leave compilation unconfirmed.
3. In your work report, state that the LSP path was not used, which tool failed, and why (error code or message).

## Examples

- "Implement dash cooldown in `PlayerController.cs` and update the related tests."
- "Create an `EnemyAI` MonoBehaviour in `Assets/Scripts/AI`."
- "Fix the compile error in `PlayerController.cs`."
- "Rename `Jump` to `Leap` across the player package without breaking serialized field names."
- "Create a new editor utility script and change the matching project setting."
- "Refactor an interface, its implementation, and the tests in one coordinated write."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [file-write-recipes.md](references/file-write-recipes.md): choosing between symbol edits, file writes, and multi-file writes.
- [lsp-write-safety.md](references/lsp-write-safety.md): preview-first edits, `namePath`, and write-result checks.
- [unity-implementation-guidelines.md](references/unity-implementation-guidelines.md): serialized fields, UI bindings, editor code, ScriptableObjects, SettingsProviders.
- [settings-write-recipes.md](references/settings-write-recipes.md): project and package settings safe-write recipes.
