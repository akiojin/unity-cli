# Troubleshooting

English | [日本語](#日本語)

## Quick Checks

1. Unity Editor is running.
2. `Unity CLI Bridge` package is installed.
3. Unity TCP listener is active (default `6400`).
4. `UNITY_CLI_HOST` / `UNITY_CLI_PORT` points to that listener.

## Connection Issues

| Symptom | Cause | Fix |
| --- | --- | --- |
| `Connection timeout` | Unity not running | Start Unity Editor |
| `ECONNREFUSED` | Listener not active / wrong port | Reopen Unity project settings and restart listener |
| `invalid response` | Protocol mismatch or stale build | Reimport package and restart Unity |

## LSP Issues

| Symptom | Fix |
| --- | --- |
| LSP command not found | Set `UNITY_CLI_LSP_BIN` or `UNITY_CLI_LSP_COMMAND` |
| LSP timeout | Increase `UNITY_CLI_TIMEOUT_MS` and retry |
| LSP required but unavailable | Use `UNITY_CLI_LSP_MODE=auto` during setup |

## WSL2/Docker -> Windows Unity

```bash
export UNITY_CLI_HOST=host.docker.internal
export UNITY_CLI_PORT=6400
```

If your process starts outside the workspace, set:

```bash
export UNITY_PROJECT_ROOT=/absolute/path/to/UnityCliBridge
```

## Known Issues

- `Capabilities: none`: see `docs/troubleshooting/capabilities-none.md`

---

## 日本語

## まず確認

1. Unity Editor が起動していること
2. `Unity CLI Bridge` パッケージが導入されていること
3. Unity TCP リスナーが起動していること（デフォルト `6400`）
4. `UNITY_CLI_HOST` / `UNITY_CLI_PORT` が一致していること

## 接続エラー

| 症状 | 原因 | 対処 |
| --- | --- | --- |
| `Connection timeout` | Unity未起動 | Unity Editorを起動 |
| `ECONNREFUSED` | リスナー未起動 / ポート不一致 | Project Settingsで再起動 |
| `invalid response` | プロトコル不一致/古いビルド | パッケージ再import後にUnity再起動 |

## LSP関連

| 症状 | 対処 |
| --- | --- |
| LSP実行ファイルが見つからない | `UNITY_CLI_LSP_BIN` または `UNITY_CLI_LSP_COMMAND` を設定 |
| LSPタイムアウト | `UNITY_CLI_TIMEOUT_MS` を延長 |
| 必須LSPモードで失敗 | セットアップ中は `UNITY_CLI_LSP_MODE=auto` を利用 |

## WSL2/Docker -> Windows Unity (日本語)

```bash
export UNITY_CLI_HOST=host.docker.internal
export UNITY_CLI_PORT=6400
```

ワークスペース外でプロセスが起動される場合は次を設定:

```bash
export UNITY_PROJECT_ROOT=/absolute/path/to/UnityCliBridge
```

## 既知の問題

- `Capabilities: none`: `docs/troubleshooting/capabilities-none.md`
