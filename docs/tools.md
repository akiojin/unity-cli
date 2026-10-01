# Tool Catalog

Snapshot date: `2026-09-28`

## JSON Results and Exit Codes

`--output json` writes one result envelope to stdout for both success and failure.
Diagnostics go to stderr. Tool-specific fields in this catalog are relative to
`data`; successful arrays (including `tool list` and `batch`) are also inside
`data`. `--help` and `--version` retain their normal informational output.

```json
{"success":true,"command":"system ping","data":{"message":"pong"},"errors":[],"warnings":[]}
```

```json
{"success":false,"command":"system ping","data":null,"errors":[{"code":"EDITOR_UNREACHABLE","message":"Could not connect to the Editor"}],"warnings":[]}
```

| Exit | Meaning | Automation action |
| --- | --- | --- |
| 0 | Success | Read `data` |
| 1 | General failure | Inspect `errors` and diagnostics |
| 2 | Invalid arguments (`INVALID_ARGUMENT`) | Correct the command or JSON; do not retry unchanged |
| 3 | Authentication failure (`UNAUTHORIZED`) | Correct credentials before retrying |
| 4 | Unmet precondition | Correct Editor/project settings or required capabilities |
| 6 | Operation failed, potentially retryable | Inspect the preserved Bridge code; retry only when the operation is safe |
| 7 | Editor unreachable (`EDITOR_UNREACHABLE`, `EDITOR_NOT_FOUND`) | Diagnose the target with `doctor`, then reconnect |
| 8 | Completed test run failed (`TEST_FAILED`) | Inspect `data.failures` and fix the tests/product |
| 130 | SIGINT | Interrupted |
| 143 | SIGTERM | Terminated |

Bridge failure codes are preserved verbatim in `errors[].code` and the original
failure payload is retained in `data` (including `details`). Errors without a
Bridge code use `OPERATION_FAILED`. Response timeouts use `TIMEOUT` (exit 6);
connection failures use `EDITOR_UNREACHABLE` (exit 7). A timeout does not prove
that an operation was not executed: poll its job/request ID before resubmitting.
`AMBIGUOUS_EDITOR` retains exit 6 and lists candidates in `data.candidates`.

`run_tests` starts an asynchronous job and normally exits 0. Poll
`get_test_status` until `data.status == "completed"`: the final poll exits 8 if
tests failed, or 0 if all tests passed. Starting a run is not evidence of a pass.
The signal codes are the standard shell statuses for signal termination;
abruptly terminated processes may not emit a result envelope.

Migration: use `jq '.data'` or `json.loads(stdout)["data"]` at the CLI boundary,
then read the existing tool fields. Check the exit status and `success` before
using a result. Do not treat the absence of stderr text as success.

## Command Groups (Typed Subcommands)

| Group       | Subcommands                           |
| ----------- | ------------------------------------- |
| `raw`       | (direct tool invocation)              |
| `tool`      | `list`, `schema`, `call`              |
| `system`    | `ping`                                |
| `editor`    | `eval`, `eval-status`, `eval-stats`   |
| `scene`     | `create`                              |
| `instances` | `list`, `set-active`                  |
| `cli`       | `install`, `doctor`                   |
| `lsp`       | `install`, `doctor`                   |
| `lspd`      | `start`, `stop`, `status`             |
| `unityd`    | `start`, `stop`, `status`             |
| `doctor`    | (connection diagnosis)                |
| `batch`     | (batch command execution)             |

Use `raw` for full command coverage when no typed subcommand exists.

Tool discovery:

- `tool list [--query <text>] [--category <name>] [--compact] [--limit N] [--offset N]`
- `--query` matches tool names and one-line descriptions case-insensitively.
- `--category` accepts a `### ...` heading of this catalog (plus `Reference Cache`) or its slug: `scenes`, `gameobjects`, `components`, `animator`, `timeline`, `prefabs`, `assets`, `visual-effect-graph`, `addressables`, `code-lsp`, `input-system`, `ui`, `playback-testing`, `player-builds`, `profiler`, `editor`, `screenshots-video`, `system`, `reference-cache`.
- `--compact` returns `{name, description}` entries instead of bare names. Without it, JSON `data` is an array of names.
- Category membership is checked against this file by `tool_index_matches_docs_headings`; keep the tool tables in sync when adding tools.

```bash
unity-cli tool list --query screenshot --compact --output json
unity-cli tool list --category scenes --output json
```

Managed binary notes:

- `cli install` downloads or refreshes the managed `unity-cli` copy under `UNITY_CLI_TOOLS_ROOT` (or the OS default tools directory).
- `cli doctor` reports the managed `unity-cli` path, local version, latest release metadata, and whether an update is pending.
- `unityd` and `lspd` automatically refresh managed binaries on daemon startup without an interactive confirmation step.

Connection diagnosis:

- `doctor [--project-path <dir>]` explains why the bridge is unreachable. It checks the Unity Editor process for the project, `Packages/manifest.json` (`com.akiojin.unity-cli-bridge` presence and version), the Editor.log (Safe Mode and `file(line,col): error CSxxxx` compile errors), the configured port (`--port` / `UNITY_CLI_PORT`, then `ProjectSettings/UnityCliBridgeSettings.asset`, then the default) and the process holding it, and socket permission errors.
- The JSON `diagnosis` is one of `OK`, `SAFE_MODE`, `COMPILE_ERRORS`, `BRIDGE_NOT_INSTALLED`, `PORT_IN_USE` (with `port.listenerPid`), `EDITOR_NOT_RUNNING`, `SANDBOX_BLOCKED`, or `BRIDGE_NOT_RESPONDING`, plus a `recovery` hint. The command exits 0 so the report is always readable.
- Connection failures from other commands point to `unity-cli doctor --output json`.

Global options:

- `--output text|json`
- `--dry-run` (skip mutating tools and return execution plan)

Registered tool total: 153 (`TOOL_NAMES` in `src/tooling/tool_catalog.rs`): 142 runtime/local tool APIs plus 11 Reference Cache tools.

## Runtime Tool APIs (142 tools)

### Scenes

