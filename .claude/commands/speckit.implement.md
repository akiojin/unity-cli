---
description: tasks.md に定義されたタスクを順に実行し、実装を進めます（チェックリストが未完了なら確認して停止）。
---

## ユーザー入力

```text
$ARGUMENTS
```

空でない場合、続行する前にユーザー入力を考慮する**必要があります**。

## 実行手順

1. リポジトリルートから `.specify/scripts/bash/check-prerequisites.sh --json --require-tasks --include-tasks` を実行し、JSON出力から `FEATURE_DIR` と `AVAILABLE_DOCS` を取得します（パスは絶対パス）。

2. チェックリスト状態を確認します（`FEATURE_DIR/checklists/` が存在する場合）:
   - `- [ ]` / `- [x]` / `- [X]` の行数で Total/Completed/Incomplete を数える
   - 例のテーブルを作る:

     ```text
     | Checklist | Total | Completed | Incomplete | Status |
     |-----------|-------|-----------|------------|--------|
     | requirements.md | 12 | 10 | 2 | ✗ FAIL |
     ```

   - **Incomplete が1つでもある場合は停止**し、次を確認する:
     - 「チェックリストが未完了です。実装を続行しますか？ (yes/no)」
   - ユーザーが `no/stop/wait` なら中断、`yes/proceed/continue` なら続行

3. 実装コンテキストを読み込みます（必須/任意）:
   - **必須**: `tasks.md`（タスク一覧と順序）
   - **必須**: `plan.md`（技術スタック、構造、制約）
   - **存在すれば**: `data-model.md`, `contracts/`, `research.md`, `quickstart.md`

4. tasks.md を上から順に実行します:
   - 各タスクは「対象ファイル」「期待結果」「完了条件」を明確化してから着手
   - `[P]` タスクは、依存がなければ並列で進められる（ただし競合ファイルは避ける）
   - テストを含む場合は TDD を守る（テスト→失敗確認→最小実装→成功→整理）

5. 実装中の注意:
   - 憲章（`docs/constitution.md`）に反する場合は、実装を止めて計画/タスク側を修正する
   - 不明点が顕在化したら、仕様（spec.md）へ `[要明確化]` を追加して戻す（必要なら `/speckit.clarify` を使う）

6. 最後にユーザーへ、完了したタスクID/残タスク/次の推奨アクションを報告します。
