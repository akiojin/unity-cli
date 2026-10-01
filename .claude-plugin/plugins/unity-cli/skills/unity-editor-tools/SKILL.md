---
name: unity-editor-tools
description: Inspect and control Unity Editor state with unity-cli. Use when the user asks to open or close an Editor, run a headless host, wait for readiness, build a standalone Player, bake lighting, NavMesh or occlusion, poll build/bake jobs, evaluate C#, read console output, update settings, run menus, inspect windows or capture profiler data. Do not use for package operations; use `unity-package-management`. For C# edits use `unity-csharp-edit`; for URP and Volume setup with visual verification use `unity-urp-setup`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.3.2
  category: editor
  triggers:
    - editor
    - console
    - profiler
    - menu
    - package
    - setting
  siblings:
    - unity-audio-setup
    - unity-localization
    - unity-urp-setup
    - unity-ui-toolkit-build
    - unity-cli-usage
    - unity-csharp-edit
    - unity-asset-management
    - unity-playmode-testing
    - unity-package-management
---

# Editor Tools

Use this skill for editor-wide diagnostics and control: console, project settings, menu items, windows, selection, and profiler. Hand off to a domain skill once the request narrows to scene, asset, package, or code work.

## Use When

- The user asks for editor health checks, console logs, or profiler data.
- The user wants to launch or close a project's Editor, wait for readiness, or keep a headless Editor running.
- The user wants a standalone Player build or lighting, NavMesh, NavMeshSurface, or occlusion bake and its job status.
- The user wants to inspect or change a project setting.
- The user wants to run a menu item, inspect windows, or manipulate the current selection.
- The user explicitly wants a short C# expression or synchronous statement evaluated in the Editor.

## Do Not Use When

- Configure AudioClip import, AudioMixer routing and AudioSource playback as one
  verified workflow: use `unity-audio-setup`.
- Configure Locale/String Table assets and verify localized UI text: `unity-localization`.

- Build a complete UXML/USS UI Toolkit screen and verify its interactions: use `unity-ui-toolkit-build`.
- The work is UPM package discovery/install/update/removal or scoped registries; use `unity-package-management`.
- The task is scene creation or prefab editing (rather than a bake job); use `unity-scene-create` or `unity-prefab-workflow`.
- The work is asset import or material edits; use `unity-asset-management`.
- The work is a C# refactor; use `unity-csharp-edit`.
- The user only needs Play Mode or test execution; use `unity-playmode-testing`.

## Preferred Flow

### Start and stop a local Editor

```bash
unity-cli editor open --project-path /path/to/project --wait-ready 300
unity-cli editor status --project-path /path/to/project --output json
unity-cli editor close --project-path /path/to/project
# To keep a batch host running:
unity-cli editor open --project-path /path/to/project --headless --wait-ready 300
unity-cli raw get_hierarchy --project-path /path/to/project --json '{}'
unity-cli editor close --project-path /path/to/project
```

The project needs the Bridge installed. `open` selects the version in
`ProjectSettings/ProjectVersion.txt` from Unity Hub's default path (or
`UNITY_EDITOR_PATH`). Missing Editor: exit 4; follow the returned
`unity install <version>` instruction. Installation/licensing belong to the
official Unity CLI. `--wait-ready` waits for this project's lockfile and ping;
without it, launch returns immediately. A timeout leaves the Editor running.
Use `status` (`stopped`, `starting`, `compiling`, `safe_mode`, `ready`) and
`Logs/unity-cli-editor.log` to diagnose startup.

`--headless` runs `-batchmode -nographics` with the Bridge enabled and no `-quit`.
`close` waits for process exit and refuses dirty scenes with `UNSAVED_SCENES`.
Save the scenes first; use `close --force` only when discarding unsaved changes
is intended. Legacy `raw quit_editor` without `force` does not provide this guard.

### Connected Editor operations

1. If the Editor is stopped, use `editor open --project-path <project> --wait-ready 300`; then verify connectivity with `unity-cli system ping` and `get_editor_state`.
2. Read state before mutating it: console before clearing, settings before updating, profiler status before start/stop.
3. Apply one editor-wide change at a time and verify the result immediately.
4. Capture before/after state when project settings change.

```bash
unity-cli system ping
unity-cli raw get_editor_state --json '{}'
unity-cli raw read_console --json '{"count":20}'
unity-cli raw update_project_settings --json '{"confirmChanges":true,"player":{"companyName":"MyStudio"}}'
unity-cli raw profiler_start --json '{}'
unity-cli raw profiler_stop --json '{}'
```

## Examples

### Player build and scene bake jobs

Run from Edit Mode after compilation/import finishes. Save the target scene first.
The build target must already be active with its support module installed. Paths
refer to the Editor host; use a new/empty output parent outside the project assets.
Replace scene/output paths and the returned IDs below with values for your project.

```bash
unity-cli raw build_player --json '{"target":"StandaloneOSX","scenes":["Assets/Scenes/Generated/E2E/Level.unity"],"outputPath":"/private/tmp/unity-cli-skill-build/Player.app","development":true}'
unity-cli raw get_build_status --json '{"buildId":"<returned-build-id>"}'
unity-cli raw start_scene_bake --json '{"target":"navmesh-surface","scenePath":"Assets/Scenes/Generated/E2E/Level.unity","surfacePath":"/BakeSurface"}'
unity-cli raw get_scene_bake_status --json '{"jobId":"<returned-job-id>"}'
```

For Windows use `StandaloneWindows64` and an `.exe` output. Poll `get_build_status`
until `state` is `succeeded`, `failed`, or `interrupted`; only `succeeded` is a pass.
Inspect the report, artifacts and `changedProjectSettings` on completion.
Output ancestors must not be symbolic links; on macOS use `/private/tmp`, not `/tmp`.

Bake targets are `lighting`, `navmesh-legacy`, `navmesh-surface`, and `occlusion`.
Load only the target scene and close prefab/asset previews before baking (a preview
scene also triggers `MULTIPLE_SCENES`). Use its saved path and contributing geometry.
Lighting needs lightmap static geometry and a baked light; occlusion needs occluder
geometry. `navmesh-surface` additionally needs AI Navigation and a NavMeshSurface at
`surfacePath`. Omit `surfacePath` for other targets. Poll `get_scene_bake_status`
until `status` is `succeeded` or `failed`, then check saved artifacts and verification.
An accepted start is not completion; never start duplicate jobs while polling.

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
- [editor-ops-checklist.md](references/editor-ops-checklist.md): safer sequence for settings or profiler changes.
