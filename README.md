# unity-cli

Rust-based CLI for Unity Editor automation over Unity TCP.

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

## Environment Variables

- `UNITY_PROJECT_ROOT`
- `UNITY_CLI_HOST`
- `UNITY_CLI_PORT`
- `UNITY_CLI_TIMEOUT_MS`
- `UNITY_CLI_LSP_MODE` (`off` | `auto` | `required`)
- `UNITY_CLI_LSP_COMMAND`
- `UNITY_CLI_LSP_BIN`

Backward-compatible `UNITY_MCP_*` aliases are also accepted.

## Development

- Contributing: `CONTRIBUTING.md`
- Development guide: `docs/development.md`
- Release guide: `RELEASE.md`

## License

MIT (`LICENSE`)

If you build an app using `unity-cli`, please include attribution (credits/about/README).
Recommended:

`This product uses unity-cli (https://github.com/akiojin/unity-cli), licensed under MIT.`
