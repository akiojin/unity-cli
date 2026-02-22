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
| `UNITY_CLI_TOOLS_ROOT` | platform default | Root directory for downloaded tools |

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

## Unsupported Legacy Variables / 旧変数は未サポート

Legacy MCP-prefixed variables are not accepted by `unity-cli`.

MCPプレフィックスの旧環境変数は `unity-cli` では受け付けません。

If any legacy MCP-prefixed variable is present, `unity-cli` returns an error and exits.

旧MCPプレフィックス変数が検出された場合、`unity-cli` はエラー終了します。

Use only `UNITY_CLI_*` variables in all environments.

すべての環境で `UNITY_CLI_*` のみを使用してください。

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
| `UNITY_CLI_TOOLS_ROOT` | OS依存既定 | ツール配置ルート |

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
