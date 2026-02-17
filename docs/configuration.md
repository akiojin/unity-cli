# Configuration

English | [日本語](#日本語)

`unity-cli` works without additional configuration, but these variables are useful for CI and multi-instance workflows.

## CLI Environment Variables

| Env | Default | Notes |
| --- | ---: | --- |
| `UNITY_PROJECT_ROOT` | auto-detect | Directory containing `Assets/` and `Packages/` |
| `UNITY_CLI_HOST` | `localhost` | Unity TCP listener host |
| `UNITY_CLI_PORT` | `6400` | Unity TCP listener port |
| `UNITY_CLI_TIMEOUT_MS` | `30000` | Command timeout (ms) |
| `UNITY_CLI_LSP_MODE` | `off` | `off`, `auto`, `required` |
| `UNITY_CLI_LSP_COMMAND` | (unset) | Explicit LSP command line |
| `UNITY_CLI_LSP_BIN` | (unset) | Explicit LSP executable path |
| `UNITY_CLI_TOOLS_ROOT` | platform default | Root directory for downloaded tools |

Backward-compatible aliases are still accepted:

- `UNITY_MCP_MCP_HOST` / `UNITY_MCP_UNITY_HOST` -> `UNITY_CLI_HOST`
- `UNITY_MCP_PORT` -> `UNITY_CLI_PORT`
- `UNITY_MCP_COMMAND_TIMEOUT` / `UNITY_MCP_CONNECT_TIMEOUT` -> `UNITY_CLI_TIMEOUT_MS`
- `UNITY_MCP_TOOLS_ROOT` -> `UNITY_CLI_TOOLS_ROOT`

### Minimal Example

```bash
export UNITY_PROJECT_ROOT=./UnityCliBridge
export UNITY_CLI_HOST=localhost
export UNITY_CLI_PORT=6400
```

## Unity Editor Settings

Unity: `Edit -> Project Settings -> Unity CLI Bridge`

- `Host`: bind/listen host (`localhost`, `0.0.0.0`, etc.)
- `Port`: TCP port (must match `UNITY_CLI_PORT`)

Click `Apply & Restart` to restart the Unity listener.

---

## 日本語

`unity-cli` はデフォルト設定のまま動作しますが、CIや複数インスタンス運用では以下の環境変数が有効です。

## CLI 環境変数

| 環境変数 | デフォルト | 補足 |
| --- | ---: | --- |
| `UNITY_PROJECT_ROOT` | 自動検出 | `Assets/` と `Packages/` を含むディレクトリ |
| `UNITY_CLI_HOST` | `localhost` | Unity TCP リスナーのホスト |
| `UNITY_CLI_PORT` | `6400` | Unity TCP リスナーのポート |
| `UNITY_CLI_TIMEOUT_MS` | `30000` | コマンドタイムアウト (ms) |
| `UNITY_CLI_LSP_MODE` | `off` | `off`, `auto`, `required` |
| `UNITY_CLI_LSP_COMMAND` | 未設定 | LSP 実行コマンド |
| `UNITY_CLI_LSP_BIN` | 未設定 | LSP 実行ファイルパス |
| `UNITY_CLI_TOOLS_ROOT` | OS依存既定 | ツール配置ルート |

後方互換として以下の `UNITY_MCP_*` も受け付けます。

- `UNITY_MCP_MCP_HOST` / `UNITY_MCP_UNITY_HOST` -> `UNITY_CLI_HOST`
- `UNITY_MCP_PORT` -> `UNITY_CLI_PORT`
- `UNITY_MCP_COMMAND_TIMEOUT` / `UNITY_MCP_CONNECT_TIMEOUT` -> `UNITY_CLI_TIMEOUT_MS`
- `UNITY_MCP_TOOLS_ROOT` -> `UNITY_CLI_TOOLS_ROOT`

### 最小例

```bash
export UNITY_PROJECT_ROOT=./UnityCliBridge
export UNITY_CLI_HOST=localhost
export UNITY_CLI_PORT=6400
```

## Unity エディタ設定

Unity: `Edit -> Project Settings -> Unity CLI Bridge`

- `Host`: 待受ホスト (`localhost`, `0.0.0.0` など)
- `Port`: TCP ポート（`UNITY_CLI_PORT` と一致させる）

`Apply & Restart` で Unity 側リスナーを再起動します。
