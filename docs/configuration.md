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

## Deprecation Policy / 廃止ポリシー

### Overview / 概要

The legacy `UNITY_MCP_*` environment variables are **deprecated since v0.1.0** and will be **removed in v1.0.0**.

レガシーの `UNITY_MCP_*` 環境変数は **v0.1.0 で非推奨** となり、**v1.0.0 で削除** されます。

When a deprecated variable is detected at runtime, a warning is emitted via the `tracing` logger:

非推奨の環境変数が実行時に検出されると、`tracing` ロガー経由で警告が出力されます。

```
WARN Environment variable 'UNITY_MCP_PORT' is deprecated and will be removed in v1.0.0. Use 'UNITY_CLI_PORT' instead.
```

### Migration Table / 移行表

| Deprecated Variable (非推奨) | Replacement (移行先) |
| --- | --- |
| `UNITY_MCP_MCP_HOST` | `UNITY_CLI_HOST` |
| `UNITY_MCP_UNITY_HOST` | `UNITY_CLI_HOST` |
| `UNITY_MCP_PORT` | `UNITY_CLI_PORT` |
| `UNITY_MCP_COMMAND_TIMEOUT` | `UNITY_CLI_TIMEOUT_MS` |
| `UNITY_MCP_CONNECT_TIMEOUT` | `UNITY_CLI_TIMEOUT_MS` |
| `UNITY_MCP_TOOLS_ROOT` | `UNITY_CLI_TOOLS_ROOT` |

### Behavior / 動作

1. If a `UNITY_CLI_*` variable is set, it is always used (primary).
2. If only a deprecated `UNITY_MCP_*` variable is set, its value is used **with a deprecation warning**.
3. If neither is set, the built-in default value is used.

---

1. `UNITY_CLI_*` 変数が設定されている場合、常にそちらが優先されます。
2. 非推奨の `UNITY_MCP_*` 変数のみが設定されている場合、その値が使用されますが **非推奨警告が出力されます**。
3. どちらも設定されていない場合、組み込みのデフォルト値が使用されます。

### Timeline / タイムライン

| Version | Action |
| --- | --- |
| v0.1.0 | `UNITY_MCP_*` variables deprecated; runtime warnings added |
| v1.0.0 | `UNITY_MCP_*` variables removed; only `UNITY_CLI_*` supported |

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
