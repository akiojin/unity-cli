# LSP Write Safety

## Preview First

- Use `apply: false` for `rename_symbol`, `replace_symbol_body`, `insert_before_symbol`, `insert_after_symbol`, and `remove_symbol` when the scope is not trivial.
- Describe the intended edit in terms of symbol names before applying it.

## Result Contract

- Treat `success`, `applied`, `changedFiles`, `changedSymbols`, `diagnostics`, `diffPreview`, `reason`, and `backend` as the canonical write result.
- `success: true` with `applied: false` usually means preview mode or a no-op. Inspect `reason` and `diffPreview` before assuming the change landed.
- Use `changedFiles` and `changedSymbols` to confirm the actual scope of a write, especially after multi-file or fallback-heavy operations.

## Symbol Targeting

- Use `get_symbols` to confirm the exact `namePath`.
- Keep `namePath` slash-separated from outer type to inner member.
- Use `find_refs` before destructive renames or removals.
- If `namePath` is unstable or incomplete, switch to `write_csharp_file` or `apply_csharp_edits`.

## Validation

1. Apply the smallest viable edit.
2. Enable `refresh`, `waitForCompile`, and `updateIndex` when later steps depend on fresh editor state, diagnostics, or symbol search.
3. Run `get_compilation_state` for non-trivial edits or when diagnostics are still unclear.
4. Report and fix real diagnostics before attempting unrelated follow-up changes.

## New Files

- Use `create_csharp_file` for every new `.cs` file (classes, `MonoBehaviour`, `ScriptableObject`, interfaces, enums, tests). It validates the text through LSP before writing.
- `create_class` (`name`, `folder`, optional `namespace` / `inherits`) is a shortcut that renders an empty class shell and writes it through the same LSP path as `create_csharp_file`. Prefer `create_csharp_file` when the file needs any members.
- Do not create or edit `.cs` files with direct file writes; they skip LSP validation. The only exception is the `LSP Write Failure Fallback` in `SKILL.md`.
- Use `validate_text_edits` when constructing larger manual replacements.

## Compile Error Fixes

- Read the error `file` / `line` from `get_compilation_state` or `editorLog.compileErrors[]`.
- Fix it with the smallest LSP write: `replace_symbol_body` for one member, `write_csharp_file` for one file, `apply_csharp_edits` when several files must change together.
- These tools run locally, so they work in `SAFE_MODE` when the bridge is not loaded.
