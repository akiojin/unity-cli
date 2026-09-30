---
name: unity-editor-tools
description: Inspect and control Unity Editor state with unity-cli. Use when the user asks to evaluate a short C# snippet, ping the editor, read console output, inspect or update a project setting, run a menu item, inspect windows or selection, manage packages, or capture profiler data. Do not use for C# file edits; use `unity-csharp-edit`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.3.0
  category: editor
  triggers:
    - editor
    - console
    - profiler
    - menu
    - package
    - setting
  siblings:
    - unity-cli-usage
    - unity-csharp-edit
    - unity-asset-management
    - unity-playmode-testing
---

# Editor Tools

Use this skill for editor-wide diagnostics and control: console, project settings, menu items, windows, selection, package manager, and profiler. Hand off to a domain skill once the request narrows to scene, asset, or code work.

## Use When

- The user asks for editor health checks, console logs, or profiler data.
- The user wants to inspect or change a project setting.
- The user wants to run a menu item, inspect windows, or manipulate the current selection.
- The user wants package manager or registry operations from the editor side.
- The user explicitly wants a short C# expression or synchronous statement evaluated in the Editor.

## Do Not Use When

- The task is primarily about a specific scene or prefab; use `unity-scene-create` or `unity-prefab-workflow`.
- The work is asset import or material edits; use `unity-asset-management`.
- The work is a C# refactor; use `unity-csharp-edit`.
- The user only needs Play Mode or test execution; use `unity-playmode-testing`.

## Preferred Flow

1. Verify connectivity with `unity-cli system ping` and `get_editor_state`.
2. Read state before mutating it: console before clearing, settings before updating, profiler status before start/stop.
3. Apply one editor-wide change at a time and verify the result immediately.
4. Capture before/after state when project settings or packages change.

```bash
unity-cli system ping
unity-cli raw get_editor_state --json '{}'
unity-cli raw read_console --json '{"count":20}'
unity-cli raw update_project_settings --json '{"confirmChanges":true,"player":{"companyName":"MyStudio"}}'
unity-cli raw profiler_start --json '{}'
unity-cli raw profiler_stop --json '{}'
unity-cli raw package_manager --json '{"action":"install","packageId":"com.unity.inputsystem"}'
```

## Examples

### Evaluate a short C# snippet

```bash
unity-cli editor eval '1+2' --request-id sum --output json
unity-cli editor eval 'var go = new GameObject("Probe"); return go;' --mode statements --request-id create-probe --output json
unity-cli editor eval-status create-probe --output json
unity-cli editor eval-stats --output json
```

Inspect `state`: only `completed` means execution and value conversion succeeded.
`compile_error`, `runtime_error`, and `serialization_error` are distinct failures.
Primitive JSON values preserve their types; Unity objects return reference descriptors.
Evaluation supports Edit/Play Mode, implicit System/UnityEngine/UnityEditor namespaces,
and loaded assembly references. Async/await and persistent REPL sessions are unsupported.
Use dedicated tools when they cover the operation; eval does not require a persistent
`.cs` file or MenuItem. File edits still belong to `unity-csharp-edit`.

A timeout only stops waiting; it does not stop code or undo changes. Query the same
request ID with `eval-status`; never automatically resend with a new ID. Identical
input under a stored ID reuses the result. Domain Reload loses results, so `unknown`
requires inspecting actual state before deciding whether to execute again. Long-running
synchronous code also blocks status responses. The newest 256 request IDs are kept;
older IDs read as `unknown`.

Eval is fast when warm: identical `code` reuses its compiled assembly (the code still
runs on every call) and costs about one ordinary command; new source adds one compile;
the first call after a Domain Reload is the slowest. To repeat an operation, resend the
same `code` instead of generating a new string per call. Each distinct source emits one
assembly, and after 128 per domain new source returns `reload_required` (already
compiled source keeps running); eval never reloads automatically. `editor eval-stats`
shows the domain's emitted-assembly, cache and memory counters.

- "Show me the latest Unity console errors."
- "Update the company name in Project Settings."
- "Start the profiler, record briefly, stop, and report the status."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [editor-ops-checklist.md](references/editor-ops-checklist.md): safer sequence for settings, package, or profiler changes.
