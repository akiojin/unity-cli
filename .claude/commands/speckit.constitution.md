---
description: プロジェクト憲章（docs/constitution.md）を更新し、.specify/memory/constitution.md を同期します。
---

## ユーザー入力

```text
$ARGUMENTS
```

空でない場合、続行する前にユーザー入力を考慮する**必要があります**。

## 概要

このプロジェクトでは、憲章の正本は `docs/constitution.md` です。Speckitコマンドが参照するミラーとして `.specify/memory/constitution.md` を同期します。

## 実行手順

1. `docs/constitution.md` を読み込み、ユーザー入力に基づいて必要な変更を反映します。

2. 変更内容が妥当かを確認します:
   - MUST/禁止事項が具体的で、運用上テスト可能になっている
   - 曖昧語（"適切に" など）が残らない
   - 既存原則との矛盾がない

3. `docs/constitution_update_checklist.md` がある場合は読み、チェック観点に沿って見直します。

4. `docs/constitution.md` を更新したら、同期を実行します:
   - `bash scripts/sync-constitution.sh`
   - 期待: `.specify/memory/constitution.md` が `docs/constitution.md` と一致する

5. 最後にユーザーへ以下を報告します:
   - 変更の要点（追加/修正/削除）
   - 同期結果（成功/注意点）
   - もしテンプレートや運用ルール（`.specify/templates/*` や `.claude/commands/*`）へ波及が必要なら、その提案
