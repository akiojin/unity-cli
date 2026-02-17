# Contract: HTTP Endpoints (SPEC-a28f3f95)

## GET /healthz
- 200 OK
- Body: `{ "status": "ok", "mode": "http", "port": <number>, "telemetryEnabled": <bool>, "uptimeMs": <number> }`
- Headers: `Content-Type: application/json; charset=utf-8`
- Failure: 503 `{ "status": "error", "error": "<message>" }`

## Error format (共通)
- statusCode: number
- error: string (human readable)
- detail?: string

## ポート競合
- listen 時に EADDRINUSE を検知
- CLI exit code ≠0
- stderr に "port in use" と代替ポート候補3つ (例: +1, +2, ランダム) を出力
