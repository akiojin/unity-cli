# 実装計画: Unity接続設定の分離

**機能ID**: `SPEC-e7035f70`
**作成日**: 2025-10-17
**ステータス**: 完了

## 憲章チェック
- TDD: RED→GREEN→REFACTOR を順守し、設定読み込みのユニットテストで要件を担保する。
- シンプルさ: 既存構造を踏襲し、新規プロパティ追加と既存ロジックの最小変更で対応する。
- LLM最適化: 設定名と説明を明瞭化し、READMEの表も更新して参照性を高める。

## 技術方針
- Node 側は環境変数（例: `UNITY_MCP_MCP_HOST` / `UNITY_MCP_PORT`）で Unity 接続先を設定する。
- Unity 側は Project Settings（Edit → Project Settings → Unity MCP Server）の Host/Port を待ち受けに使用する。
- 旧 設定ファイル方式は廃止（移行は `docs/configuration.md` を参照）。

## オープンな質問
- 既存利用者への移行ガイドを README にどの程度記載するか → 要約とサンプル設定を追記する。
- デフォルト値は従来どおり `localhost` とするか → 変更せず。

## Phase 0: リサーチ
- 現行の設定読み込みコードとテストを把握し、後方互換の考慮点を整理する。

## Phase 1: 設計
- Node 側設定モジュールに新しいプロパティを追加し、Unity 接続クラスで使用する流れを定義する。
- README（英/日）に設定例と移行案内を記載する。

## Phase 2: タスク計画
1. テスト追加（RED）: `mcpHost` / `unityHost` の読込と旧キーからのフォールバックを検証するユニットテストを作成。
2. 実装（GREEN）: 設定モジュール・Unity 接続クラス・ドキュメント（`docs/configuration.md`）を更新。
3. リファクタ: README の設定表やコメントを整備し、識別しやすくする。
