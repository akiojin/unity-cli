# クイックスタートガイド: 3層リリースフロー

**SPEC ID**: SPEC-1ac96646
**日付**: 2025-11-07

## 概要

このガイドでは、3層リリースフロー（feature → develop → main）の初期セットアップと使用方法を説明します。

---

## 初期セットアップ

### 1. developブランチ作成

mainブランチから新しいdevelopブランチを作成します：

```bash
# mainブランチをpullして最新化
git checkout main
git pull origin main

# developブランチを作成してpush
git checkout -b develop
git push -u origin develop
```

### 2. GitHubデフォルトブランチ変更

gh CLIでデフォルトブランチをdevelopに変更：

```bash
# デフォルトブランチをdevelopに設定
gh repo edit --default-branch develop
```

**または GitHub Web UI**:

1. リポジトリページ → **Settings**
2. 左メニュー → **Branches**
3. **Default branch** セクション → スイッチアイコンをクリック
4. `develop` を選択 → **Update**
5. 確認ダイアログで **I understand, update the default branch** をクリック

これにより、新規PRのデフォルトベースがdevelopになります。

### 3. Branch Protection Rules設定（重要）

develop → main の自動マージを有効化するため、mainブランチにRequired status checksを設定：

```bash
# mainブランチのBranch Protection Rules設定
gh api repos/{owner}/{repo}/branches/main/protection \
  -X PUT \
  --input - <<EOF
{
  "required_status_checks": {
    "strict": true,
    "contexts": ["test"]
  },
  "enforce_admins": false,
  "required_pull_request_reviews": null,
  "restrictions": null,
  "allow_force_pushes": false,
  "allow_deletions": false
}
EOF
```

**{owner}と{repo}を実際の値に置き換えてください**（例: `akiojin/unity-cli`）

**設定内容**:
- `required_status_checks.contexts: ["test"]`: testチェックが必須
- `strict: true`: PRブランチがベースブランチの最新であることを要求
- `enforce_admins: false`: 管理者は制限をバイパス可能
- `required_pull_request_reviews: null`: レビュー不要
- `allow_force_pushes: false`: force pushを禁止

**developブランチも同様に設定（オプション）**:

```bash
# developブランチのBranch Protection Rules設定
gh api repos/{owner}/{repo}/branches/develop/protection \
  -X PUT \
  --input - <<EOF
{
  "required_status_checks": {
    "strict": true,
    "contexts": ["test"]
  },
  "enforce_admins": false,
  "required_pull_request_reviews": null,
  "restrictions": null,
  "allow_force_pushes": false,
  "allow_deletions": false
}
EOF
```

### 4. 既存PRの移行（オプション）

既にmainベースのPRが存在する場合、developベースに変更します：

**手動変更（PR数が少ない場合）**:

1. PR詳細ページを開く
2. ベースブランチ横の **Edit** をクリック
3. `develop` を選択
4. PRが更新される

**一括変更（PR数が多い場合）**:

```bash
# migrate-pr-to-develop.shスクリプトを使用（今後実装予定）
.specify/scripts/bash/migrate-pr-to-develop.sh

# または手動で各PRを変更
gh pr list --base main --json number | jq -r '.[].number' | while read pr_number; do
  gh pr edit "$pr_number" --base develop
done
```

---

## 通常の開発フロー

### 1. 新機能開発開始

```bash
# 仕様書作成（自動的にfeatureブランチ＆Worktree作成）
/speckit.specify

# Worktreeに移動
cd .worktrees/SPEC-xxxxxxxx/

# 実装計画作成
/speckit.plan

# タスク分解
/speckit.tasks
```

### 2. 実装とコミット

```bash
# TDDサイクル厳守（RED-GREEN-REFACTOR）
# テスト作成 → 実装 → リファクタリング

# 各変更をコミット
git add .
git commit -m "feat: 機能Xを実装"
```

### 3. PR作成＆自動マージ（developへ）

