---
name: unity-csharp-navigate
description: Explore Unity C# code without modifying files. Use when the user asks to read scripts, search text, find a symbol, trace references, inspect namespaces or package sources, or understand where a class, method, or field is used. Do not use for UPM package discovery, installation or updates; use `unity-package-management`. For code edits use `unity-csharp-edit`; for scene inspection use `unity-scene-inspect`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.3.0
  category: code
  triggers:
    - csharp
    - read
    - search
    - reference
    - symbol
  siblings:
    - unity-csharp-edit
    - unity-csharp-reference
    - unity-scene-inspect
    - unity-package-management
---

# Unity C# Navigate

Navigate and search C# source via `unity-cli` local tools. This is the read-only sibling of `unity-csharp-edit`; hand off as soon as the request implies a write.

## Use When

- The user wants to inspect a script or understand existing C# behavior.
- The user asks where a symbol is defined or referenced.
- The user wants to search packages or source trees before making a change.

## Do Not Use When

- The user wants UPM registry discovery or dependency changes; use `unity-package-management`.
- The user wants to modify code or create new C# files; use `unity-csharp-edit`.
- The task depends on Unity scene state rather than source; use `unity-scene-inspect`.

## Preferred Flow

1. Build or refresh the index before symbol-heavy queries.
2. Anchor on the right file with `read` or `search`.
3. Use `get_symbols`, `find_symbol`, and `find_refs` once the target identifier is clear.
4. Narrow the search path or page size if the result set is large.
5. Use `list_packages` when the question might involve packages rather than project sources.

```bash
unity-cli raw list_packages --json '{}'
unity-cli raw read --json '{"path":"Assets/Scripts/Player.cs"}'
unity-cli raw search --json '{"pattern":"OnCollisionEnter","path":"Assets/Scripts"}'
unity-cli raw find_symbol --json '{"name":"PlayerController","kind":"class","scope":"assets"}'
unity-cli raw find_refs --json '{"name":"Health","scope":"assets","pageSize":20}'
```

## Examples

- "Read `PlayerController.cs` and explain how jumping works."
- "Find every reference to `Health` in project scripts."
- "List installed packages and check whether Input System is present."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [code-search-playbook.md](references/code-search-playbook.md): indexing strategy, path narrowing, and reference tracing guidance.
