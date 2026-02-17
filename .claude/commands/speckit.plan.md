---
description: planテンプレートに従って実装計画（plan.md）と設計アーティファクトを作成します。
---

## ユーザー入力

```text
$ARGUMENTS
```

空でない場合、続行する前にユーザー入力を考慮する**必要があります**。

## 概要

これはSpec駆動開発ライフサイクルの2番目のステップです（仕様 → 計画）。

## 実行手順

1. リポジトリルートから `.specify/scripts/bash/setup-plan.sh --json` を実行し、JSON出力から以下を取得します（パスはすべて絶対パス）:
   - `FEATURE_ID`
   - `FEATURE_DIR`
   - `FEATURE_SPEC`
   - `IMPL_PLAN`

2. コンテキストを読み込みます:
   - `FEATURE_SPEC`（spec.md）
   - `docs/constitution.md`（憲章。存在しない場合は `.specify/memory/constitution.md` を参照）
   - `.specify/templates/plan-template.md`（構造と必須セクションの理解。すでに `IMPL_PLAN` にコピー済み）

3. `IMPL_PLAN`（plan.md）をテンプレート構造に沿って埋めます:
   - 技術コンテキストを具体化（不明点は `要明確化` として明示）
   - 憲章チェックを、憲章のMUST/禁止事項に従って記述
   - プロジェクト構造（ドキュメント/ソース）を実パスで確定
   - ユーザー入力 `$ARGUMENTS` の技術選択・制約を反映（矛盾があれば指摘し、合理案を提示）

4. 設計アーティファクトを作成します（必要に応じて）:
   - `FEATURE_DIR/research.md`: 技術選定の根拠、代替案、決定事項（要明確化の解消）
   - `FEATURE_DIR/data-model.md`: 主要エンティティと関係（要件/ストーリーに対応）
   - `FEATURE_DIR/contracts/`: API/入出力契約（該当する場合）
   - `FEATURE_DIR/quickstart.md`: 主要ユーザーストーリーを検証する最短の実行/確認手順

5. 最後に、ユーザーへ以下を返します:
   - `FEATURE_ID`
   - `IMPL_PLAN` のパス
   - 生成したアーティファクト（存在するもの）の一覧
   - 次ステップ（`/speckit.tasks`）への案内

## 重要

- すべてのファイル操作は**絶対パス**で行い、パス解決の誤りを避けてください。
