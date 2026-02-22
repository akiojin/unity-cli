# unity-cli

Rust-based CLI for Unity Editor automation over Unity TCP.
Successor to [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server) — rewritten from Node.js/MCP to native Rust.

## Install

From crates.io:

```bash
cargo install unity-cli
```

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

Legacy MCP-prefixed variables are not supported. Use `UNITY_CLI_*` only. See [docs/configuration.md](docs/configuration.md).

## Output Modes

- Default: text
- JSON: `--output json`

```bash
cargo run -- --output json system ping
```

## License

MIT - See [ATTRIBUTION.md](ATTRIBUTION.md) for attribution templates when redistributing.