| Tool | Description |
| ---- | ----------- |
| `create_scene` | Create a new scene |
| `get_scene_info` | Get scene metadata |
| `list_scenes` | List all scenes |
| `load_scene` | Load a scene |
| `save_scene` | Save the current scene |
| `start_scene_bake` | Start Lighting, legacy NavMesh, NavMeshSurface, or Occlusion baking |
| `get_scene_bake_status` | Poll bake progress, terminal failure, and verified saved artifacts |

### Scene baking

Use `start_scene_bake` with a `target` of `lighting`, `navmesh-legacy`,
`navmesh-surface`, or `occlusion`, and the saved active `scenePath`. Save scene
edits first and load only the target scene. Surface baking additionally requires
`surfacePath`, the hierarchy path of a GameObject with a NavMeshSurface component
from `com.unity.ai.navigation`.
Use a single navigation backend in a scene: mixed legacy data and Surface data
are rejected so that an older NavMesh cannot make a new bake appear usable.

```bash
unity-cli tool call start_scene_bake --json '{"target":"lighting","scenePath":"Assets/Scenes/Level.unity"}'
unity-cli tool call start_scene_bake --json '{"target":"navmesh-surface","scenePath":"Assets/Scenes/Level.unity","surfacePath":"/Navigation"}'
unity-cli tool call get_scene_bake_status --json '{"jobId":"<returned-job-id>"}'
```

Starting a job is not completion. Poll the returned `jobId` until `status` is
`succeeded` or `failed`. A successful job reports saved `artifacts` and verification
results; failure reports a reason/code, including missing output. Lighting verifies
lightmaps and scene lighting data; NavMesh verifies generated data and navigation
queries; Occlusion verifies saved data assigned to the scene. Each response identifies
the selected backend and Unity version. Jobs are exclusive, and interruption by an
assembly reload is a failure. Keep the target scene loaded while a job runs.

This operation rebuilds the selected bake output. Use the target's normal Unity
settings and eligible static geometry; missing packages, invalid settings, unsupported
environments, and empty output must be addressed before retrying.

### GameObjects

| Tool                     | Description                          |
| ------------------------ | ------------------------------------ |
| `create_gameobject`      | Create a new GameObject              |
| `delete_gameobject`      | Delete a GameObject                  |
| `find_gameobject`        | Find GameObjects by name or criteria |
| `get_hierarchy`          | Get scene hierarchy tree             |
| `modify_gameobject`      | Modify GameObject properties         |
| `get_gameobject_details` | Get detailed GameObject info         |

### Components

| Tool                    | Description                        |
| ----------------------- | ---------------------------------- |
| `add_component`         | Add a component to a GameObject    |
| `set_component_field`   | Set a component field value        |
| `get_component_types`   | List available component types     |
| `list_components`       | List components on a GameObject    |
| `modify_component`      | Modify component properties        |
| `remove_component`      | Remove a component                 |
| `find_by_component`     | Find GameObjects by component type |
| `get_component_values`  | Get component field values         |
| `get_object_references` | Get object reference graph         |

### Animator

| Tool                         | Description                                                                        |
| ---------------------------- | ---------------------------------------------------------------------------------- |
| `create_animator_controller` | Create an AnimatorController asset with parameters, states, and transitions        |
| `create_animation_clip`      | Create an AnimationClip asset from sprite frames with frame rate and loop settings |
| `get_animation_curves`       | Read numeric bindings, keys and tangents; identify object-reference bindings       |
| `edit_animation_curve`       | Set, upsert or remove numeric keys for one validated binding                       |
| `get_animator_runtime_info`  | Get Animator runtime info                                                          |
| `get_animator_state`         | Get current Animator state                                                         |

### Timeline

| Tool | Description |
| ---- | ----------- |
| `get_timeline` | Inspect a Timeline asset or PlayableDirector, tracks, clips, and bindings (read-only) |
| `manage_timeline` | Create and edit Timeline AnimationTracks, bind a director, or evaluate a time (mutating) |

Discover these tools with `unity-cli tool list` and inspect parameters with
`unity-cli tool schema get_timeline` or `unity-cli tool schema manage_timeline`.
Both `unity-cli tool call` and `unity-cli raw` accept the tool names.

`get_timeline` requires `assetPath` (a project asset path such as
`Assets/Timelines/Intro.playable`) or `directorPath` (the full hierarchy path of a
GameObject with a PlayableDirector, such as `/Root/Director`). Use full hierarchy
paths for `directorPath` and `animatorPath`; ambiguous object names are rejected.
Inspection includes unsupported tracks, while editing supports only top-level
`AnimationTrack` objects. Group tracks, subtracks, override/infinite tracks,
non-animation clips, and other track types are outside the editing scope.

`manage_timeline` accepts these actions and required parameters:

| Action | Required parameters (in addition to `action`) |
| ------ | ------------------------------------------ |
| `create_asset` | `assetPath` |
| `assign_director` | `assetPath`, `directorPath` |
| `create_track` | `assetPath`, `trackName` (`trackType` defaults to `AnimationTrack`) |
| `delete_track` | `assetPath`, `trackId` |
| `add_clip` | `assetPath`, `trackId`, `animationClipPath`, `start`, `duration` |
| `update_clip` | `assetPath`, `trackId`, `clipIndex`, `expectedClip`; provide updated `animationClipPath`, `start`, or `duration` |
| `remove_clip` | `assetPath`, `trackId`, `clipIndex`, `expectedClip` |
| `set_binding` | `directorPath`, `trackId`, `animatorPath` |
| `clear_binding` | `directorPath`, `trackId` |
| `evaluate` | `directorPath`, `time` |

Use the stable `trackId` (`GUID:localID`) returned by inspection, rather than a
track name. `clipIndex` is zero-based and may change after edits. For update and
remove operations, pass `expectedClip` with the inspected `animationClipPath`,
`start`, and `duration`; a stale index or mismatching clip snapshot is rejected.
Re-read `get_timeline` after each edit before targeting another clip.
Times are seconds: `start` and `time` must be finite and nonnegative; `duration`
must be finite and positive. Each time value, including those in `expectedClip`,
must be at most 1,000,000 seconds; larger values fail with `INVALID_TIME`.
The Unity bridge validates these constraints and
action-specific editing restrictions before mutation.

