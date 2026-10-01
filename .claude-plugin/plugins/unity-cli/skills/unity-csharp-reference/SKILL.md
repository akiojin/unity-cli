---
name: unity-csharp-reference
description: Browse Unity Technologies' official UnityCsReference C# source as a read-only local cache. Use when the user asks about the exact signature, behavior, or internal implementation of a Unity API, when comparing API differences between Unity versions, or when validating LLM-suggested Unity code against the canonical source. Do not use for editing project code (use `unity-csharp-edit`). Do not use for project-local script reading (use `unity-csharp-navigate`).
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.1.0
  category: code
  triggers:
    - reference
    - unity-api
    - signature
    - unity-source
    - version-diff
  siblings:
    - unity-csharp-navigate
    - unity-csharp-edit
    - unity-scene-inspect
---

# Unity C# Reference

With `--output json`, results use `{success, command, data, errors, warnings}`. Check the exit status and envelope `success` first; tool-result fields in this skill are relative to `data`. Read failure codes from `errors[0].code`; see `unity-cli-usage` for exit-code recovery.

Browse Unity Technologies' official UnityCsReference C# source as a read-only local cache. This skill is the sibling of `unity-csharp-navigate` (project sources) and `unity-csharp-edit` (writes). Hand off as soon as the request implies project-level reading or a write.

## Use When

- The user asks about the exact signature, attributes, or behavior of a Unity API such as `UnityEngine.Animator.Play` or `UnityEditor.AssetDatabase.Refresh`.
- The user wants to read the internal implementation of a Unity type to predict performance characteristics or side effects.
- The user compares API differences between Unity versions (LTS vs Tech release branches).
- The user wants to validate an LLM-suggested Unity script against the canonical Unity source.

## Do Not Use When

- The user wants to read project-local scripts; use `unity-csharp-navigate`.
- The user wants to write or refactor C# code; use `unity-csharp-edit`.
- The user wants Unity scene state, packages, or assets; use the matching scene, asset, or editor skill family.

## Preferred Flow

1. Run the runtime checklist in [runtime-checklist.md](references/runtime-checklist.md) before the first fetch in a new environment.
2. Populate the cache for the project's Unity version with `unity-cli reference fetch --accept-license` (one-time per version).
3. Use `unity-cli reference grep` for line-level pattern lookups with optional context, or `unity-cli reference search` for filtered file hits.
4. Open the candidate file with `unity-cli reference view --start-line N --max-lines M` to read the relevant span.
5. Confirm the signature or behavior, then hand off to `unity-csharp-navigate` for project sources or `unity-csharp-edit` for writes.

```bash
unity-cli reference fetch --accept-license
unity-cli reference status --output json
unity-cli reference find-symbol --name Animator --kind class
unity-cli reference grep "class Animator " --context 3
unity-cli reference view Runtime/Export/Animation/Animator.bindings.cs --start-line 100 --max-lines 60
unity-cli reference diff --from 2022.3.10f1 --to 2023.2.20f1 --symbol UnityEngine.Animator
unity-cli reference resolve-symbol-at Assets/Scripts/Player.cs --line 42 --column 18
unity-cli reference embed-build --version 2023.2.20f1
unity-cli reference embed-search --query "animator state callback" --version 2023.2.20f1
unity-cli reference clean --keep 1 --dry-run
```

Use `reference find-symbol` first when you already know the type name (class / interface / struct / enum). It is backed by an on-disk index per Unity version (`~/.unity/cache/UnityCsReference/<version>/.unity-cli-index/symbols.json`) and is faster than `grep` for repeated lookups. Drop back to `grep` for free-text patterns or member-level matches.

Use `reference diff` to compare two cached Unity versions. The default symbol-only mode (`--symbol <fqn>`) returns a single before/after pair, and the opt-in path mode (`--path <subpath> [--max-symbols N]`) lists added / removed / changed type definitions in a directory.

Use `reference resolve-symbol-at <project-rel-path> --line <n> --column <m>` when the user is on a specific cursor position in a project file (`Assets/...` or `Packages/...`) and wants to see the canonical Unity reference for the type under the cursor. The tool is a thin wrapper: it reads the project file, extracts the identifier at the cursor, and feeds it through `reference find-symbol` + `reference view` for each cached version.

Use `reference embed-build` + `reference embed-search --query "..."` for semantic / natural-language lookup when the user has a concept (`"animator state callback"`) instead of an exact symbol name. The first call downloads the BGE-Small-EN ONNX model (~130MB, cached under the platform-default fastembed cache) and writes a per-version `.unity-cli-index/embeddings.bin`. Subsequent `embed-search` calls only run a single query embedding and a linear cosine scan over that file, so they stay fast. The result is sorted by `score` (cosine similarity, higher is closer); `find-symbol` and `grep` remain the right tools when the exact symbol name is already known.

## Examples

- "Show me how Unity implements `Animator.Play` so I can predict whether it allocates."
- "Compare `UnityWebRequest.Get` between Unity 2022.3 and Unity 6 staging."
- "Confirm the exact signature of `EditorApplication.delayCall` before I wire it into the build pipeline."

## References

- [runtime-checklist.md](references/runtime-checklist.md): runtime prerequisites and license acceptance before the first fetch.
- [fetch-and-cache.md](references/fetch-and-cache.md): `reference fetch`, `status`, and `clean` command details and branch mapping per Unity version.
- [symbol-lookup-playbook.md](references/symbol-lookup-playbook.md): the canonical reference → navigate → edit workflow when validating LLM-suggested Unity code.
