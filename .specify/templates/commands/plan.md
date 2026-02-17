---
description: planテンプレートに従って実装計画（plan.md）と設計アーティファクトを作成します。
handoffs:
  - label: タスク生成
    agent: speckit.tasks
    prompt: planをtasksに分解してください
    send: true
  - label: チェックリスト作成
    agent: speckit.checklist
    prompt: 以下のドメインについてチェックリストを作成してください...
scripts:
  sh: .specify/scripts/bash/setup-plan.sh --json
  ps: .specify/scripts/powershell/setup-plan.ps1 -Json
agent_scripts:
  sh: .specify/scripts/bash/update-agent-context.sh __AGENT__
  ps: .specify/scripts/powershell/update-agent-context.ps1 -AgentType __AGENT__
---

## ユーザー入力

```text
$ARGUMENTS
```

空でない場合、続行する前にユーザー入力を考慮する**必要があります**。

## 実行手順

1. **セットアップ**: `{SCRIPT}` をリポジトリルートから実行し、JSON出力から `FEATURE_SPEC` / `IMPL_PLAN` / `SPECS_DIR` を取得します（パスはすべて絶対パス）。
   - シングルクォートを含む引数は適切にエスケープ

2. **コンテキスト読み込み**:
   - `FEATURE_SPEC` を読み込み
   - 憲章（`/memory/constitution.md`）を読み込み
   - `IMPL_PLAN` テンプレート（既にコピー済み）を読み込み

3. **計画ワークフロー実行**: `IMPL_PLAN` テンプレートの構造に従って:
   - 技術コンテキストを埋める（不明点は「要明確化」マーク）
   - 憲章チェックセクションを埋める
   - ゲート評価（違反が正当化されない場合はエラー）
   - Phase 0: research.md生成（すべての「要明確化」を解決）
   - Phase 1: data-model.md, contracts/, quickstart.md生成
   - Phase 1: エージェントコンテキスト更新（`{AGENT_SCRIPT}` 実行）
   - 設計後に憲章チェック再評価

4. **完了報告**: Phase 2計画後に停止。報告内容:
   - `IMPL_PLAN` のパス
   - 生成したアーティファクト一覧
   - 次ステップ `/speckit.tasks`

## フェーズ詳細

### Phase 0: 概要とリサーチ

1. **技術コンテキストから不明点を抽出**:
   - 各「要明確化」→ リサーチタスク
   - 各依存関係 → ベストプラクティスタスク
   - 各インテグレーション → パターンタスク

2. **リサーチエージェントを生成・実行**:
   ```text
   不明点ごとに:
     タスク: "{機能コンテキスト}における{不明点}のリサーチ"
   技術選択ごとに:
     タスク: "{ドメイン}での{技術}のベストプラクティス調査"
   ```

3. **調査結果を`research.md`に統合**:
   - 決定: [選択した内容]
   - 根拠: [選択理由]
   - 検討した代替案: [評価した他の選択肢]

**出力**: すべての「要明確化」が解決された research.md

### Phase 1: 設計とコントラクト

**前提条件**: `research.md` 完了

1. **機能仕様からエンティティを抽出** → `data-model.md`:
   - エンティティ名、フィールド、関係
   - 要件からのバリデーションルール
   - 状態遷移（該当する場合）

2. **機能要件からAPIコントラクトを生成**:
   - 各ユーザーアクション → エンドポイント
   - 標準的なREST/GraphQLパターンを使用
   - OpenAPI/GraphQLスキーマを `/contracts/` に出力

3. **エージェントコンテキスト更新**:
   - `{AGENT_SCRIPT}` を実行
   - 現在の計画から新しい技術のみ追加
   - マーカー間の手動追加を保持

**出力**: data-model.md, /contracts/*, quickstart.md, エージェント固有ファイル

## 重要なルール

- 絶対パスを使用
- ゲート失敗または未解決の明確化でエラー
- 憲章違反は文書化し正当化が必要