Asset changes save automatically. Editing a shared Timeline asset affects every
director using it. Director assignment and bindings mark the scene dirty; call
`save_scene` explicitly to persist scene changes. `evaluate` evaluates the
director at the requested time without entering Play Mode. Mutating Timeline
calls participate in `--dry-run` and are skipped without contacting Unity.

```bash
unity-cli tool call get_timeline --json '{"assetPath":"Assets/Timelines/Intro.playable"}'
unity-cli tool call manage_timeline --json '{"action":"create_asset","assetPath":"Assets/Timelines/Intro.playable"}'
unity-cli tool call manage_timeline --json '{"action":"assign_director","assetPath":"Assets/Timelines/Intro.playable","directorPath":"/Root/Director"}'
unity-cli tool call manage_timeline --json '{"action":"evaluate","directorPath":"/Root/Director","time":0.5}'
```

### Prefabs

| Tool                      | Description                                 |
| ------------------------- | ------------------------------------------- |
| `create_prefab`           | Create a Prefab or inherited Variant        |
| `get_prefab_overrides`    | List instance overrides and apply targets   |
| `manage_prefab_overrides` | Apply or revert individual or all overrides |
| `unpack_prefab`           | Unpack outermost or all Prefab connections  |
| `exit_prefab_mode`        | Exit Prefab editing mode                    |
| `instantiate_prefab`      | Instantiate a Prefab in the scene           |
| `modify_prefab`           | Modify Prefab properties                    |
| `open_prefab`             | Open a Prefab for editing                   |
| `save_prefab`             | Save Prefab changes                         |

Prefab inheritance and overrides:

- Variant creation uses the existing tools: `instantiate_prefab` the base, then
  pass that connected scene instance root to `create_prefab` with a new
  `prefabPath`. Unity saves it as an inherited Variant and connects that scene
  object to the Variant. Do not unpack the source first. Base asset edits
  propagate to fields that the Variant does not override. Keep `overwrite`
  unset to protect existing destinations.
- `get_prefab_overrides` takes a scene instance root `gameObjectPath`, including
  inactive objects. The JSON contains `properties` (`instanceId`, `propertyPath`,
  `propertyType`, `isDefaultOverride`), `objects`, `addedComponents`,
  `removedComponents` (`instanceId` of the containing object, `assetComponentId`,
  `type`, `assetPath`), and `applyTargets` (immediate Variant through its ancestors).
  IDs belong to the current Editor session; list again after reload or mutation.
- `manage_prefab_overrides` requires `gameObjectPath`, `action: apply|revert`, and
  `scope: all|property|object|added_component|removed_component`. Apply also requires
  `assetPath` from `applyTargets`, making the destination explicit. Individual
  scopes require `instanceId` from the listing; `property` additionally requires
  `propertyPath`, and `removed_component` requires `assetComponentId`. Revert restores
  the immediate source and does not accept `assetPath`. Applying to an ancestor
  rejects objects that exist only in a derived Variant. Whole-instance operations
  also handle added/removed GameObjects. Default root position/rotation overrides
  are not applied; individual apply rejects them as `DEFAULT_OVERRIDE`.
- `unpack_prefab` requires `gameObjectPath` and `mode: Outermost|Completely`.
  Outermost removes the selected outer connection while preserving nested Prefabs
  (and the base connection of a Variant). Completely removes every connection in
  that hierarchy. Inspect `isPartOfPrefabInstance` and `connectedObjectCount` in the
  response; a completely unpacked hierarchy reports `false` and `0`.
- Override and unpack writes require Edit Mode. Instance changes mark the scene dirty; save the
  scene to persist them. Asset apply saves the changed Prefab. Existing
  `save_prefab` remains available for Prefab Mode and immediate-source apply.

```bash
unity-cli raw instantiate_prefab --json '{"prefabPath":"Assets/Prefabs/Player.prefab","name":"ArmoredPlayer"}'
unity-cli raw create_prefab --json '{"gameObjectPath":"/ArmoredPlayer","prefabPath":"Assets/Prefabs/ArmoredPlayer.prefab"}'
unity-cli raw get_prefab_overrides --json '{"gameObjectPath":"/ArmoredPlayer"}'
# Replace instanceId with the component ID returned above.
unity-cli raw manage_prefab_overrides --json '{"gameObjectPath":"/ArmoredPlayer","action":"apply","scope":"property","instanceId":1234,"propertyPath":"m_Mass","assetPath":"Assets/Prefabs/ArmoredPlayer.prefab"}'
unity-cli raw manage_prefab_overrides --json '{"gameObjectPath":"/ArmoredPlayer","action":"revert","scope":"all"}'
unity-cli raw unpack_prefab --json '{"gameObjectPath":"/ArmoredPlayer","mode":"Completely"}'
```

Real Editor acceptance (isolated macOS projects, CLI calls, persisted values and
connection assertions; logs and counts are saved to `results.json`):

```bash
cargo build --bin unity-cli
python3 scripts/e2e-prefab.py --launch --editmode --versions 6000.3.25f1,2022.3.62f3 --output /tmp/prefab-e2e
```

### Assets

| Tool                           | Description                                                    |
| ------------------------------ | -------------------------------------------------------------- |
| `analyze_scene_contents`       | Analyze scene asset contents                                   |
| `manage_asset_database`        | Manage AssetDatabase operations                                |
| `analyze_asset_dependencies`   | Analyze asset dependency graph                                 |
| `manage_asset_import_settings` | Manage asset import settings                                   |
| `manage_audio_mixer`           | Create/read AudioMixers, groups and exposed Volume parameters  |
| `create_sprite_atlas`          | Create a SpriteAtlas asset with packables and packing settings |
| `create_material`              | Create a new Material                                          |
| `modify_material`              | Modify Material properties                                     |
| `refresh_assets`               | Refresh the AssetDatabase                                      |

### Visual Effect Graph