```bash
# finish-featureスクリプト実行
.specify/scripts/bash/finish-feature.sh

# 自動実行される処理:
# 1. featureブランチをリモートにpush
# 2. GitHub PR作成（ベース: develop）
# 3. GitHub Actions Required Checks監視
# 4. すべてのチェック成功 → 自動的にdevelopへマージ
```

**ドラフトPR（自動マージしたくない場合）**:

```bash
.specify/scripts/bash/finish-feature.sh --draft
```

---

## リリースフロー（develop → main）

### 1. リリース準備

developブランチに複数のfeatureが蓄積された時点でリリースを実行します。

```bash
# /releaseコマンド実行
/release
```

### 2. リリースコマンドの処理フロー

`/release`コマンドは以下を自動実行します：

**Step 1: 差分確認**
```bash
# developとmainの差分を表示
git log origin/main..origin/develop --oneline
```

差分がない場合はスキップメッセージを表示して終了。

**Step 2: リリースノートプレビュー**
```bash
# semantic-release dry-runでリリース内容を取得
cd unity-cli && npx semantic-release --dry-run --no-ci
```

出力例：
```
[semantic-release] › ℹ  The next release version is 2.17.0
[semantic-release] › ℹ  Release note for version 2.17.0:
## [2.17.0] (2025-11-07)

### Features
* Add video capture support
* Implement 3-tier release flow
```

**Step 3: ユーザー確認プロンプト**

```markdown
## 🚀 リリース準備完了

**次のバージョン**: v2.17.0

**リリース内容**:
## [2.17.0] (2025-11-07)

### Features
* Add video capture support
* Implement 3-tier release flow

**リリース成果物**:
- ✅ MCPサーバー（npm） → npmjs.comに自動publish
- ✅ LSPサーバー（全プラットフォーム） → GitHub Releaseに添付
- ✅ Unity Package → バージョン同期
- ✅ CHANGELOG.md → 自動生成

**マージ後の自動フロー**:
1. semantic-release実行 → バージョンアップ＆タグ作成
2. GitHub Release作成
3. MCPサーバー npm publish
4. LSPサーバービルド（全プラットフォーム）

---

このリリースを実行しますか？ (yes/no)
```

**Step 4: develop → main PR作成**

ユーザーが `yes` を入力すると、PRを自動作成：

```bash
gh pr create --base main --head develop \
  --title "release: v2.17.0" \
  --body "## 🚀 リリース: v2.17.0

このPRは、developブランチの変更をmainブランチにリリースします。

---

## 📋 リリース内容

$(git log origin/main..origin/develop --oneline)

---

## 🎯 リリースノート

[semantic-release dry-runの結果を挿入]

---

## 🤖 自動リリースフロー

このPRをマージすると、以下が自動的に実行されます：

1. **semantic-release**: package.json、Unity Package、CHANGELOG.mdを更新、タグ作成
2. **GitHub Release**: リリースノートと共にリリース作成
3. **npm publish**: MCPサーバーをnpmjs.comに公開
4. **LSPサーバービルド**: 全プラットフォーム（linux-x64, osx-x64, osx-arm64, win-x64）でビルド、GitHub Releaseに添付

---

⚠️ **重要**: このPRは自動マージされません。手動でレビュー＆承認が必要です。

📝 **仕様**: specs/SPEC-1ac96646/spec.md を参照してください。"
```

**Step 5: 完了メッセージ**

```markdown
✅ リリースPRが作成されました！

**PR URL**: https://github.com/akiojin/unity-cli/pull/XX

**次のステップ**:
1. PRをレビュー
2. CI/CDチェックの完了を待機
3. PRをマージ → 自動リリース開始

**マージ後の成果物**:
- npmjs.com: @akiojin/unity-cli v2.17.0
- GitHub Release: v2.17.0（LSPバイナリ添付）
- CHANGELOG.md: 自動更新

🎉 リリース準備完了です！
```

