# 実装計画: HTTP/プロキシ対応とテレメトリ透明性

**機能ID**: `SPEC-a28f3f95` | **日付**: 2025-12-04 | **仕様**: [spec.md](./spec.md)
**入力**: `/specs/SPEC-a28f3f95/spec.md` の機能仕様

## 概要
HTTP/JSON-RPC での起動モードを追加し、ポート競合検知とヘルスチェックを提供する。同時にテレメトリ挙動をデフォルト無効・明示化し、送信先/オプトアウトをログと README に反映する。既存 stdio/TCP モードとの互換性を維持し、ポート競合時はガイド付きで安全に失敗する。

## 技術コンテキスト
**言語/バージョン**: Node.js 18/20/22 LTS (ESM)
**主要依存関係**: @modelcontextprotocol/sdk, fastify/express不使用（軽量HTTPサーバーは標準httpで実装予定）、better-sqlite3（既存）
**ストレージ**: なし（設定は既存 config）
**テスト**: node --test (contract/integration/unit)
**対象プラットフォーム**: Linux/macOS/Windows, Unity 2020.3+ クライアント
**プロジェクトタイプ**: single (unity-cli)
**パフォーマンス目標**: HTTP ヘルスチェック応答 ≤50ms ローカル, 起動時ポート競合検知 ≤200ms
**制約**: 既存 CLI 互換、外向き通信ゼロを保証 (テレメトリ無効時)
**スケール/スコープ**: 1 HTTP リスナー追加 + ログ/設定ドキュメント更新

## 憲章チェック
**シンプルさ**: プロジェクト1、標準 http モジュール直使用、追加ラッパーなし → OK
**アーキテクチャ**: 既存 unity-cli に HTTP リスナーを追加、ライブラリ分割不要 → OK
**テスト**: RED→GREEN→REFACTOR、contract→integration→unit 順で作成 → 厳守
**可観測性**: 起動ログにチャネル/ポート/テレメトリ状態を出力、ヘルスチェックで状態確認 → OK
**バージョニング**: 破壊的変更なし、semantic-release patch/minor 予定 → OK

## プロジェクト構造
```
unity-cli/
  src/
    core/httpServer.js        # 新規: HTTP トランスポート
    core/server.js            # 既存: 起動オプションにhttp追加
  tests/
    contract/http-health.test.js
    integration/http-mode.test.js
    unit/core/httpServer.test.js
specs/SPEC-a28f3f95/
  spec.md
  plan.md
  research.md
  data-model.md
  quickstart.md
  contracts/
    http-endpoints.md
    telemetry-config.md
  tasks.md (後で /speckit.tasks 相当で作成)
```

## Phase 0: アウトライン＆リサーチ (research.md に反映)
- HTTP 実装方式: Node.js 標準 http vs fastify/express → シンプルさ優先で標準 http を選定。
- ポート競合検知: server.listen エラーハンドリングで EADDRINUSE を捕捉し、再提案ポートを返す。
- ヘルスチェック仕様: `GET /healthz` 200/JSON {status:"ok", mode:"http"}。
- テレメトリ: デフォルト無効、環境変数 `UNITY_MCP_TELEMETRY=on/off` で切替、送信先なしを保証。

## Phase 1: 設計＆契約 (data-model.md, contracts/, quickstart.md)
- data-model: ConnectionChannelSetting, TelemetrySetting のフィールド定義。
- contracts: http-endpoints (healthz, errorフォーマット), telemetry-config (環境変数・ログ出力項目)。
- quickstart: HTTP 起動手順、ポート競合時の再試行例、テレメトリ設定例。

## Phase 2: タスク計画アプローチ
`/specs/SPEC-a28f3f95/tasks.md` にテスト優先で作成 (次フェーズ)。
