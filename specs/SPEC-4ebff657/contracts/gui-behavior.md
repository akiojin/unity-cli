# Contract: GUI Behavior (SPEC-4ebff657)

## Menu Items
- MCP Server/Start: 停止中のみ有効。クリックでプロセス起動。
- MCP Server/Stop: 稼働中のみ有効。クリックでプロセス終了。
- MCP Server/Run Sample (Scene): 稼働中のみ有効。
- MCP Server/Run Sample (Addressables): 稼働中 + Addressables 有効時のみ有効。

## Window UI
- Toggle: transport (stdio/http/both)
- Input: httpPort (int, default 6401)
- Toggle: telemetry (on/off)
- Status label: "Stopped" | "Starting" | "Running" | "Error"
- Log area: 最新ログ 20 行

## Success/Failure 表示
- 起動成功: status Running, 現在の transport/port を表示。
- 起動失敗: status Error, error message を表示し再試行ボタンを出す。
- サンプル成功: toast/console に Result OK, duration を表示。
- サンプル失敗: Error と後処理ガイドを表示。
