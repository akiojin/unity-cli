---
name: unity-cli-usage
description: Practical usage guide for the Rust-based unity-cli command set, including raw command fallback and instance switching.
allowed-tools: Read, Grep, Glob
---

# unity-cli Usage (Claude Code)

Use `unity-cli` as the primary Unity automation interface.

## Preferred Order

1. Use typed subcommands (`system`, `scene`, `instances`) when available.
2. Use `raw` for the rest of Unity command types.
3. Use `--output json` when chaining automation steps.

## Examples

```bash
unity-cli system ping
unity-cli scene create MainScene --path Assets/Scenes/
unity-cli raw create_gameobject --json '{"name":"Player"}'
unity-cli instances list --ports 6400,6401
unity-cli instances set-active localhost:6401
```

## Safety Notes

- If `instances set-active` fails with `unreachable`, run `instances list` and pick an `up` target.
- Keep payloads as valid JSON objects.
- Prefer explicit host/port in CI (`UNITY_CLI_HOST`, `UNITY_CLI_PORT`).
