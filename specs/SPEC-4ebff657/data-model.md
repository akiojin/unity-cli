# Data Model: Unity メニュー起動 & サンプル (SPEC-4ebff657)

## StartupSettings
- transport: `"stdio" | "http" | "both"`
- httpPort: int (optional)
- telemetryEnabled: bool
- lastStartedAt?: ISO datetime

## SampleWorkflowResult
- name: string (scene | addressables)
- success: bool
- message: string
- durationMs: number

## UiState
- isRunning: bool
- activeTransport: string
- portInfo: string
- lastLogLines: string[]
