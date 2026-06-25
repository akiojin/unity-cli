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

The marketplace plugin installs skills only. Install the `unity-cli` binary
separately using one of the manual options below.

### Codex Skills

When using this repository with Codex, skills are available via `.codex/skills/` (symlinks to the plugin source).
No additional setup is required - just clone the repository.

### Quick Install (recommended)

```bash
curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
```

This downloads the latest release binary to `~/.unity/tools/unity-cli/{rid}/`
and symlinks it to `~/.local/bin/unity-cli`. After the initial install the CLI
checks for updates automatically in the background — no manual upgrades needed.

If `~/.local/bin` is not in your PATH, add the following to your shell profile
(e.g. `~/.zshrc` or `~/.bashrc`):

```bash
export PATH="$HOME/.local/bin:$PATH"
```

Set `UNITY_CLI_NO_AUTO_UPDATE=1` to disable auto-update.

### Manual Install

Download the latest binary from [GitHub
Releases](https://github.com/akiojin/unity-cli/releases), or install from a
local checkout:

```bash
git clone https://github.com/akiojin/unity-cli.git
cd unity-cli
cargo install --path .
```

Unity-side bridge package (choose one):

**OpenUPM** (recommended):

```bash
openupm add com.akiojin.unity-cli-bridge
```

**Git URL** (Unity Package Manager):

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

Connection check:

```bash
unity-cli system ping
```

Managed binary maintenance:

```bash
unity-cli cli doctor
unity-cli cli install
```

`unityd` and `lspd` automatically refresh their managed `unity-cli` / C# LSP binaries on daemon startup.
The managed copies live under `UNITY_CLI_TOOLS_ROOT` (or the OS default tools directory) and are updated without an interactive confirmation prompt.

## Skills (14)

These skills are written as workflow guides, not just command catalogs. They are designed to trigger from natural Unity requests such as "create a test scene", "trace references to this MonoBehaviour", or "run PlayMode tests and capture a screenshot".

| Category | Skills |
| --- | --- |
| Getting Started | `unity-cli-usage` |
| Scenes & Objects | `unity-scene-create`, `unity-scene-inspect`, `unity-gameobject-edit`, `unity-prefab-workflow` |
| Assets | `unity-asset-management`, `unity-addressables` |
| Code | `unity-csharp-navigate`, `unity-csharp-edit` |
| Runtime & Testing | `unity-playmode-testing`, `unity-input-system`, `unity-ui-automation` |
| Editor | `unity-editor-tools` |
| Maintenance | `gh-skills-sync` |

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

# Inspect machine-readable tool schema
unity-cli tool schema create_scene --output json

# Dry-run mutating tool calls (no side effects)
unity-cli --dry-run tool call create_scene --json '{"sceneName":"PreviewScene"}'

# Run EditMode tests
unity-cli tool call run_tests --json '{"mode":"editmode"}'
```

## GWT Spec Workflow

Feature specifications are managed in GitHub Issues labeled `gwt-spec`.
Use the Issue body as the source of truth for `Spec`, `Plan`, `Tasks`, and `TDD`.

## Contributing

External contributions should target the `develop` branch. Read
[CONTRIBUTING.md](CONTRIBUTING.md) before opening a PR, especially for branch
policy, required CI, and skill documentation changes.

## Configuration

| Variable | Description | Default |
| --- | --- | --- |
| `UNITY_PROJECT_ROOT` | Directory containing `Assets/` and `Packages/` | auto-detect |
| `UNITY_CLI_HOST` | Unity Editor host | `localhost` |
| `UNITY_CLI_PORT` | Unity Editor port | `6400` |
| `UNITY_CLI_TIMEOUT_MS` | Command timeout (ms) | `30000` |
| `UNITY_CLI_LSP_MODE` | LSP mode (`off` / `auto` / `required`) | `off` |
| `UNITY_CLI_TOOLS_ROOT` | Downloaded tools root directory | OS default |
| `UNITY_CLI_NO_AUTO_UPDATE` | Disable background auto-update (`1` to disable) | *(unset)* |

Legacy MCP-prefixed variables are not supported. Use `UNITY_CLI_*` only.

## Documentation

- Full command and tool catalog: [docs/tools.md](docs/tools.md)
- Development workflow and CI: [docs/development.md](docs/development.md)
- Contribution guide: [CONTRIBUTING.md](CONTRIBUTING.md)
- Release process: [RELEASE.md](RELEASE.md)
- Attribution templates: [ATTRIBUTION.md](ATTRIBUTION.md)

## License

MIT. See [ATTRIBUTION.md](ATTRIBUTION.md) for redistribution attribution templates.
