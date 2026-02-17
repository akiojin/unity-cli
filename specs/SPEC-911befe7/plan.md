# 実装計画: マルチインスタンス安全切替 CLI

**機能ID**: `SPEC-911befe7` | **日付**: 2025-12-04 | **仕様**: [spec.md](./spec.md)
**入力**: `/specs/SPEC-911befe7/spec.md` の機能仕様

## 概要
複数 Unity MCP インスタンスを CLI から検出・表示し、明示的に接続先を切替える `list-instances` / `set-active` を追加。疎通不可インスタンスは安全に失敗し、再選択ガイドを返す。誤送信ゼロを目標。

## 技術コンテキスト
**言語/バージョン**: Node.js 18/20/22 (ESM)
**主要依存関係**: @modelcontextprotocol/sdk (既存), net (TCP ping), dns なし
**ストレージ**: なし（メモリ内レジストリ）
**テスト**: node --test (integration/unit)
**対象プラットフォーム**: 開発マシン (mac/Win/Linux) + Unity 2020.3+ サーバー
**プロジェクトタイプ**: single (unity-cli)
**パフォーマンス目標**: list 100ms, set-active 200ms 以内ローカル
**制約**: 既存 stdio/TCP 接続ロジックに追加、LSP 通信の安定性を壊さない
**スケール/スコープ**: 同時 3-5 インスタンス想定

## 憲章チェック
シンプルさ: 追加 CLI 2 コマンドのみ、レジストリは単純配列 → OK
アーキテクチャ: 既存 server/core にインスタンス管理モジュール追加 → OK
テスト: RED→GREEN→REFACTOR、integration で誤送信ゼロ確認 → 厳守
可観測性: list/set の結果を常にログ/レスポンスに表示 → OK
バージョニング: 非破壊、semantic-release patch → OK

## プロジェクト構造
```
unity-cli/
  src/
    core/instanceRegistry.js   # 新規: インスタンス管理
    cli/commands/listInstances.js
    cli/commands/setActive.js
  tests/
    integration/instances.test.js
    unit/core/instanceRegistry.test.js
specs/SPEC-911befe7/
  spec.md
  plan.md
  research.md
  data-model.md
  quickstart.md
  contracts/cli-commands.md
  tasks.md
```

## Phase 0: リサーチ (research.md)
- インスタンス検出: 既存設定ファイル or 動的 ping? → 既存設定(ports list) + ヘルスチェック ping で確認。
- ID 付与方式: host:port を ID にする（安定で衝突少）。
- タイムアウト: ping 1s, set-active 1s 以内で失敗を返す。

## Phase 1: 設計 & 契約
- data-model: InstanceEntry, InstanceRegistry, ConnectionState。
- contracts: CLI 引数/出力フォーマット (list, set-active), エラー構造。
- quickstart: 2 インスタンス起動 → list → set-active → コマンド送信確認手順。

## Phase 2: タスク計画アプローチ
tasks.md に TDD 順で作成（次フェーズ）。
