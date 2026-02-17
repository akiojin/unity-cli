---
description: プロジェクト憲章（docs/constitution.md）を更新し、.specify/memory/constitution.md を同期します。
---

## ユーザー入力

```text
$ARGUMENTS
```

空でない場合、続行する前にユーザー入力を考慮する**必要があります**。

## 実行手順

1. `docs/constitution.md` を読み込み、ユーザー入力に基づいて変更します（正本）。

2. `docs/constitution_update_checklist.md` がある場合は読み、チェック観点に沿って見直します。

3. 同期を実行します:
   - `bash scripts/sync-constitution.sh`
   - 期待: `.specify/memory/constitution.md` が更新される

4. 完了報告:
   - 変更の要点
   - 同期結果
   - 影響範囲（テンプレート/運用への波及が必要なら提案）