| Tool | Description |
| --- | --- |
| `vfx_describe_graph` | Describe a Visual Effect Graph asset: contexts (with settings, blocks and slots), operators, exposed parameters, slot and flow links, validation + compile `errors` (on by default; `includeErrors:false` to skip), the last `compile` outcome, and canvas `layout` diagnostics (`overlapCount` / `overlaps`). Large graphs describe big: `include` keeps only the named top-level sections (counts are always kept) and `includeSlots:false` omits the slot trees that dominate the payload, flagged by `slotsOmitted` |
| `vfx_list_library` | List available Visual Effect Graph descriptors (`kind`: block, operator, context, parameter, or template) |
| `vfx_apply` | Apply an authoring mutation to a Visual Effect Graph asset. Every op response carries a `compile` summary (`success`/`exception`/`errors`/`logs`) of the recompile it triggered; pass `autoCompile:false` to skip that recompile and batch edits, then one `compile` op to flush them (its response reports `flushedDeferred`). Ops: add_block, set_block_setting, set_block_enabled, reorder_block, move_block, move_node, group_nodes, remove_group, auto_layout, compile, duplicate_block, duplicate_operator, add_context, add_operator, add_parameter, set_parameter, link_slots, set_slot_value, set_slot_space, convert_to_property, convert_to_inline, unlink_slots, set_operator_setting, add_operator_input, remove_operator_input, set_operator_operand_type, rename_operator_input, reorder_operator_input, set_context_setting, remove_block, remove_operator, remove_parameter, rename_parameter, set_parameter_category, rename_category, reorder_category, reorder_parameter, duplicate_parameter, remove_context, delete_system, set_system_name, add_custom_attribute, link_flow, unlink_flow, set_bounds, add_sticky_note, update_sticky_note, remove_sticky_note, reorder_sticky_note, set_instancing, set_initial_event_name, create_subgraph_asset, create_from_template, insert_template, designate_template |
| `vfx_runtime` | Control a VisualEffect component at runtime via its public API (ops: set_asset, set_float, set_int, set_bool, set_vector2/3/4, set_texture, set_mesh, send_event, set_initial_event_name, reinit, simulate, get_state). `simulate` advances the live sim via `VisualEffect.Simulate(deltaTime, steps)` — params `deltaTime` (0.05) and `steps` (1). |
| `vfx_bake_sdf` | Bake a Mesh asset into a Signed Distance Field Texture3D asset (programmatic SDF Bake Tool, via the public `MeshToSDFBaker`). Params: `meshPath`, `outputPath`, `maxResolution` (64), `center`/`size` (default = mesh bounds), `signPassCount` (1), `threshold` (0.5), `sdfOffset` (0), `overwrite` (false). Requires compute shader support. |
| `vfx_settings` | Read or write VFX environment settings — ops: `get` (read all), `set` (write one named setting). `scope`: `project` (default — `ProjectSettings/VFXManager.asset`; `fixedTimeStep`, `maxDeltaTime`, `maxCapacity`, and the Object-ref plumbing `m_IndirectShader`/`m_CopyBufferShader`/`m_SortShader`/`m_StripUpdateShader`/`m_RuntimeResources` set by asset path, ...) or `preferences` (per-machine EditorPrefs via `UnityEditor.VFX.VFXViewPreference`; `instancingEnabled`, `displayExperimentalOperator`, `multithreadUpdateEnabled`, `allowShaderExternalization`, ...) |

### Addressables

| Tool                   | Description                            |
| ---------------------- | -------------------------------------- |
| `addressables_analyze` | Analyze Addressables configuration     |
| `addressables_build`   | Build Addressables content             |
| `addressables_manage`  | Manage Addressables groups and entries |

### Code / LSP

| Tool                    | Description                      |
| ----------------------- | -------------------------------- |
| `get_compilation_state` | Get C# compilation state         |
| `hot_reload_status`     | Inspect hot reload preview state |
| `hot_reload`            | Preview methods or recover Play  |
| `read`                  | Read a C# source file            |
| `find_refs`             | Find symbol references           |
| `search`                | Search code by pattern           |
| `find_symbol`           | Find symbol definitions          |
| `get_symbols`           | Get symbols in a file            |
| `build_index`           | Build code search index          |
| `update_index`          | Update code search index         |
| `get_index_status`      | Get code search index status     |
| `rename_symbol`         | Rename a C# symbol               |
| `replace_symbol_body`   | Replace a C# symbol body         |
| `insert_before_symbol`  | Insert C# source before a symbol |
| `insert_after_symbol`   | Insert C# source after a symbol  |
| `remove_symbol`         | Remove a C# symbol               |
| `validate_text_edits`   | Validate C# text edits           |
| `write_csharp_file`     | Write a complete C# source file  |
| `create_csharp_file`    | Create a new C# source file      |
| `apply_csharp_edits`    | Apply structured C# source edits |
| `create_class`          | Create a C# class                |

`hot_reload` changes the body of existing methods while Play Mode keeps running
(scene, objects and field values are preserved; the `.cs` file on disk is not
modified). Prerequisites: the optional FastScriptReload 1.8.0 package installed
in the project with its auto and on-demand reload disabled, and a Mono Editor
(Unity 2022.3+) on x64 or Apple Silicon macOS. `hot_reload_status` returns
`supported`, `code` (`HOT_RELOAD_PACKAGE_MISSING`,
`HOT_RELOAD_PLATFORM_UNSUPPORTED`) and the verified `appliedRevision`. Flow:
`begin` with the script path, `apply` with the complete candidate source and
the last `appliedRevision`, `recover` to stop Play and recompile. See
[hot-reload.md](hot-reload.md) for limits and recovery.

### Input System

| Tool                          | Description                        |
| ----------------------------- | ---------------------------------- |
| `add_input_action`            | Add an Input Action                |
| `create_action_map`           | Create an Action Map               |
| `remove_action_map`           | Remove an Action Map               |
| `remove_input_action`         | Remove an Input Action             |
| `analyze_input_actions_asset` | Analyze Input Actions asset        |
| `get_input_actions_state`     | Inspect Input Actions structure    |
| `add_input_binding`           | Add an Input Binding               |
| `create_composite_binding`    | Create a composite binding         |
| `remove_input_binding`        | Remove an Input Binding            |
| `remove_all_bindings`         | Remove all bindings from an action |
| `manage_control_schemes`      | Manage control schemes             |
| `input_gamepad`               | Simulate gamepad input             |
| `input_keyboard`              | Simulate keyboard input            |
| `input_mouse`                 | Simulate mouse input               |
| `input_touch`                 | Simulate touch input               |
| `create_input_sequence`       | Create an input sequence           |
| `get_current_input_state`     | Get current input device state     |

