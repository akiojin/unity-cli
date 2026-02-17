# Phase 0: リサーチ＆技術調査

**SPEC ID**: SPEC-1ac96646
**日付**: 2025-11-07
**ステータス**: 完了

## 概要

本ドキュメントは、3層リリースフロー（feature → develop → main）実装に必要な技術調査結果をまとめたものです。

---

## 1. GitHub Actions ブランチフィルタリング

### 調査内容
`branches:` フィルタで複数ブランチを条件分岐する方法を調査。

### 調査結果

**現状（auto-merge.yml）**:
```yaml
on:
  pull_request:
    types: [opened, synchronize, reopened, ready_for_review]
    branches:
      - main
      - develop
```

**分析**:
- `branches:` は**PRのベースブランチ（マージ先）**を指定
- 現在はmainとdevelopの両方でトリガー
- ジョブ内で条件分岐する場合は`if:`を使用可能

### 決定事項

**オプションA: ワークフロー分離（推奨）**
- developブランチのみ自動マージ
- mainブランチは除外

```yaml
on:
  pull_request:
    branches:
      - develop  # mainを削除
```

**理由**:
- シンプル（条件分岐不要）
- 意図が明確（developのみ自動マージ）
- mainへのPRは手動承認を強制

**オプションB: 条件分岐**
```yaml
branches:
  - main
  - develop

jobs:
  auto-merge:
    if: github.event.pull_request.base.ref == 'develop'
```

**却下理由**:
- 複雑化（条件ロジック追加）
- mainブランチへのPRで無駄にワークフロー起動

---

## 2. semantic-release ブランチ設定

### 調査内容
`.releaserc.json`の`branches`設定とmainブランチ専用化を確認。

### 調査結果

**現状（.releaserc.json）**:
```json
{
  "branches": ["main"],
  "tagFormat": "v${version}",
  "plugins": [...]
}
```

**分析**:
- `branches: ["main"]`で既にmainブランチ専用
- developブランチでsemantic-releaseは実行されない
- release.ymlは`on.push.branches: [main]`でトリガー

### 決定事項

**変更不要**:
- `.releaserc.json`は現状維持
- `branches: ["main"]`のまま
- release.ymlも`branches: [main]`を維持

**理由**:
- 既に要件を満たしている
- mainへのpushでのみsemantic-release実行
- developブランチは除外済み

---

## 3. gh CLI PR操作

### 調査内容
`gh pr create`の`--base`オプション、PRのベースブランチ変更方法を調査。

### 調査結果

**PR作成（finish-feature.sh）**:
```bash
gh pr create --base main --head "$CURRENT_BRANCH" --title "$PR_TITLE" --body "$PR_BODY"
```

**ベースブランチ変更（PR移行）**:
```bash
gh pr edit <PR番号> --base develop
```

**PRリスト取得**:
```bash
# mainベースのPR一覧
gh pr list --base main --json number,title,headRefName

# すべてのPR一覧
gh pr list --state open --json number,title,baseRefName,headRefName
```

### 決定事項

**finish-feature.sh修正**:
```bash
# 変更前
gh pr create --base main ...

# 変更後
gh pr create --base develop ...
```

**PR移行スクリプト（migrate-pr-to-develop.sh）**:
```bash
#!/bin/bash
# 既存16個のPRをdevelopベースに一括変更

set -euo pipefail

# mainベースのPR取得
pr_list=$(gh pr list --base main --json number,title,headRefName)
pr_count=$(echo "$pr_list" | jq '. | length')

echo "Found $pr_count PRs with base=main"

# 各PRをdevelopベースに変更
echo "$pr_list" | jq -r '.[] | @json' | while IFS= read -r pr_json; do
  pr_number=$(echo "$pr_json" | jq -r '.number')
  pr_title=$(echo "$pr_json" | jq -r '.title')

  echo "Migrating PR #$pr_number: $pr_title"
  gh pr edit "$pr_number" --base develop
done

echo "Migration complete!"
```

**代替案（検討したが却下）**:
- **手動変更**: エラーの可能性、時間がかかる
- **GitHub API直接呼び出し**: gh CLIで十分、複雑化

---

## 4. Claudeスラッシュコマンド実装

### 調査内容
`.claude/commands/*.md`のフォーマット、semantic-release dry-run実行方法を調査。

### 調査結果

**スラッシュコマンドフォーマット**:
```markdown
---
description: コマンドの説明（1行）
---

## ユーザー入力

```text
$ARGUMENTS
```

## 説明

コマンドの目的と実行内容

## 実行手順

1. ステップ1
2. ステップ2
...
```

**semantic-release dry-run**:
```bash
# dry-runでリリースノートプレビュー
npx semantic-release --dry-run --no-ci

# 出力例:
# [semantic-release] › ℹ  The next release version is 2.17.0
# [semantic-release] › ℹ  Release note for version 2.17.0:
# ## [2.17.0] (2025-11-07)
# ### Features
# * Add video capture support
```

