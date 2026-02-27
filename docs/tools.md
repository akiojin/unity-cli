# Tool Catalog

Snapshot date: `2026-02-23`

## Command Groups (Typed Subcommands)

| Group | Subcommands |
| --- | --- |
| `raw` | (direct tool invocation) |
| `tool` | `list`, `call` |
| `system` | `ping` |
| `scene` | `create` |
| `instances` | `list`, `set-active` |
| `lsp` | `install`, `doctor` |
| `lspd` | `start`, `stop`, `status` |
| `unityd` | `start`, `stop`, `status` |
| `batch` | (batch command execution) |

Use `raw` for full command coverage when no typed subcommand exists.

## Unity Tool APIs (101 tools)

### Scenes

| Tool | Description |
| --- | --- |
| `create_scene` | Create a new scene |
| `get_scene_info` | Get scene metadata |
| `list_scenes` | List all scenes |
| `load_scene` | Load a scene |
| `save_scene` | Save the current scene |

### GameObjects

| Tool | Description |
| --- | --- |
| `create_gameobject` | Create a new GameObject |
| `delete_gameobject` | Delete a GameObject |
| `find_gameobject` | Find GameObjects by name or criteria |
| `get_hierarchy` | Get scene hierarchy tree |
| `modify_gameobject` | Modify GameObject properties |
| `get_gameobject_details` | Get detailed GameObject info |

### Components

| Tool | Description |
| --- | --- |
| `add_component` | Add a component to a GameObject |
| `set_component_field` | Set a component field value |
| `get_component_types` | List available component types |
| `list_components` | List components on a GameObject |
| `modify_component` | Modify component properties |
| `remove_component` | Remove a component |
| `find_by_component` | Find GameObjects by component type |
| `get_component_values` | Get component field values |
| `get_object_references` | Get object reference graph |

### Animator

| Tool | Description |
| --- | --- |
| `get_animator_runtime_info` | Get Animator runtime info |
| `get_animator_state` | Get current Animator state |

### Prefabs

| Tool | Description |
| --- | --- |
| `create_prefab` | Create a new Prefab |
| `exit_prefab_mode` | Exit Prefab editing mode |
| `instantiate_prefab` | Instantiate a Prefab in the scene |
| `modify_prefab` | Modify Prefab properties |
| `open_prefab` | Open a Prefab for editing |
| `save_prefab` | Save Prefab changes |

### Assets

| Tool | Description |
| --- | --- |
| `analyze_scene_contents` | Analyze scene asset contents |
| `manage_asset_database` | Manage AssetDatabase operations |
| `analyze_asset_dependencies` | Analyze asset dependency graph |
| `manage_asset_import_settings` | Manage asset import settings |
| `create_material` | Create a new Material |
| `modify_material` | Modify Material properties |
| `refresh_assets` | Refresh the AssetDatabase |

### Addressables

| Tool | Description |
| --- | --- |
| `addressables_analyze` | Analyze Addressables configuration |
| `addressables_build` | Build Addressables content |
| `addressables_manage` | Manage Addressables groups and entries |

### Code / LSP

| Tool | Description |
| --- | --- |
| `get_compilation_state` | Get C# compilation state |
| `read` | Read a C# source file |
| `find_refs` | Find symbol references |
| `search` | Search code by pattern |
| `find_symbol` | Find symbol definitions |
| `get_symbols` | Get symbols in a file |
| `build_index` | Build code search index |
| `update_index` | Update code search index |

### Input System

| Tool | Description |
| --- | --- |
| `add_input_action` | Add an Input Action |
| `create_action_map` | Create an Action Map |
| `remove_action_map` | Remove an Action Map |
| `remove_input_action` | Remove an Input Action |
| `analyze_input_actions_asset` | Analyze Input Actions asset |
| `get_input_actions_state` | Get Input Actions runtime state |
| `add_input_binding` | Add an Input Binding |
| `create_composite_binding` | Create a composite binding |
| `remove_input_binding` | Remove an Input Binding |
| `remove_all_bindings` | Remove all bindings from an action |
| `manage_control_schemes` | Manage control schemes |
| `input_gamepad` | Simulate gamepad input |
| `input_keyboard` | Simulate keyboard input |
| `input_mouse` | Simulate mouse input |
| `input_touch` | Simulate touch input |
| `create_input_sequence` | Create an input sequence |
| `get_current_input_state` | Get current input device state |

### UI

| Tool | Description |
| --- | --- |
| `click_ui_element` | Click a UI element |
| `find_ui_elements` | Find UI elements by criteria |
| `get_ui_element_state` | Get UI element state |
| `set_ui_element_value` | Set UI element value |
| `simulate_ui_input` | Simulate UI input events |

### Playback & Testing

| Tool | Description |
| --- | --- |
| `pause_game` | Pause Play mode |
| `play_game` | Enter Play mode |
| `stop_game` | Exit Play mode |
| `get_test_status` | Get test run status |
| `run_tests` | Run EditMode/PlayMode tests |

### Profiler

| Tool | Description |
| --- | --- |
| `profiler_get_metrics` | Get profiler metrics |
| `profiler_start` | Start profiler capture |
| `profiler_status` | Get profiler status |
| `profiler_stop` | Stop profiler capture |

### Editor

| Tool | Description |
| --- | --- |
| `clear_console` | Clear the Console window |
| `clear_logs` | Clear editor logs |
| `read_console` | Read Console output |
| `manage_layers` | Manage layers |
| `quit_editor` | Quit Unity Editor |
| `manage_selection` | Manage editor selection |
| `manage_tags` | Manage tags |
| `manage_tools` | Manage editor tools |
| `manage_windows` | Manage editor windows |
| `execute_menu_item` | Execute a menu item |
| `package_manager` | Manage packages |
| `registry_config` | Configure scoped registries |
| `get_editor_info` | Get editor version info |
| `get_editor_state` | Get editor state |
| `get_project_settings` | Get project settings |
| `update_project_settings` | Update project settings |

### Screenshots & Video

| Tool | Description |
| --- | --- |
| `analyze_screenshot` | Analyze a screenshot |
| `capture_screenshot` | Capture a screenshot |
| `capture_video_start` | Start video capture |
| `capture_video_status` | Get video capture status |
| `capture_video_stop` | Stop video capture |

### System

| Tool | Description |
| --- | --- |
| `get_command_stats` | Get command statistics |
| `ping` | Check Unity Editor connectivity |
| `list_packages` | List installed packages |

## Local Tools (No Unity Connection Required)

These tools run locally via Rust and do not require a TCP connection to Unity Editor:

- `read` — Read C# source files
- `search` — Search code by pattern
- `list_packages` — List installed packages
- `get_symbols` — Get symbols in a file
- `build_index` — Build code search index
- `update_index` — Update code search index
- `find_symbol` — Find symbol definitions
- `find_refs` — Find symbol references

## Regenerate This Catalog

```bash
unity-cli --help
unity-cli tool list --host 127.0.0.1 --port 6400 --output json | jq -r '.[]'
```
