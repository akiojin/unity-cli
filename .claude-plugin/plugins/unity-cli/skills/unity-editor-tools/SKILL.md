---
name: unity-editor-tools
description: Unity Editor utilities -- ping, project settings, profiler, console, menu items, windows, and selection.
allowed-tools: Bash, Read, Grep, Glob
---

# Editor Utilities & Profiler

General editor operations, diagnostics, profiling, and project settings.

## Connection & Info

```bash
unity-cli system ping
unity-cli raw get_server_info --json '{}'
unity-cli raw get_command_stats --json '{}'
unity-cli raw search_tools --json '{"query":"scene"}'
```

## Project Settings

```bash
unity-cli raw get_project_settings --json '{"category":"Player"}'
unity-cli raw update_project_settings --json '{"category":"Player","settings":{"companyName":"MyStudio"}}'
```

## Editor Operations

```bash
unity-cli raw execute_menu_item --json '{"menuPath":"File/Save Project"}'
unity-cli raw manage_windows --json '{"action":"list"}'
unity-cli raw manage_selection --json '{"action":"get"}'
unity-cli raw manage_selection --json '{"action":"set","objectPath":"/Player"}'
unity-cli raw manage_tools --json '{"action":"list"}'
unity-cli raw quit_editor --json '{}'
```

## Console

```bash
unity-cli raw read_console --json '{"count":20}'
unity-cli raw clear_console --json '{}'
```

## Profiler

```bash
unity-cli raw profiler_start --json '{}'
unity-cli raw profiler_stop --json '{}'
unity-cli raw profiler_status --json '{}'
unity-cli raw profiler_get_metrics --json '{"category":"CPU"}'
```

## Package Manager

```bash
unity-cli raw package_manager --json '{"action":"list"}'
unity-cli raw package_manager --json '{"action":"add","packageId":"com.unity.inputsystem"}'
unity-cli raw registry_config --json '{"action":"list"}'
```

## Tips

- Use `system ping` as a health check before automation.
- `search_tools` helps discover available raw commands.
- Profiler categories: `CPU`, `GPU`, `Memory`, `Rendering`.
