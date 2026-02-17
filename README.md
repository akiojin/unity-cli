# unity-cli

Rust-based CLI for Unity Editor automation via the Unity TCP protocol.

## Install

From crates.io (after publish):

```bash
cargo install unity-cli
```

From GitHub:

```bash
cargo install --git https://github.com/akiojin/unity-cli.git unity-cli
```

## Goals

- Replace the old Node.js runtime with a native Rust CLI
- Keep command execution compatible with existing Unity command types
- Provide a practical subcommand UX for common workflows

## Quick Start

```bash
cd unity-cli
cargo run -- system ping
```

Call any Unity command directly:

```bash
cargo run -- raw create_scene --json '{"sceneName":"MainScene"}'
```

List and switch active instances:

```bash
cargo run -- instances list --ports 6400,6401
cargo run -- instances set-active localhost:6401
```

## Command Overview

- `raw <tool> --json '{...}'`: direct command execution (fallback for all 108 tools)
- `tool list`: list all supported tool names (108)
- `tool call <tool> --json '{...}'`: explicit alias of `raw`
- `tool <tool> --json '{...}'`: direct named tool invocation (validated against catalog)
- `system ping [--message text]`
- `scene create <scene_name> [--path ...] [--load-scene ...] [--add-to-build-settings ...]`
- `instances list [--ports csv] [--host host]`
- `instances set-active <host:port>`

## Local Tool Execution (Rust-side)

The following tool names are executed locally without Unity TCP roundtrip:

- `read`
- `search`
- `list_packages`
- `get_symbols`
- `build_index`
- `update_index`
- `find_symbol`
- `find_refs`

### Script/Index workflow

```bash
export UNITY_PROJECT_ROOT=/path/to/UnityProject
cargo run -- --output json tool build_index --json '{"excludePackageCache":true}'
cargo run -- --output json tool find_symbol --json '{"name":"MyClass","kind":"class","exact":true}'
cargo run -- --output json tool find_refs --json '{"name":"MyClass","pageSize":20}'
cargo run -- --output json tool update_index --json '{"paths":["Assets/Scripts/MyClass.cs"]}'
```

## Unity Package (UPM)

`unity-cli` ships the Unity-side bridge package in this repository:

- `UnityCliBridge/Packages/unity-cli-bridge`

Install from Git URL in Unity Package Manager:

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

## LSP (Bundled)

The C# LSP implementation is bundled in:

- `lsp/Program.cs`
- `lsp/Server.csproj`

Run LSP tests:

```bash
dotnet test lsp/Server.Tests.csproj
```

## Repository Split / Release

- Subtree export script: `scripts/export-unity-cli-subtree.sh`
- Release workflow (GitHub Actions): `.github/workflows/unity-cli-release.yml`
- Detailed steps: `unity-cli/RELEASE.md`

## Environment Variables

- `UNITY_CLI_HOST` (fallback: `UNITY_MCP_MCP_HOST`, `UNITY_MCP_UNITY_HOST`)
- `UNITY_CLI_PORT` (fallback: `UNITY_MCP_PORT`)
- `UNITY_CLI_TIMEOUT_MS` (fallback: `UNITY_MCP_COMMAND_TIMEOUT`)
- `UNITY_CLI_LSP_MODE` (`off` | `auto` | `required`, default: `off`)
- `UNITY_CLI_LSP_COMMAND` (explicit LSP executable command)
- `UNITY_CLI_LSP_BIN` (explicit LSP executable path)

## Output Modes

- Default: text
- JSON: `--output json`

Example:

```bash
cargo run -- --output json system ping
```
