---
description: tasks生成後に、spec/plan/tasks の整合性と品質を横断分析します（読み取り専用・非破壊）。
---

## ユーザー入力

```text
$ARGUMENTS
```

空でない場合、続行する前にユーザー入力を考慮する**必要があります**。

## 目的

実装前に、`spec.md`・`plan.md`・`tasks.md` の間で矛盾/重複/曖昧さ/抜け漏れがないかを検出し、修正方針を提示します。

## 重要な制約

- **厳密に読み取り専用**: ファイルを編集しない。分析レポートのみ出力する。
- **憲章優先**: `docs/constitution.md`（または `.specify/memory/constitution.md`）と矛盾する場合は最優先で指摘する。

## 実行手順

1. リポジトリルートから `.specify/scripts/bash/check-prerequisites.sh --json --require-tasks --include-tasks` を実行し、`FEATURE_DIR` を取得します（絶対パス）。

2. `FEATURE_DIR` から以下を読み込みます:
   - spec.md, plan.md, tasks.md（必須）
   - 憲章（docs/constitution.md / .specify/memory/constitution.md）

3. 検出観点（例）:
   - 要件に対してタスクが割り当てられていない（Coverageギャップ）
   - タスクがどの要件/ストーリーにも紐づいていない（Unmapped tasks）
   - 同義・重複要件（Duplication）
   - 曖昧語（"高速" "安全" など）の定量化不足（Ambiguity）
   - 用語揺れ/データ定義の不一致（Terminology drift）
   - 憲章MUST違反（Constitution conflict）

4. Markdownの分析レポートを出力します（ファイル書き込みなし）:
   - 指摘ID、カテゴリ、深刻度（CRITICAL/HIGH/MEDIUM/LOW）、場所、要約、推奨対応
   - 要件→タスクのカバレッジ要約
   - 次の推奨アクション（CRITICALがあれば実装前に修正推奨）
