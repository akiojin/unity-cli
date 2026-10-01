# MCP adapter

The CLI remains the primary implementation. `unity-cli mcp` is a thin stdio
adapter over the same tool catalog, argument validation, local tools, Unity
transport, unityd and authentication. It does not implement a second set of Unity
operations. Claude Desktop and the legacy Node.js MCP server are outside this
distribution.

## Start and inspect

```bash
unity-cli mcp --project-path /path/to/UnityProject
npx @modelcontextprotocol/inspector --cli unity-cli mcp --method tools/list
npx @modelcontextprotocol/inspector --cli unity-cli mcp \
  --method tools/call --tool-name create_gameobject --tool-arg name=McpCube
npx @modelcontextprotocol/inspector --cli unity-cli mcp \
  --method tools/call --tool-name get_hierarchy
```

Run Inspector from the target Unity project. To select another project, add
`-e UNITY_PROJECT_ROOT=/path/to/UnityProject` after `unity-cli mcp`; Inspector
controls which environment variables reach its child process.

The adapter negotiates the MCP initialization protocol through `2025-11-25`.
It accepts newline-delimited JSON-RPC on stdin and writes only protocol messages
to stdout; diagnostics use stderr. It exits on stdin EOF. Start it through an MCP
client, not as a detached background daemon.

An Editor is not required at startup. The catalog is available immediately.
After initialization the adapter polls the shared tool discovery every two seconds
and sends `notifications/tools/list_changed` when connectivity or the published
catalog changes. Project tools from `[UnityCliBridge.Tools.UnityCliTool]` use the
same descriptions, parameter schemas and execution path as `tool list` / `raw`.
An unavailable Editor leaves the builtin catalog available. Endpoint
resolution runs again for each tool call, so an Editor started later can be
discovered. `--project-path`, `--host`, `--port`, and the normal `UNITY_CLI_*`
settings apply. Multiple Editors require an unambiguous target.

`tools/call` returns the existing `{success, command, data, errors, warnings}`
envelope as MCP text content. A tool failure sets `isError: true`, retaining
the original error code and bridge payload. Invalid MCP requests and unknown
tools use JSON-RPC errors. A mutation timeout does not imply that nothing ran;
inspect the returned request/job identifier before repeating it.

Authentication is identical to ordinary CLI calls. The adapter discovers the
endpoint's private Editor token or reads `UNITY_CLI_AUTH_TOKEN_FILE`; it does not
write tokens into client settings. In particular, `eval_csharp` retains the
existing authenticated, non-retrying execution path.

## Configure a client

```bash
unity-cli mcp configure cursor --local --dry-run
unity-cli mcp configure cursor --local
unity-cli mcp configure claude-code
unity-cli mcp configure vscode --local
unity-cli mcp configure windsurf
unity-cli mcp configure codex --local
```

`--dry-run` prints a unified diff without creating directories or modifying
files. Running without it applies the same diff using an atomic replacement in
the destination directory. Repeating the command is idempotent. Other servers,
top-level keys and existing `unity-cli` options such as `env` are retained;
only its command, arguments and stdio transport are managed. TOML comments are
retained. Invalid configurations, symlink files/directories below the selected
root, and an existing HTTP server named `unity-cli` are rejected without writing.
JSON files must contain valid JSON; JSONC comments/trailing commas must be removed
before configuring. Dry-run output can include surrounding existing settings,
so review it before sharing.

| Client | Default user configuration | `--local` in the current directory |
| --- | --- | --- |
| `claude-code` | `~/.claude.json` (`mcpServers`) | `.mcp.json` (Claude Code's **project** scope) |
| `cursor` | `~/.cursor/mcp.json` (`mcpServers`) | `.cursor/mcp.json` |
| `vscode` | macOS: `~/Library/Application Support/Code/User/mcp.json` (`servers`) | `.vscode/mcp.json` |
| `windsurf` | `~/.codeium/windsurf/mcp_config.json` (`mcpServers`) | Unsupported; the command rejects `--local` |
| `codex` | `~/.codex/config.toml` (`mcp_servers`) | `.codex/config.toml` |

On Windows, VS Code uses `%APPDATA%\Code\User\mcp.json` (with
`%USERPROFILE%\AppData\Roaming` as the fallback). On Linux it uses
`$XDG_CONFIG_HOME/Code/User/mcp.json`, falling back to `~/.config`. Other default
paths are relative to the user's home on every OS. Codex honors `CODEX_HOME` for
user configuration. Local configuration includes the absolute current project
path in server arguments; regenerate it after moving a project or on another
machine. Codex loads project configuration only for trusted projects.

The generated command is `unity-cli`, which must be available on the client's
PATH. Restart/reload the client after configuration and accept its normal MCP
trust prompt. Windsurf here means the client using the `.codeium/windsurf`
configuration; its successor Devin Desktop has a different configuration path
and is not configured by this command.

## Verification

`cargo test --test mcp --test mcp_dynamic -- --test-threads=1` checks catalog
parity, error results, live custom-tool removal, and five-client configuration
behavior in temporary directories. For isolated
real-Editor acceptance runs (macOS Apple Silicon):

```bash
cargo build
python3 scripts/e2e-mcp.py --version 6000.3.25f1 --output /tmp/mcp-6000 --port 6550 --custom-tools
python3 scripts/e2e-mcp.py --version 2022.3.62f3 --output /tmp/mcp-2022 --port 6551 --custom-tools
```

The runner pins MCP Inspector, starts MCP before the Editor, checks the live
notification, compares tool names, creates a GameObject, reads its hierarchy,
and verifies authenticated eval plus a rejected token with no scene side effect.
`--custom-tools` adds a project tool fixture and verifies its publication and call.
It also compares CLI and MCP schemas exactly on raw stdio; Inspector's JavaScript
JSON parser rounds integer bounds beyond its safe integer range.
Use new output directories for each run. They contain raw Inspector output and a
summary; Editor lockfiles contain private tokens and must not be published.

Configuration/protocol references: [MCP lifecycle](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle),
[MCP tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools),
[Claude Code scopes](https://code.claude.com/docs/en/mcp),
[Cursor MCP](https://cursor.com/docs/context/mcp),
[VS Code configuration](https://code.visualstudio.com/docs/agents/reference/mcp-configuration),
[Codex MCP](https://developers.openai.com/codex/mcp),
[Windsurf/Devin Desktop configuration](https://docs.windsurf.com/windsurf/cascade/mcp).