For `input_gamepad` with `action: "stick"`, `x` and `y` are **processed
individual axis values**, clamped independently to `[-1, 1]`. On the standard
Gamepad layout, `leftStick.x.ReadValue()` / `leftStick.y.ReadValue()` (or the
right-stick equivalents) match those values, including single-axis input.
The bridge compensates once for the axis deadzone using the current Input
System `defaultDeadzoneMin` / `defaultDeadzoneMax` settings. The response and
simulated-state snapshot also use the clamped values.

`stick.ReadUnprocessedValue()` returns the compensated device values, not the
requested values. `stick.ReadValue()` applies Unity's radial stick deadzone,
so its Vector2 components can differ from the individual axis readings on
diagonals. For example, `(1, 1)` gives individual axes `(1, 1)` while the
Vector2 is approximately `(0.7071, 0.7071)`. Custom control/action processors
can further change readings and are not inverted by this command.

### UI

| Tool                   | Description                  |
| ---------------------- | ---------------------------- |
| `click_ui_element`     | Click a UI element           |
| `find_ui_elements`     | Find UI elements by criteria |
| `get_ui_element_state` | Get UI element state         |
| `set_ui_element_value` | Set UI element value         |
| `simulate_ui_input`    | Simulate UI input events     |

### Playback & Testing

| Tool              | Description                 |
| ----------------- | --------------------------- |
| `pause_game`      | Pause Play mode             |
| `play_game`       | Enter Play mode             |
| `stop_game`       | Exit Play mode              |
| `get_test_status` | Get test run status         |
| `run_tests`       | Run EditMode/PlayMode tests |

### Player Builds

| Tool               | Description                                               |
| ------------------ | --------------------------------------------------------- |
| `build_player`     | Queue a standalone player build on the Unity Editor host  |
| `get_build_status` | Read build status, report, and artifact paths by build ID |

`build_player` requires `target` (`StandaloneWindows64` or `StandaloneOSX`),
`scenes` (a nonempty array of scene asset paths), and `outputPath` (the executable
or app path, for example `C:/Builds/Player/Player.exe` or `/Users/me/Builds/Player/Player.app`).
The optional `development` boolean defaults to `false`.
Invoke these tools through `raw` or `tool call`; no dedicated subcommand is required.

The target must already be active in the Editor and its build support module must
be installed. The Editor must not be compiling, playing, or updating assets.
The output path's parent directory must be new or empty. The tool does not switch targets,
write project or scene build settings, or overwrite existing output. Unity's build
pipeline and project build callbacks can update settings; `changedProjectSettings`
lists the changed files under `ProjectSettings/` in the completed result.
Output under Assets, Packages, ProjectSettings, Library, or symbolic-link ancestors is rejected.
Paths refer to
the **Unity Editor host**, including when the CLI runs on another machine.

The initial response returns a `buildId`. Poll `get_build_status` with the required
`buildId` string for `state` in the CLI response: `queued`, `running`, `succeeded`, `failed`,
or `interrupted` (failure snapshots are in `details` on the error envelope).
The completed report includes `reportResult`, `totalErrors`,
`totalWarnings`, `errors`, `warnings`, `durationSeconds`, `outputPath`, and
`artifacts` (an array of artifact paths). A failed or interrupted build status produces a
non-success CLI exit; accepting a queued build does not mean the build succeeded.
`get_build_status` is read-only, including under `--dry-run`.
Report result and counts are null if Unity never produced a report. The latest
build survives Editor restart; up to 16 recent jobs are retained during a session.

### Profiler

| Tool                   | Description            |
| ---------------------- | ---------------------- |
| `profiler_get_metrics` | Get profiler metrics   |
| `profiler_start`       | Start profiler capture |
| `profiler_status`      | Get profiler status    |
| `profiler_stop`        | Stop profiler capture  |

### Editor

Manage a **local** Unity Editor from a stopped project:

```bash
unity-cli editor open --project-path /path/to/project --wait-ready 300
unity-cli system ping --project-path /path/to/project
unity-cli editor status --project-path /path/to/project --output json
unity-cli editor close --project-path /path/to/project

# Keep an Editor running in batch mode, without a graphics device.
unity-cli editor open --project-path /path/to/project --headless --wait-ready 300
unity-cli raw get_hierarchy --project-path /path/to/project --json '{}'
unity-cli editor close --project-path /path/to/project
```

`open` reads `ProjectSettings/ProjectVersion.txt` and resolves that exact Editor
under Unity Hub's default macOS, Windows or Linux installation directory.
`UNITY_EDITOR_PATH` overrides the executable location. An absent version returns
exit code **4** and an instruction to run `unity install <version>`; Editor
installation and licensing remain the official Unity CLI's responsibility.
Install the Bridge in the project first (`unity-cli bridge install`).

Without `--wait-ready`, `open` returns after launching. With it, the command waits
for the target Editor's lockfile and a successful project-matching Bridge ping.
An already running project is reused. A timeout leaves the Editor running for
inspection; use `editor status` and `Logs/unity-cli-editor.log` to diagnose it.
`--headless` adds `-batchmode -nographics`, enables the Bridge batch host, and does
not add `-quit`. It remains running until closed.

`status` returns `stopped`, `starting`, `compiling`, `safe_mode` or `ready`.
`close` refuses dirty scenes with `UNSAVED_SCENES`; save them first or explicitly
use `editor close --force` to discard unsaved changes. It waits until the local
Editor process is absent (bounded by `--timeout-ms`, default 30000).
`--dry-run` on `open` or `close` prints the intended action without launching or
quitting an Editor. Legacy `raw quit_editor` without a `force` parameter retains
its previous unconditional quit behavior; prefer `editor close` for scene protection.

