# タスク: HTTP/プロキシ対応とテレメトリ透明性 (SPEC-a28f3f95)

**入力**: plan.md, research.md, data-model.md, contracts/
**前提条件**: Node.js 18/20/22, Unity 不要 (サーバー単体)

## Phase 3.1: セットアップ
- [ ] T001 リポ構成確認と http モード用フォルダ `src/core/httpServer.js` 追加
- [ ] T002 [P] ESLint/Prettier 設定に HTTP ファイルを含める

## Phase 3.2: テストファースト (TDD)
- [ ] T003 tests/contract/http-health.test.js に GET /healthz contract test (200, error)
- [ ] T004 [P] tests/integration/http-mode.test.js に HTTP 起動/停止・ポート競合シナリオ
- [ ] T005 tests/unit/core/httpServer.test.js に EADDRINUSE/invalid-port のユニットテスト
- [ ] T006 [P] tests/unit/core/telemetry.test.js に telemetry on/off ログ出力と送信ゼロを検証

## Phase 3.3: コア実装
- [ ] T007 src/core/httpServer.js 実装（healthz, graceful shutdown, port check）
- [ ] T008 src/core/server.js に HTTP 起動オプション追加（stdio/TCP 共存）
- [ ] T009 CLI オプション/README に --http, telemetry 設定を追記

## Phase 3.4: 統合
- [ ] T010 ポート競合時の代替ポート提案ロジック実装＆ログ
- [ ] T011 テレメトリ設定のログ出力と外向き通信ブロック確認（テストと連動）

## Phase 3.5: 仕上げ
- [ ] T012 [P] docs/README 更新（Quickstart, env, error examples）
- [ ] T013 [P] manual smoke: HTTP ヘルスチェック3回成功を記録

## 依存関係
- T003,T004,T005,T006 → T007,T008,T010,T011
- T007 → T008
- T008 → T009

## 並列実行候補
- [P] T002 と T003/T004/T005/T006 は並列可（別ファイル）
- [P] T012,T013 は実装完了後並列可
