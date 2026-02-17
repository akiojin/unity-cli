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
- Unity test project: `UnityCliBridge/TestProject`

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
| `UNITY_CLI_LSP_COMMAND` | Explicit LSP executable command | - |
| `UNITY_CLI_LSP_BIN` | Explicit LSP executable path | - |

`UNITY_MCP_*` variables are deprecated since v0.1.0 and will be removed in v1.0.0. See [docs/configuration.md](docs/configuration.md) for the migration guide.

## Output Modes

- Default: text
- JSON: `--output json`

```bash
cargo run -- --output json system ping
```

## License

MIT - See [ATTRIBUTION.md](ATTRIBUTION.md) for attribution templates when redistributing.
