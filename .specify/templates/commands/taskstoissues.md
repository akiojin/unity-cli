---
description: tasks.md の各タスクを GitHub Issue に変換します（リモートURLがGitHubのときのみ）。
tools: ['github/github-unity-cli/issue_write']
scripts:
  sh: .specify/scripts/bash/check-prerequisites.sh --json --require-tasks --include-tasks
  ps: .specify/scripts/powershell/check-prerequisites.ps1 -Json -RequireTasks -IncludeTasks
---

## ユーザー入力

```text
$ARGUMENTS
```

空でない場合、続行する前にユーザー入力を考慮する**必要があります**。

## 実行手順

1. `{SCRIPT}` を実行し、`FEATURE_DIR` を取得します（パスはすべて絶対パス）。
2. `FEATURE_DIR/tasks.md` を読み込み、チェックボックス形式のタスクを抽出します。
3. GitリモートURLを取得します:

   ```bash
   git config --get remote.origin.url
   ```

   > 注意: リモートが GitHub のURLでない場合は**中止**してください。

4. タスクごとにIssueを作成します（作成先はリモートURLのリポジトリと一致すること）:
   - タイトル: `[FEATURE_ID] T### <短い要約>`
   - 本文: tasks.md の該当タスク行、関連ファイルパス、依存関係

> 警告: リモートURLと一致しないリポジトリにIssueを作成してはいけません。
