# Data Model: HTTP/プロキシ & Telemetry (SPEC-a28f3f95)

## ConnectionChannelSetting
- mode: `"stdio" | "tcp" | "http" | "both"`
- httpPort: number (optional, required when mode includes http)
- host: string (default localhost)
- healthPath: string (default `/healthz`)
- status: `"starting" | "ready" | "error"`
- error?: string (last startup error)

## TelemetrySetting
- enabled: boolean (default false)
- optInSource: `"env" | "cli" | "config"`
- destinations: string[] (when enabled; empty when disabled)
- fields: string[] (collected fields; empty when disabled)
- lastReportedAt?: ISO datetime (when enabled)

## HealthResponse (HTTP)
- status: `"ok" | "error"`
- mode: `"http"`
- uptimeMs: number
- telemetryEnabled: boolean
- port: number
- error?: string
