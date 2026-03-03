# unity-cli

[日本語](README.ja.md) | [中文](README.zh.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Italiano](README.it.md) | [Español](README.es.md)

`unity-cli` is a Rust CLI that lets Claude Code control Unity Editor over direct TCP.
It is the successor to [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server), redesigned from Node.js + MCP to a native binary workflow.

## Why unity-cli

- Operate Unity from Claude Code with focused skills and typed commands.
- Access `101` Unity Tool APIs across scene, asset, code, test, UI, and editor domains.
- Run as a single binary with fast startup and low overhead.

## How It Works

```text
Claude Code
  -> Skills (on demand)
  -> unity-cli
  -> Unity Editor (TCP bridge)
```

Some code tools (`read`, `search`, `find_symbol`, `find_refs`, etc.) run locally without a Unity connection.

## Getting Started

### Recommended: Claude Code Plugin

Install the `unity-cli` plugin from Claude Code Marketplace:

```bash
/plugin marketplace add akiojin/unity-cli
```

When `cargo` is available, plugin setup can install or update `unity-cli` automatically.

### Codex Skills

When using this repository with Codex, skills are available via `.codex/skills/` (symlinks to the plugin source).
No additional setup is required — just clone the repository.

### Manual Install

```bash
cargo install unity-cli
```

Unity-side UPM package URL:

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

Connection check:

```bash
unity-cli system ping
```

## Skills (13)

| Category | Skills |
| --- | --- |
| Getting Started | `unity-cli-usage` |
| Scenes & Objects | `unity-scene-create`, `unity-scene-inspect`, `unity-gameobject-edit`, `unity-prefab-workflow` |
| Assets | `unity-asset-management`, `unity-addressables` |
| Code | `unity-csharp-navigate`, `unity-csharp-edit` |
| Runtime & Testing | `unity-playmode-testing`, `unity-input-system`, `unity-ui-automation` |
| Editor | `unity-editor-tools` |

## Quick Examples

```bash
# Connectivity
unity-cli system ping

# Create a scene
unity-cli scene create MainScene

# Create a GameObject through raw tool call
unity-cli raw create_gameobject --json '{"name":"Player"}'

# Search C# code (local tool)
unity-cli tool call search --json '{"pattern":"PlayerController"}'

# Run EditMode tests
unity-cli tool call run_tests --json '{"mode":"editmode"}'
```

## Configuration

| Variable | Description | Default |
| --- | --- | --- |
| `UNITY_PROJECT_ROOT` | Directory containing `Assets/` and `Packages/` | auto-detect |
| `UNITY_CLI_HOST` | Unity Editor host | `localhost` |
| `UNITY_CLI_PORT` | Unity Editor port | `6400` |
| `UNITY_CLI_TIMEOUT_MS` | Command timeout (ms) | `30000` |
| `UNITY_CLI_LSP_MODE` | LSP mode (`off` / `auto` / `required`) | `off` |
| `UNITY_CLI_TOOLS_ROOT` | Downloaded tools root directory | OS default |

Legacy MCP-prefixed variables are not supported. Use `UNITY_CLI_*` only.

## Documentation

- Full command and tool catalog: [docs/tools.md](docs/tools.md)
- Development workflow and CI: [docs/development.md](docs/development.md)
- Contribution guide: [CONTRIBUTING.md](CONTRIBUTING.md)
- Release process: [RELEASE.md](RELEASE.md)
- Attribution templates: [ATTRIBUTION.md](ATTRIBUTION.md)

## License

MIT. See [ATTRIBUTION.md](ATTRIBUTION.md) for redistribution attribution templates.