| Tool                      | Description                 |
| ------------------------- | --------------------------- |
| `clear_console`           | Clear the Console window    |
| `clear_logs`              | Clear editor logs           |
| `read_console`            | Read Console output         |
| `manage_layers`           | Manage layers               |
| `quit_editor`             | Quit Unity Editor           |
| `manage_selection`        | Manage editor selection     |
| `manage_tags`             | Manage tags                 |
| `manage_tools`            | Manage editor tools         |
| `manage_windows`          | Manage editor windows       |
| `execute_menu_item`       | Execute a menu item         |
| `eval_csharp`             | Evaluate synchronous C#     |
| `get_eval_status`         | Query evaluation result     |
| `get_eval_stats`          | Query eval domain counters  |
| `package_manager`         | Manage packages             |
| `registry_config`         | Configure scoped registries |
| `get_editor_info`         | Get editor version info     |
| `get_editor_state`        | Get editor state            |
| `get_project_setting`     | Get a project setting       |
| `set_project_setting`     | Set a project setting       |
| `get_project_settings`    | Get project settings        |
| `get_package_setting`     | Get a package setting       |
| `set_package_setting`     | Set a package setting       |
| `update_project_settings` | Update project settings     |

`eval_csharp` reuses compilation references and compiled snippets inside one
Editor domain: repeating identical `code` costs about one ordinary command
(p50 14–25 ms with the Editor frontmost), new source adds one compile, and the
first call after a Domain Reload is the slowest. `get_eval_stats`
(`unity-cli editor eval-stats`) shows the domain's loaded-assembly, cache and
memory counters. Details and the latency budget:
[`editor-eval.md`](editor-eval.md#performance-and-caching).

### Screenshots & Video

Video capture requires the optional `com.unity.recorder` package (4.0 or
newer). Install it through Unity Package Manager when video capture is needed;
the sample `UnityCliBridge` project already includes it. Without Recorder,
video commands return `RECORDER_PACKAGE_MISSING`. Recorder itself installs
Timeline as a dependency; the Bridge no longer requires either package for
unrelated commands.

| Tool                   | Description              |
| ---------------------- | ------------------------ |
| `analyze_screenshot`   | Analyze a screenshot     |
| `capture_screenshot`   | Capture a screenshot     |
| `capture_video_start`  | Start video capture      |
| `capture_video_status` | Get video capture status |
| `capture_video_stop`   | Stop video capture       |

For `captureMode: "game"`, `includeUI: true` (the default) captures the final
Game frame, including UI Toolkit and Screen Space Overlay canvases, in Edit or
Play mode. Focus the Game View first. The bridge does not change focus or fall
back to camera-only rendering: it returns `GAME_VIEW_NOT_FOCUSED` if the Game
View is not focused, `GAME_CAPTURE_UNAVAILABLE` if graphics/capture is unavailable,
`GAME_CAPTURE_BUSY` for overlapping requests, or `GAME_CAPTURE_TIMEOUT` if no
completed frame arrives within five seconds. Requested `width` / `height` resize
the complete frame; omitted dimensions use the native Game resolution.

Use `includeUI: false` for the existing camera-only capture. It excludes UI
Toolkit and Screen Space Overlay canvases; camera/world-space content keeps
its existing rendering behavior. `scene`, `explorer`, and `window` modes are
unchanged.

When the Editor accepts `capture_screenshot` but does not answer before
`--timeout-ms` (for example, its main thread is blocked by a modal dialog such
as a save prompt or the API Updater), the CLI captures the whole desktop
instead and saves it to `<project>/.unity/capture/image_os_<millis>.png`
(the temp directory outside a Unity project). The result carries
`"fallback": "os"`, `fallbackTool`, and a `note` explaining why.

- macOS: `screencapture` (grant Screen Recording to the terminal, otherwise
  only the wallpaper is captured)
- Windows: PowerShell + GDI (`Graphics.CopyFromScreen`)
- Linux: `grim` / `gnome-screenshot` / `spectacle` on Wayland, `import` /
  `scrot` / `maim` on X11 (first available wins)

Pass `"osFallback": false` to receive the timeout error instead. The flag is
handled by the CLI and never sent to the bridge.

```bash
unity-cli --timeout-ms 5000 raw capture_screenshot --json '{"captureMode":"game"}'
unity-cli raw capture_screenshot --json '{"captureMode":"game","osFallback":false}'
```

### System

| Tool                | Description                                                                         |
| ------------------- | ----------------------------------------------------------------------------------- |
| `get_command_stats` | Get bridge command statistics and, via the CLI, merged local transport timing stats |
| `ping`              | Check Unity Editor connectivity                                                     |
| `list_packages`     | List installed packages                                                             |

## Numeric animation curves

`get_animation_curves` takes `clipPath` and an optional exact `binding` filter
(`path`, `component`, `property`). It returns numeric curves with keys and tangent
modes, and a separate `objectReferenceBindings` list. Infinite Constant tangents
are represented as JSON `null`; their mode remains `Constant`.

`edit_animation_curve` requires a real GameObject instance ID in `animationRoot`
and a binding relative to that root. Component names are fully qualified;
properties use Unity's serialized names. Transform `localPosition`,
`localRotation` and `localScale` aliases map to their `m_` names.

```bash
unity-cli raw edit_animation_curve --json '{"clipPath":"Assets/Animations/Move.anim","animationRoot":12345,"binding":{"path":"","component":"UnityEngine.Transform","property":"localPosition.x"},"operation":"set","createIfMissing":true,"keys":[{"time":0,"value":0},{"time":1,"value":2}]}'
unity-cli raw get_animation_curves --json '{"clipPath":"Assets/Animations/Move.anim"}'
```

Replace `12345` with the inspected scene object's instance ID. `set` replaces only
the selected curve. `upsert_keys` changes/adds exact key times on an existing
curve. `remove_keys` takes a nonempty `times` array; `remove_curve` deletes the
binding. Only `set` with `createIfMissing:true` creates a missing clip.

New keys default to Linear. Each key may specify `leftTangentMode` and
`rightTangentMode` as Linear, Constant, Auto, ClampedAuto or Free; omitted settings
on existing keys are retained. Free tangents use finite `inTangent`/`outTangent`
values (zero for a new key when omitted). Time must be finite, nonnegative and
unique after conversion to Unity's float precision; values and supplied tangents
must be finite. Missing removal times are errors. Editing requires a writable
standalone `.anim` under `Assets/`, outside Play Mode. Imported model clips,
object-reference, boolean/enum and discrete bindings are rejected. Unspecified
bindings, events and clip settings are preserved.

Run `scripts/test-animation-curves.sh` for Editor regression tests and
`cargo build --release` followed by `scripts/e2e-animation-curves-batch-host.sh`
for the CLI-to-Editor test (default dedicated port `6473`).

## Audio authoring

`manage_audio_mixer` uses an existing `Assets/` folder and a `.mixer` asset path.
Mutations require Edit Mode; existing assets are never overwritten. Group paths
are exact hierarchy paths, starting at `Master`. Duplicate sibling groups,
duplicate exposed names, and exposing the same parameter twice return errors.

```bash
unity-cli raw manage_audio_mixer --json '{"action":"create","assetPath":"Assets/Game.mixer"}'
unity-cli raw manage_audio_mixer --json '{"action":"add_group","assetPath":"Assets/Game.mixer","parentGroup":"Master","name":"Music"}'
unity-cli raw manage_audio_mixer --json '{"action":"expose_parameter","assetPath":"Assets/Game.mixer","groupPath":"Master/Music","parameter":"Volume","parameterName":"MusicVolume"}'
unity-cli raw manage_audio_mixer --json '{"action":"get","assetPath":"Assets/Game.mixer"}'
```

Every successful action returns `assetPath`, `groups` (`name`, `path`,
`parentPath`) and `exposedParameters` (`name`, `guid`, `groupPath`, `parameter`).
Only group `Volume` can be exposed by this tool. Parameters exposed elsewhere
are also listed; their `groupPath` and `parameter` are null when they are not
group Volume parameters. Internal Unity audio editor APIs are isolated in the
bridge; unavailable APIs return `AUDIO_MIXER_API_ERROR`.

`manage_asset_import_settings` supports AudioImporter `modify` and `get`:
`loadType`, `compressionFormat`, `quality` (0–1), `forceToMono`,
`loadInBackground`, `ambisonic`, `sampleRateSetting`, and `sampleRateOverride`
(1–192000 Hz). Enum values use Unity's exact enum names. Settings target the
default sample settings; existing platform overrides remain independent.
All requested values are validated before assigning any importer settings,
then `SaveAndReimport` persists them.

```bash
unity-cli raw manage_asset_import_settings --json '{"action":"modify","assetPath":"Assets/Music.wav","settings":{"loadType":"Streaming","compressionFormat":"Vorbis","quality":0.7,"forceToMono":false,"loadInBackground":true}}'
unity-cli raw manage_asset_import_settings --json '{"action":"get","assetPath":"Assets/Music.wav"}'
```

## Local Runtime Tools (No Unity Connection Required)

These runtime/code tools run locally via Rust and do not require a TCP connection to Unity Editor. Reference Cache tools are also local and are listed separately below.

- `read` — Read C# source files
- `search` — Search code by pattern
- `list_packages` — List installed packages
- `get_symbols` — Get symbols in a file
- `build_index` — Build code search index
- `update_index` — Update code search index
- `get_index_status` — Get code search index status
- `find_symbol` — Find symbol definitions
- `find_refs` — Find symbol references
- `rename_symbol` — Rename a C# symbol
- `replace_symbol_body` — Replace a C# symbol body
- `insert_before_symbol` — Insert C# source before a symbol
- `insert_after_symbol` — Insert C# source after a symbol
- `remove_symbol` — Remove a C# symbol
- `validate_text_edits` — Validate C# text edits
- `write_csharp_file` — Write a complete C# source file
- `create_csharp_file` — Create a new C# source file
- `apply_csharp_edits` — Apply structured C# source edits
- `create_class` — Create a C# class

## Schema Notes

- `load_scene`:
  - exactly one of `scenePath` / `sceneName` is required (`oneOf`)
- `delete_gameobject`:
  - at least one of `path` / `paths` is required (`anyOf`)
- `input_keyboard`:
  - one of `action` (single action) or `actions` (batch) is required (`anyOf`)
- Action-based tools:
  - action-specific required fields are enforced via schema variants (`oneOf`)
  - examples:
    - `manage_layers` action `add` requires `layerName`
    - `package_manager` action `search` requires `keyword`
    - `addressables_manage` action `move_entry` requires `targetGroupName`
    - `execute_menu_item` action `get_available_menus` does not require `menuPath`

Examples:

```bash
unity-cli tool schema load_scene --output json
unity-cli tool schema delete_gameobject --output json
unity-cli tool schema input_keyboard --output json
unity-cli tool schema package_manager --output json
```

## Reference Cache (11 tools)

The `unity-cli reference *` family provides a local read-only mirror of the
official [UnityCsReference](https://github.com/Unity-Technologies/UnityCsReference)
source. Use it when you need the canonical signature or internal
implementation of a Unity API. The cache lives under
`~/.unity/cache/UnityCsReference/<version>/` (override with
`UNITY_CLI_CACHE_ROOT`). License acceptance is mandatory before the first
fetch via `--accept-license` or `UNITY_CLI_ACCEPT_LICENSE=1`.

| Tool                          | Description                                                                                                                                  |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| `reference_fetch`             | Shallow-clone UnityCsReference for the active Unity version into the local cache.                                                            |
| `reference_status`            | List cached UnityCsReference versions, branches, fetched-at, and disk usage.                                                                 |
| `reference_search`            | Search the cached reference source for a pattern with optional path and result limits.                                                       |
| `reference_grep`              | Grep the cached reference source line-by-line with optional file glob and context lines.                                                     |
| `reference_view`              | Display a slice of a file in the cached reference source by line range.                                                                      |
| `reference_clean`             | Remove old UnityCsReference snapshots, keeping the newest entries.                                                                           |
| `reference_find_symbol`       | Look up type / method / property definitions in the cached reference source via a per-version on-disk index.                                 |
| `reference_diff`              | Compare a symbol or path range between two cached Unity versions. Returns symbol-level hunks or `{added, removed, changed}`.                 |
| `reference_resolve_symbol_at` | Resolve the identifier at a project cursor position (`Assets/...` / `Packages/...`) to candidate reference cache entries with view excerpts. |
| `reference_embed_build`       | Build an embedding index (BGE-Small-EN, ONNX) for a cached Unity version. Writes `.unity-cli-index/embeddings.bin`.                          |
| `reference_embed_search`      | Semantic / natural-language lookup over the embedding index. Returns hits sorted by cosine similarity.                                       |

Typed CLI equivalents:

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

See `.claude-plugin/plugins/unity-cli/skills/unity-csharp-reference/` for the
companion skill and the `reference -> navigate -> edit` workflow.

## Regenerate This Catalog

```bash
unity-cli --help
unity-cli tool list --host 127.0.0.1 --port 6400 --output json | jq -r '.[]'
unity-cli tool schema create_scene --output json
```

## v0.13–v0.15 executable examples

These examples are also discoverable in the domain skills: [runtime testing](../.claude-plugin/plugins/unity-cli/skills/unity-playmode-testing/SKILL.md), [Editor jobs](../.claude-plugin/plugins/unity-cli/skills/unity-editor-tools/SKILL.md), and [Timeline assets](../.claude-plugin/plugins/unity-cli/skills/unity-asset-management/SKILL.md). Select the intended Editor with host/port or project-path first.

### Input simulation and InputAction notifications

Requires Input System enabled and a running Play session. Replace the asset path
with an existing input asset. Enable the intended action map in the game's runtime
code and observe its `performed`/`canceled` callback (for example, log a counter).
`get_input_actions_state` inspects asset maps/actions/bindings; it does **not** prove
that callbacks fired, and a PlayerInput runtime copy can differ from the asset.

```bash
unity-cli raw input_keyboard --json '{"key":"space","action":"press","holdSeconds":0.2}'
unity-cli raw input_gamepad --json '{"action":"button","button":"a","buttonAction":"press","holdSeconds":0.2}'
unity-cli raw input_mouse --json '{"action":"move","x":100,"y":200,"absolute":true}'
unity-cli raw input_touch --json '{"action":"tap","x":100,"y":200,"touchId":0}'
unity-cli raw create_input_sequence --json '{"sequence":[{"type":"keyboard","params":{"action":"press","key":"space","holdSeconds":0.1}},{"type":"mouse","params":{"action":"move","x":100,"y":200,"absolute":true}}],"delayBetween":80}'
unity-cli raw get_input_actions_state --json '{"assetPath":"Assets/Input/Player.inputactions","includeBindings":true}'
unity-cli raw read_console --json '{"count":20}'
```

Check the callback evidence after sending input. `holdSeconds` holds a press across
frames before release; sequence `delayBetween` is milliseconds. Device state alone
is not evidence of a gameplay notification. For gamepad sticks, x/y specify
individual processed axes; a diagonally processed Vector2 can differ.

### Video and PNG sequences

Requires a graphics-enabled Editor with Recorder installed and a visible Game View.
Start one session, allow frames to render, inspect status, then stop before starting
another. `format` accepts `mp4`, `webm`, or `png_sequence` (run the same flow for each).

```bash
unity-cli raw capture_video_start --json '{"captureMode":"game","format":"png_sequence","width":320,"height":180,"fps":10,"maxDurationSec":0}'
unity-cli raw capture_video_status --json '{}'
unity-cli raw capture_video_stop --json '{}'
```

Check `isRecording` and the final `outputPath`. For PNG sequences it names the first
frame in a unique session directory; verify subsequent PNG files exist too.

### Tests with Domain Reload enabled

Domain Reload may remain enabled in Enter Play Mode Settings. Start tests from
Edit Mode; the bridge persists and recovers the result across the reload. Poll
status until complete, including results, and inspect passed/failed counts. A
temporary disconnect during reload is not a failed test; reconnect and query status
instead of submitting a duplicate test run.

```bash
unity-cli raw run_tests --json '{"testMode":"PlayMode"}'
unity-cli raw get_test_status --json '{"includeTestResults":true}'
```

### Method hot reload preview

Requires optional FastScriptReload 1.8.0, a supported macOS Editor, a compiled
baseline and Play Mode. Replace the path with an existing eligible script.
Disable Fast Script Reload's automatic and on-demand reload in its settings first;
the bridge rejects concurrent patchers rather than changing those preferences.
Inspect `supported` before beginning; do not report an unsupported preview as a pass.

```bash
unity-cli raw hot_reload_status --json '{}'
unity-cli raw hot_reload --json '{"action":"begin","path":"Assets/HotReloadProbe.cs"}'
unity-cli raw hot_reload --json '{"action":"recover"}'
```

`begin` returns `appliedRevision`; to apply complete candidate source and verify the
actual method change, follow the [Hot Reload Preview Loop](../.claude-plugin/plugins/unity-cli/skills/unity-development-loop/references/development-loop-playbook.md#hot-reload-preview-loop).
`recover` stops Play and recompiles; poll `hot_reload_status` until idle. Preview
does not save source. Persist a verified edit through `unity-csharp-edit` afterwards.
If `begin` returns `HOT_RELOAD_BASELINE_UNPROVEN`, run `recover`, wait for idle,
then re-enter Play Mode and begin again against the freshly compiled baseline.

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

### Timeline assets, tracks, clips and bindings

Requires Timeline installed, Edit Mode, and an existing writable parent folder.
Use a new asset path for creation, then inspect before subsequent edits.

```bash
unity-cli raw manage_timeline --json '{"action":"create_asset","assetPath":"Assets/Timelines/Intro.playable"}'
unity-cli raw manage_timeline --json '{"action":"create_track","assetPath":"Assets/Timelines/Intro.playable","trackName":"Movement","trackType":"AnimationTrack"}'
unity-cli raw get_timeline --json '{"assetPath":"Assets/Timelines/Intro.playable"}'
```

Use inspected stable `trackId` values (GUID:localID), not track names. Only top-level
AnimationTrack editing is supported. For `add_clip`, supply `animationClipPath`,
`start`, and positive `duration`; re-inspect before `update_clip`/`remove_clip` and
include `clipIndex` plus `expectedClip` from that snapshot. To bind or evaluate a
PlayableDirector, use full hierarchy paths for `directorPath`/`animatorPath`.
Inspect `unity-cli tool schema manage_timeline` for action-specific required fields.
