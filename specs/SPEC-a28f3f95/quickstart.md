# Quickstart: HTTP/プロキシ対応 & テレメトリ透明性

## HTTP モード起動
```
npx @akiojin/unity-cli --http 6401
# 出力例: HTTP listening on http://localhost:6401, telemetry: off
curl -s http://localhost:6401/healthz
```

## ポート競合時の再試行
```
npx @akiojin/unity-cli --http 6401 || \
  npx @akiojin/unity-cli --http 6501
```

## テレメトリ設定
- 既定: 送信なし（外向き通信ゼロ）。
- 明示的に有効化: `UNITY_MCP_TELEMETRY=on npx @akiojin/unity-cli --http`
- 無効化確認: 起動ログに `telemetry: off` 表示、ネットワークキャプチャで送信 0 件。

## 併用 (stdio + HTTP)
```
npx @akiojin/unity-cli --stdio --http 6401
```
有効チャネルは起動ログで確認する。
