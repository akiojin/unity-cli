---
name: unity-project-bootstrap
description: Bootstrap a new Unity project with unity-cli. Use when the user wants to create a project from scratch and install the bridge, connect it, or prepare its first runnable scene. Do not use for setup or ping alone in an existing project; use `unity-cli-usage`. For scene-only authoring use `unity-scene-create`; for runtime capture alone use `unity-playmode-testing`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.1.0
  category: foundation
  triggers:
    - project
    - bootstrap
  siblings:
    - unity-cli-usage
    - unity-scene-create
    - unity-playmode-testing
    - unity-package-management
---

# Unity Project Bootstrap

With `--output json`, results use `{success, command, data, errors, warnings}`. Check the exit status and envelope `success` first; tool-result fields in this skill are relative to `data`. Read failure codes from `errors[0].code`; see `unity-cli-usage` for exit-code recovery.

Take an absent project through creation, bridge connection, a saved starter scene,
and a visible Play-mode check using existing Unity and unity-cli operations.

## Use When

- The user wants a new Unity project with the bridge installed and reachable.
- The request spans project creation, initial packages, and a first runnable scene.
- The project folder is empty or does not exist, rather than an existing project
  needing a connection repair.

## Do Not Use When

- Only install or diagnose the CLI/bridge in an existing project: `unity-cli-usage`.
- Only create, load or save a scene in an existing project: `unity-scene-create`.
- Only capture or simulate input in Play mode: `unity-playmode-testing`.
- Only manage packages: `unity-package-management`; only edit materials/imports:
  `unity-asset-management`; only interact with UI: `unity-ui-automation`.

## Preferred Flow

1. Establish the absolute project path, installed Editor version, and template /
   render pipeline from the request. Use a minimal built-in 3D project when no
   pipeline is specified, and state that choice. Preserve explicit choices.
   Check whether the destination already contains a project; never replace it
   or silently change its Editor version. Resume existing work through the
   appropriate sibling instead of running creation again.
2. Follow [project creation](references/project-creation.md) for Hub or Editor CLI.
   Wait for creation/import to finish and verify `Packages/manifest.json` and
   `ProjectSettings/ProjectVersion.txt`. Close the newly created Editor before
   bridge installation so Input System activation can be configured without a
   restart dialog. Do not close another project's Editor.
3. Follow [runtime prerequisites](references/runtime-checklist.md) to obtain the
   CLI and select this project. Explain that `setup` adds the bridge / OpenUPM
   registry to the manifest and may enable Both input backends. Then run
   `unity-cli --project-path <project> --output json setup --launch-editor`.
   With multiple Editors, choose an unused port and pass the same explicit
   `--port` on setup and every subsequent command. Require `ok: true`,
   `editor.projectMatches: true`, and a matching bridge version before authoring.
   Check `unity-cli system ping` against that same project/port. On failure use
   `doctor`, import/compile logs and `editor.hint`; do not loop blindly or create
   another project. For CLI versions without `setup`, use `bridge install`,
   open the project and verify ping per `unity-cli-usage` (#363 / #366).
4. Verify package resolution in `Packages/packages-lock.json`. The bridge brings
   Input System as a dependency; do not install an arbitrary latest version.
   Add only other packages requested by the user, using `unity-package-management`
   and the selected Editor's compatible package versions. Allow import/reload
   to settle and ping again before continuing.
5. Use `unity-scene-create` to create and save a minimal scene. The default scene
   contains a camera and directional light; inspect the hierarchy, then add a
   visible Cube. Keep one camera and avoid duplicate starter objects on retry.
   Use `Assets/Scenes/Bootstrap.unity` unless another path was requested. For
   repository E2E runs use `Assets/Scenes/Generated/E2E/Bootstrap.unity`.
6. Use `unity-playmode-testing`: enter Play, confirm `get_editor_state`, capture
   the Game view and inspect the image. Require the Cube to be visible, no
   compile/runtime errors, and a real Game capture (not an OS fallback).
   Input simulation is optional when a requested controller/action exists;
   an empty scene's device state does not prove gameplay input works. Stop Play
   and confirm the scene remains saved before reporting success.
7. Report Editor/CLI/bridge versions, project path, resolved packages, ping,
   saved scene and screenshot paths, and any remaining failures. Keep the
   manifest, scene and screenshot as evidence. If an operation is absent from
   the tool catalog, report the gap and link a separately tracked issue; do not
   add CLI/bridge functionality as part of this workflow.

## Examples

- "Create a new Unity 2022.3 project, install the bridge and confirm ping."
- "新しい Unity 6 プロジェクトを作り、Input System と最小シーンを用意して実行確認して。"
- "Create only a scene in my existing project" → `unity-scene-create`.
- "The bridge in my existing project is unreachable" → `unity-cli-usage`.

After project creation and setup, run these commands inside that project with
the selected endpoint (add the same `--project-path` / `--port` if needed):

```bash
unity-cli system ping
unity-cli scene create Bootstrap --path Assets/Scenes/
unity-cli raw get_hierarchy --json '{"includeComponents":true}'
unity-cli raw create_gameobject --json '{"name":"BootstrapCube","primitiveType":"cube"}'
unity-cli raw save_scene --json '{"scenePath":"Assets/Scenes/Bootstrap.unity"}'
unity-cli raw play_game --json '{}'
unity-cli raw get_editor_state --json '{}'
unity-cli raw capture_screenshot --json '{"captureMode":"game","width":1280,"height":720,"osFallback":false}'
unity-cli raw read_console --json '{"count":20}'
unity-cli raw stop_game --json '{}'
```

## References

- [runtime-checklist.md](references/runtime-checklist.md): CLI, setup, instance selection and connection recovery.
- [project-creation.md](references/project-creation.md): Hub/Editor CLI creation and verification artifacts.