### 3. PRマージとリリース実行（自動）

1. PRのCI/CDチェック（Required checks）の完了を待機
2. すべてのRequired checks成功 → **自動的にmainへマージ**
3. マージ後、自動的にsemantic-releaseが実行される
4. GitHub Releaseが作成され、LSPバイナリが添付される
5. MCPサーバーがnpmjs.comに公開される

**注**: 手動マージは不要です。Required checksが通れば自動マージされます。

---

## トラブルシューティング

### developとmainに差分がない

```
ℹ️  developとmainに差分がありません。
リリースする変更がないため、PR作成をスキップします。

developブランチに新しい機能をマージしてから、再度/releaseを実行してください。
```

→ featureブランチを開発してdevelopにマージしてから再実行

### semantic-release dry-run失敗

```
❌ semantic-release dry-runが失敗しました。

原因:
- Conventional Commits規約に準拠していないコミットがある可能性
- package.jsonやnode_modulesに問題がある可能性

対策:
1. developブランチのコミットメッセージを確認
2. unity-cliディレクトリで `npm ci` を実行
3. エラーログを確認
```

→ Conventional Commits形式を確認（`feat:`, `fix:`, `chore:` 等）

### gh CLI認証エラー

```
❌ GitHub CLI認証エラー

以下のコマンドで認証してください:
gh auth login
```

→ `gh auth login` を実行してGitHub CLIを認証

### PRが自動マージされない

**原因と対策**:

1. **Branch Protection Rulesが未設定**
   ```bash
   # mainブランチのProtection Rules確認
   gh api repos/{owner}/{repo}/branches/main/protection

   # 設定されていない場合は初期セットアップの手順3を実行
   ```

2. **Required checksが失敗している**
   ```bash
   # PRのチェック状態確認
   gh pr checks <PR番号>

   # 失敗しているチェックを修正してpush
   ```

3. **PRがdraftモード**
   ```bash
   # draft状態を解除
   gh pr ready <PR番号>
   ```

4. **GitHub Actions権限不足**
   - リポジトリ設定 → Actions → General → Workflow permissions
   - "Read and write permissions"を選択

---

## 参考資料

- **仕様書**: `specs/SPEC-1ac96646/spec.md`
- **実装計画**: `specs/SPEC-1ac96646/plan.md`
- **技術調査**: `specs/SPEC-1ac96646/research.md`
- **/releaseコマンド**: `.claude/commands/release.md`
- **finish-featureスクリプト**: `.specify/scripts/bash/finish-feature.sh`
- **auto-mergeワークフロー**: `.github/workflows/auto-merge.yml`

---

## よくある質問

**Q: なぜ3層フローにしたの？**

A: 以前のfeature → mainフローでは、featureがマージされるたびにリリースが実行され、リリース頻度が高すぎました。3層フローでは、developで複数のfeatureを蓄積してからmainにリリースできます。

**Q: developブランチの役割は？**

A: 統合ブランチとして機能します。複数のfeatureブランチをdevelopにマージして統合テストを実行し、問題がなければmainにリリースします。

**Q: mainブランチの役割は？**

A: リリースブランチとして機能します。mainへのマージでsemantic-releaseが実行され、npm publish、GitHub Release作成、LSPビルドが自動実行されます。

**Q: 既存のfeatureブランチはどうなる？**

A: 既存のfeatureブランチはそのまま使用できます。PRのベースブランチをdevelopに変更するだけです。

**Q: semantic-releaseはいつ実行される？**

A: mainブランチへのpushでのみ実行されます。developへのマージでは実行されません（`.releaserc.json`の`branches: ["main"]`設定により）。

**Q: リリース頻度はどれくらい？**

A: プロジェクトの判断に依存します。developに複数のfeatureが蓄積された時点で `/release` を実行してください。週次、月次、機能区切りなど、チームの方針に従ってください。
