# Contract: Telemetry Configuration (SPEC-a28f3f95)

## 環境変数
- `UNITY_CLI_TELEMETRY`: `on` | `off` (デフォルト off)

## 起動ログ要件
- `telemetry: off` または `telemetry: on -> <destinations>` を必ず1行出力。
- off の場合、送信先/収集項目を空配列で表示。

## 送信先 (有効時のみ)
- destinations: URL[]
- fields: string[] (例: version, os, nodeVersion)
- payload: `{ event: "startup", version, os, node }`

## 無効時保証
- DNS ルックアップ・HTTP リクエストを行わない。
- tests/integration/http-mode.test.js でネットワークモック/pcap確認。
