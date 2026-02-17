# タスク: マルチインスタンス安全切替 CLI (SPEC-911befe7)

**入力**: plan.md, research.md, data-model.md, contracts/
**前提条件**: Node.js 18/20/22, Unity インスタンスはテストでモック/ダミー TCP で代替可

## Phase 3.1: セットアップ
- [ ] T101 インスタンスレジストリ用ファイル `src/core/instanceRegistry.js` 追加
- [ ] T102 [P] CLI フレームに list/set-active コマンドの雛形追加

## Phase 3.2: テストファースト
- [ ] T103 tests/integration/instances.test.js に list→set-active→再list の統合テスト（2インスタンス、1停止ケース含む）
- [ ] T104 [P] tests/unit/core/instanceRegistry.test.js に add/markStatus/setActive/duplicate のユニットテスト
- [ ] T105 [P] tests/unit/cli/set-active.test.js に unreachable/invalid-id のエラー検証

## Phase 3.3: コア実装
- [ ] T106 src/core/instanceRegistry.js 実装（ヘルスチェック + アトミック setActive）
- [ ] T107 src/cli/commands/listInstances.js 実装（json/表形式両対応）
- [ ] T108 src/cli/commands/setActive.js 実装（1s タイムアウト、確認 ping）

## Phase 3.4: 統合
- [ ] T109 set-active 後に現在の接続先を server 状態へ反映し、他コマンドが参照できるようにする
- [ ] T110 エラーメッセージを日本語/英語で人間可読に整備

## Phase 3.5: 仕上げ
- [ ] T111 [P] README/quickstart へ list/set-active 使い方追記
- [ ] T112 [P] 手動スモーク: 2 ポートで 5 回切替し誤送信ゼロを確認

## 依存関係
- T103,T104,T105 → T106,T107,T108
- T106 → T109

## 並列実行候補
- [P] T102 と T104/T105 は並行可
- [P] T111,T112 は実装完了後並列可
