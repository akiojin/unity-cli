# Tool Catalog

Snapshot date: `2026-09-28`

## Command Groups (Typed Subcommands)

| Group       | Subcommands               |
| ----------- | ------------------------- |
| `raw`       | (direct tool invocation)  |
| `tool`      | `list`, `schema`, `call`  |
| `system`    | `ping`                    |
| `scene`     | `create`                  |
| `instances` | `list`, `set-active`      |
| `cli`       | `install`, `doctor`       |
| `lsp`       | `install`, `doctor`       |
| `lspd`      | `start`, `stop`, `status` |
| `unityd`    | `start`, `stop`, `status` |
| `batch`     | (batch command execution) |

Use `raw` for full command coverage when no typed subcommand exists.

Managed binary notes:

- `cli install` downloads or refreshes the managed `unity-cli` copy under `UNITY_CLI_TOOLS_ROOT` (or the OS default tools directory).
- `cli doctor` reports the managed `unity-cli` path, local version, latest release metadata, and whether an update is pending.
- `unityd` and `lspd` automatically refresh managed binaries on daemon startup without an interactive confirmation step.

Global options:

- `--output text|json`
- `--dry-run` (skip mutating tools and return execution plan)

Registered tool total: 138 (`TOOL_NAMES` in `src/tooling/tool_catalog.rs`): 127 runtime/local tool APIs plus 11 Reference Cache tools.

## Runtime Tool APIs (127 tools)

### Scenes

| Tool             | Description            |
| ---------------- | ---------------------- |
| `create_scene`   | Create a new scene     |
| `get_scene_info` | Get scene metadata     |
| `list_scenes`    | List all scenes        |
| `load_scene`     | Load a scene           |
| `save_scene`     | Save the current scene |

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

| Tool                 | Description                       |
| -------------------- | --------------------------------- |
| `create_prefab`      | Create a new Prefab               |
| `exit_prefab_mode`   | Exit Prefab editing mode          |
| `instantiate_prefab` | Instantiate a Prefab in the scene |
| `modify_prefab`      | Modify Prefab properties          |
| `open_prefab`        | Open a Prefab for editing         |
| `save_prefab`        | Save Prefab changes               |

### Assets

| Tool                           | Description                                                    |
| ------------------------------ | -------------------------------------------------------------- |
| `analyze_scene_contents`       | Analyze scene asset contents                                   |
| `manage_asset_database`        | Manage AssetDatabase operations                                |
| `analyze_asset_dependencies`   | Analyze asset dependency graph                                 |
| `manage_asset_import_settings` | Manage asset import settings                                   |
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

### Input System

| Tool                          | Description                        |
| ----------------------------- | ---------------------------------- |
| `add_input_action`            | Add an Input Action                |
| `create_action_map`           | Create an Action Map               |
| `remove_action_map`           | Remove an Action Map               |
| `remove_input_action`         | Remove an Input Action             |
| `analyze_input_actions_asset` | Analyze Input Actions asset        |
| `get_input_actions_state`     | Get Input Actions runtime state    |
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

### Profiler

| Tool                   | Description            |
| ---------------------- | ---------------------- |
| `profiler_get_metrics` | Get profiler metrics   |
| `profiler_start`       | Start profiler capture |
| `profiler_status`      | Get profiler status    |
| `profiler_stop`        | Stop profiler capture  |

### Editor

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

### System

| Tool                | Description                                                                         |
| ------------------- | ----------------------------------------------------------------------------------- |
| `get_command_stats` | Get bridge command statistics and, via the CLI, merged local transport timing stats |
| `ping`              | Check Unity Editor connectivity                                                     |
| `list_packages`     | List installed packages                                                             |

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