### 決定事項

**/releaseコマンド仕様**:

**ファイル**: `.claude/commands/release.md`

**処理フロー**:
1. developとmainの差分確認（`git log main..develop --oneline`）
2. 差分がなければスキップ、メッセージ表示
3. semantic-release dry-run実行、リリースノートプレビュー取得
4. ユーザーに確認プロンプト表示
5. 確認後、develop → main PR作成（`gh pr create --base main --head develop`）
6. PRボディにリリースノート、成果物リスト、マージ後の自動フロー説明

**代替案（検討したが却下）**:
- **PRの自動マージ**: 手動承認を残すべき（リリースは慎重に）
- **mainへの直接push**: PRレビューフローを尊重すべき

---

## 5. 既存PR一括操作

### 調査内容
`gh pr edit`でベースブランチ変更、複数PR処理のベストプラクティスを調査。

### 調査結果

**PR情報取得**:
```bash
# JSON形式で取得（処理しやすい）
gh pr list --base main --json number,title,headRefName,url

# 出力例:
# [
#   {"number": 15, "title": "Add feature X", "headRefName": "feature/SPEC-12345678", "url": "..."},
#   {"number": 16, "title": "Fix bug Y", "headRefName": "feature/SPEC-87654321", "url": "..."}
# ]
```

**ベースブランチ変更**:
```bash
# 単一PR
gh pr edit 15 --base develop

# エラーハンドリング
if gh pr edit "$pr_number" --base develop; then
  echo "✅ PR #$pr_number migrated successfully"
else
  echo "❌ Failed to migrate PR #$pr_number"
fi
```

**べき等性保証**:
```bash
# 既にdevelopベースの場合はスキップ
current_base=$(gh pr view "$pr_number" --json baseRefName --jq '.baseRefName')
if [ "$current_base" = "develop" ]; then
  echo "⏭️ PR #$pr_number already targets develop, skipping"
  continue
fi
```

### 決定事項

**migrate-pr-to-develop.sh 仕様**:

**機能**:
- mainベースのPRを一括取得
- 各PRのベースブランチをdevelopに変更
- べき等性保証（既にdevelopベースならスキップ）
- エラーハンドリング（失敗時もログ記録、続行）
- dry-runモード（`--dry-run`で実行せず表示のみ）

**オプション**:
- `--help`: ヘルプ表示
- `--dry-run`: 変更せずに対象PRリスト表示
- `--verbose`: 詳細ログ出力

**代替案（検討したが却下）**:
- **GitHub Web UIで手動変更**: 16個は多すぎる、エラーの可能性
- **GitHub GraphQL Mutation**: gh CLI RESTで十分、複雑化

---

## まとめ

### 技術スタック（確定）
- **GitHub Actions**: YAML設定、既存ワークフロー修正のみ
- **semantic-release**: 現状維持（.releaserc.json変更不要）
- **gh CLI**: PR操作（create、edit、list、view）
- **Bash**: スクリプト実装（finish-feature.sh修正、migrate-pr-to-develop.sh新規）
- **Markdown**: /releaseコマンド実装

### 変更が必要なファイル
1. `.github/workflows/auto-merge.yml`: `branches: [develop]`のみ
2. `.specify/scripts/bash/finish-feature.sh`: `--base develop`に変更
3. `.specify/scripts/bash/migrate-pr-to-develop.sh`: 新規作成
4. `.claude/commands/release.md`: 新規作成
5. `CLAUDE.md`: ブランチフロー説明更新
6. `README.md`: リリースフロー図追加

### 変更不要なファイル
- `.releaserc.json`: 既にmainブランチ専用
- `.github/workflows/release.yml`: mainへのpushでトリガー済み
- `.github/workflows/unity-cli-publish.yml`: GitHub Release作成時にトリガー（ブランチ非依存）
- `.github/workflows/release-lsp.yml`: タグpush時にトリガー（ブランチ非依存）

**補足説明**:
- **unity-cli-publish.yml**: `on.release.types: [published]`でトリガー。semantic-releaseがmainでGitHub Releaseを作成するため、develop/mainフロー導入後も正常動作。変更不要。
- **release-lsp.yml**: `on.push.tags: ['v*']`でトリガー。semantic-releaseがmainでタグ（v*）を作成するため、develop/mainフロー導入後も正常動作。変更不要。

### リスク＆対策
| リスク | 対策 |
|--------|------|
| 既存PR移行時のエラー | べき等性保証、エラーハンドリング、dry-runモード |
| mainへのPRが誤って自動マージ | auto-merge.ymlでmainを除外、契約テストで検証 |
| semantic-releaseがdevelopで実行 | branches設定で既に防止済み、テストで確認 |
| /releaseコマンド実行ミス | dry-runプレビュー、ユーザー確認プロンプト |

---

## 次のステップ

Phase 1（設計＆契約）に進む準備完了:
- すべての技術的不明点を解決
- 実装方針決定
- 変更対象ファイル特定

✅ **Phase 0完了**
