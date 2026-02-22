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
unity-cli raw get_editor_info --json '{}'
unity-cli raw get_editor_state --json '{}'
unity-cli raw get_command_stats --json '{}'
```

## Project Settings

```bash
unity-cli raw get_project_settings --json '{"includePlayer":true}'
unity-cli raw update_project_settings --json '{"confirmChanges":true,"player":{"companyName":"MyStudio"}}'
```

## Editor Operations

```bash
unity-cli raw execute_menu_item --json '{"menuPath":"File/Save Project"}'
unity-cli raw manage_windows --json '{"action":"get"}'
unity-cli raw manage_selection --json '{"action":"get"}'
unity-cli raw manage_selection --json '{"action":"set","objectPaths":["/Player"]}'
unity-cli raw manage_tools --json '{"action":"get"}'
unity-cli raw quit_editor --json '{}'
```

## Console

```bash
unity-cli raw read_console --json '{"count":20}'
unity-cli raw clear_console --json '{}'
unity-cli raw clear_logs --json '{}'
```

## Profiler

```bash
unity-cli raw profiler_start --json '{}'
unity-cli raw profiler_stop --json '{}'
unity-cli raw profiler_status --json '{}'
unity-cli raw profiler_get_metrics --json '{"listAvailable":true}'
```

## Package Manager

```bash
unity-cli raw package_manager --json '{"action":"list"}'
unity-cli raw package_manager --json '{"action":"install","packageId":"com.unity.inputsystem"}'
unity-cli raw registry_config --json '{"action":"list"}'
```

## Tips

- Use `system ping` as a health check before automation.
- Use `tool list` to discover available raw commands.
- Query profiler metrics with `listAvailable` before requesting specific metrics.
