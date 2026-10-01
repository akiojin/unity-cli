# Editor Ops Checklist

## Safe Order

1. Ping the editor and capture `get_editor_state`.
2. Read the current state before changing it.
3. Apply one editor-wide change at a time.
4. Re-read the relevant state and summarize the delta.

## Settings Changes

- Use `get_project_settings` first so the response can mention the current value.
- Send the narrowest `update_project_settings` payload possible.
- Include `"confirmChanges": true` for mutating settings updates.

## Console and Profiler

- Read the console before clearing it.
- Check `profiler_status` before starting or stopping capture.
- Query `profiler_get_metrics --json '{"listAvailable":true}'` before requesting named metrics.

## Package Operations

- Use `unity-package-management` for UPM discovery, dependency changes and scoped registries.
- Keep standalone console/profiler diagnostics in this skill.
