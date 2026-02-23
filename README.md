# unity-cli

Rust-based CLI for Unity Editor automation over Unity TCP.
Successor to [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server) — rewritten from Node.js/MCP to native Rust.

## Install

From crates.io:

```bash
cargo install unity-cli
```

If you use repository skills, make sure `unity-cli` is installed in advance.  
`unity-cli-usage` can bootstrap automatically by running `cargo install unity-cli` when `cargo` is available.

From GitHub:

```bash
cargo install --git https://github.com/akiojin/unity-cli.git unity-cli
```

## Quick Start

```bash
unity-cli system ping
unity-cli scene create MainScene
unity-cli raw create_gameobject --json '{"name":"Player"}'
```

## Command Groups

- `system`
- `scene`
- `instances`
- `tool`
- `raw`

Use `raw` for full command coverage when no typed subcommand exists.

## Full Capability Catalog (Snapshot)

Snapshot date: `2026-02-23`  
Source: `unity-cli --help` and `unity-cli tool list --host 127.0.0.1 --port 6400 --output json`

Typed command groups:

- `raw`
- `tool` (`list`, `call`)
- `system` (`ping`)
- `scene` (`create`)
- `instances` (`list`, `set-active`)
- `lsp` (`install`, `doctor`)
- `lspd` (`start`, `stop`, `status`)

Unity Tool APIs (`101`):

```text
addressables_analyze
addressables_build
addressables_manage
get_animator_runtime_info
get_animator_state
find_by_component
get_component_values
get_gameobject_details
get_object_references
analyze_scene_contents
manage_asset_database
analyze_asset_dependencies
manage_asset_import_settings
create_material
modify_material
create_prefab
exit_prefab_mode
instantiate_prefab
modify_prefab
open_prefab
save_prefab
build_index
update_index
get_compilation_state
add_component
set_component_field
get_component_types
list_components
modify_component
remove_component
clear_console
clear_logs
read_console
manage_layers
quit_editor
manage_selection
manage_tags
manage_tools
manage_windows
create_gameobject
delete_gameobject
find_gameobject
get_hierarchy
modify_gameobject
add_input_action
create_action_map
remove_action_map
remove_input_action
analyze_input_actions_asset
get_input_actions_state
add_input_binding
create_composite_binding
remove_input_binding
remove_all_bindings
manage_control_schemes
input_gamepad
input_keyboard
input_mouse
input_touch
create_input_sequence
get_current_input_state
execute_menu_item
package_manager
registry_config
get_editor_info
get_editor_state
pause_game
play_game
stop_game
profiler_get_metrics
profiler_start
profiler_status
profiler_stop
create_scene
get_scene_info
list_scenes
load_scene
save_scene
analyze_screenshot
capture_screenshot
list_packages
read
find_refs
search
find_symbol
get_symbols
get_project_settings
update_project_settings
get_command_stats
ping
refresh_assets
get_test_status
run_tests
click_ui_element
find_ui_elements
get_ui_element_state
set_ui_element_value
simulate_ui_input
capture_video_start
capture_video_status
capture_video_stop
```

Regenerate this section:

```bash
unity-cli --help
unity-cli tool list --host 127.0.0.1 --port 6400 --output json | jq -r '.[]'
```

## Skills Architecture

`unity-cli` provides 13 skills that invoke CLI commands on demand, replacing the old MCP tool definitions:

| Skill | Domain |
| --- | --- |
| `unity-cli-usage` | CLI basics and raw command reference |
| `unity-scene-create` | Scene and GameObject creation |
| `unity-scene-inspect` | Scene hierarchy analysis |
| `unity-gameobject-edit` | GameObject and Component editing |
| `unity-prefab-workflow` | Prefab lifecycle management |
| `unity-asset-management` | Asset and Material operations |
| `unity-addressables` | Addressables build and analysis |
| `unity-csharp-navigate` | C# code exploration (LSP) |
| `unity-csharp-edit` | C# code editing and refactoring |
| `unity-playmode-testing` | PlayMode, testing, and input simulation |
| `unity-input-system` | Input System configuration |
| `unity-ui-automation` | UI element interaction |
| `unity-editor-tools` | Editor utilities and profiler |

## Skill Distribution

- Source of truth: `.claude-plugin/plugins/unity-cli/skills/`
- Claude Code (official distribution): Marketplace plugin (`.claude-plugin/marketplace.json`)
- Claude Code (repository-local test registration): `.claude/skills/` symlinks to source-of-truth skills
- Codex (official in this repository): `.codex/skills/` symlinks to source-of-truth skills
- Zip packaging is intentionally not provided in this repository
- Legacy MCP skill names and compatibility aliases are intentionally not provided

## Local Tools (Rust-side)

These tools run locally:

- `read`
- `search`
- `list_packages`
- `get_symbols`
- `build_index`
- `update_index`
- `find_symbol`
- `find_refs`

## Unity Package (UPM)

Unity-side bridge package:

- `UnityCliBridge/Packages/unity-cli-bridge`

Install URL:

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

## LSP

Bundled C# LSP source:

- `lsp/Program.cs`
- `lsp/Server.csproj`

Test command:

```bash
dotnet test lsp/Server.Tests.csproj
```

## Development

- Contributing: `CONTRIBUTING.md`
- Development guide: `docs/development.md`
- Release guide: `RELEASE.md`
- Unity test project: `UnityCliBridge`

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full contributing guide.

```bash
cargo test                              # Rust tests
dotnet test lsp/Server.Tests.csproj     # LSP tests
./scripts/e2e-test.sh                   # Unity E2E (requires running Unity Editor)
./scripts/e2e-all-tools.sh              # Full 101-tool E2E (+ LSP perf check)
./scripts/lsp-perf-check.sh             # LSP perf (small + large file) + threshold check
./scripts/benchmark.sh                  # Performance benchmarks
```

Docker-based verification:

```bash
docker build -t unity-cli-dev .
docker run --rm unity-cli-dev
```

## Release

Release script and CI workflow handle validation, build, and publish:

```bash
./scripts/publish.sh 0.2.0
```

See [RELEASE.md](RELEASE.md) for the full release guide.

## Environment Variables

| Variable | Description | Default |
| --- | --- | --- |
| `UNITY_PROJECT_ROOT` | Directory containing `Assets/` and `Packages/` | auto-detect |
| `UNITY_CLI_HOST` | Unity Editor host | `localhost` |
| `UNITY_CLI_PORT` | Unity Editor port | `6400` |
| `UNITY_CLI_TIMEOUT_MS` | Command timeout (ms) | `30000` |
| `UNITY_CLI_LSP_MODE` | LSP mode (`off` / `auto` / `required`) | `off` |
| `UNITY_CLI_TOOLS_ROOT` | Downloaded tools root directory | OS default |

Legacy MCP-prefixed variables are not supported. Use `UNITY_CLI_*` only. See [docs/development.md](docs/development.md).

## Output Modes

- Default: text
- JSON: `--output json`

```bash
cargo run -- --output json system ping
```

## License

MIT - See [ATTRIBUTION.md](ATTRIBUTION.md) for attribution templates when redistributing.
