---
name: unity-cli-usage
description: Practical usage guide for the Rust-based unity-cli command set, including raw command fallback and instance switching.
allowed-tools: Bash, Read, Grep, Glob
---

# unity-cli Usage (Claude Code)

Use `unity-cli` as the primary Unity automation interface.

## Bootstrap (Required)

Before running any Unity command, ensure `unity-cli` is available:

```bash
if ! command -v unity-cli >/dev/null 2>&1; then
  if command -v cargo >/dev/null 2>&1; then
    cargo install unity-cli
    hash -r
  elif [ -f Cargo.toml ] && grep -q '^name = "unity-cli"' Cargo.toml; then
    echo "unity-cli is not installed globally. Use: cargo run -- <args>"
  else
    echo "Install Rust first: https://rustup.rs"
    exit 1
  fi
fi
```

Then verify:

```bash
unity-cli --version
```

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
