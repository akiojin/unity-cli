---
description: developブランチでバージョン更新を行い、mainへのRelease PRを作成します（LLMベース）。
tags: [project]
---

# リリースコマンド（LLMベース）

develop ブランチでバージョン更新・CHANGELOG更新を行い、main への Release PR を作成します。

## フロー概要

```
develop (バージョン更新・CHANGELOG更新・タグ作成) → main (PR)
                                                      ↓
                                            GitHub Release & バイナリビルド (自動)
```

## 前提条件

- `develop` ブランチにチェックアウトしていること
- `git-cliff` がインストールされていること（`cargo install git-cliff`）
- `gh` CLI が認証済み（`gh auth login`）
- 前回リリースタグ以降にコミットがあること

## 処理フロー

以下の手順を **順番に** 実行してください。エラーが発生した場合は即座に中断し、エラーメッセージを日本語で表示してください。

### 1. ブランチ確認

```bash
git rev-parse --abbrev-ref HEAD
```

**判定**: 結果が `develop` でなければ、以下のメッセージを表示して中断：
> 「エラー: developブランチでのみ実行可能です。現在のブランチ: {ブランチ名}」

### 2. リモート同期

```bash
git fetch origin main develop
git pull origin develop
```

### 3. リリース対象コミット確認

```bash
PREV_TAG=$(git tag --list 'v[0-9]*' --sort=-version:refname | head -1)
```

上記で取得したタグから現在までのコミット数を確認:

```bash
# タグが存在する場合
git rev-list {PREV_TAG}..HEAD --count

# タグが存在しない場合（初回リリース）
git rev-list --count HEAD
```

**判定**:
- タグが存在しない場合: 初回リリースとして続行（全コミットがリリース対象）
- タグが存在し、コミット数が 0 の場合、以下のメッセージを表示して中断：
> 「エラー: リリース対象のコミットがありません。」

### 4. バージョン判定

```bash
GITHUB_TOKEN=$(gh auth token) git-cliff --bumped-version
```

**出力例**: `v5.3.0`

このバージョンを `NEW_VERSION` として記録（例: `5.3.0`、`v` は除去）。

**重複チェック**: バージョン判定後、既存タグとの重複を確認：

```bash
git tag --list "v{NEW_VERSION}"
```

**判定**: タグが既に存在する場合、以下のメッセージを表示して中断：
> 「エラー: タグ v{NEW_VERSION} は既に存在します。コミット履歴を確認してください。」

### 5. ファイル更新

以下のファイルを更新してください：

#### 5.1 Cargo.toml（ルート）

`version = "X.Y.Z"` を `version = "{NEW_VERSION}"` に更新

#### 5.2 package.json（ルート）

`"version": "X.Y.Z"` を `"version": "{NEW_VERSION}"` に更新

#### 5.3 UnityCliBridge/Packages/unity-cli-bridge/package.json

`"version": "X.Y.Z"` を `"version": "{NEW_VERSION}"` に更新

#### 5.4 Cargo.lock

```bash
cargo update -w
```

#### 5.5 CHANGELOG.md

前回リリースタグ以降の変更のみを追加してください。git-cliffが過去の変更を含める場合は、手動でv{PREV_TAG}以降の変更のみを追加してください。

```bash
GITHUB_TOKEN=$(gh auth token) git-cliff --unreleased --tag v{NEW_VERSION} --prepend CHANGELOG.md
```

**注意**: CHANGELOG.md が存在しない場合（初回リリース）は `--prepend` ではなく `--output` を使用：

```bash
GITHUB_TOKEN=$(gh auth token) git-cliff --tag v{NEW_VERSION} --output CHANGELOG.md
```

CHANGELOGに既に含まれている変更が重複しないよう確認してください。

### 6. リリースコミット作成

```bash
git add Cargo.toml Cargo.lock package.json UnityCliBridge/Packages/unity-cli-bridge/package.json CHANGELOG.md
git commit -m "chore(release): v{NEW_VERSION}"
```

### 7. タグ作成 + push

unity-cli の CI（`unity-cli-release.yml`）はタグ push トリガーのため、ここでタグを作成して push します。

```bash
git tag -a "v{NEW_VERSION}" -m "v{NEW_VERSION}"
git push origin develop --tags
```

**失敗時**: 最大3回リトライ。それでも失敗した場合：
> 「エラー: pushに失敗しました。ネットワーク接続を確認してください。」

### 8. Closing Issue の収集

`develop` 向けPRに書かれた `Closes #...` は自動クローズされないため、release PR（`develop -> main`）本文に再掲します。

まず、今回のリリース範囲を決定：

```bash
if [ -n "$PREV_TAG" ]; then
  RANGE="${PREV_TAG}..HEAD"
else
  RANGE="HEAD"
fi
```

次に、リリース範囲内のマージ済みPR本文とコミット本文から、closing keyword を使っている Issue 番号を抽出：

```bash
MERGED_PRS=$(git log --merges --pretty=%s "$RANGE" \
  | sed -n 's/^Merge pull request #\([0-9]\+\).*$/\1/p' \
  | sort -u)

ISSUE_NUMBERS=""
for PR_NUMBER in $MERGED_PRS; do
  PR_BODY=$(gh pr view "$PR_NUMBER" --json body --jq '.body' 2>/dev/null || true)
  if [ -n "$PR_BODY" ]; then
    FOUND=$(printf '%s\n' "$PR_BODY" \
      | grep -Ei '(close[sd]?|fix(e[sd])?|resolve[sd]?)' \
      | grep -Eo '#[0-9]+' \
      | tr -d '#' || true)
    if [ -n "$FOUND" ]; then
      ISSUE_NUMBERS="${ISSUE_NUMBERS}\n${FOUND}"
    fi
  fi
done

COMMIT_FOUND=$(git log --format=%B "$RANGE" \
  | grep -Ei '(close[sd]?|fix(e[sd])?|resolve[sd]?)' \
  | grep -Eo '#[0-9]+' \
  | tr -d '#' || true)

ISSUE_NUMBERS=$(printf '%b\n%s\n' "$ISSUE_NUMBERS" "$COMMIT_FOUND" \
  | awk 'NF' \
  | sort -nu)
```

`ISSUE_NUMBERS` が空でなければ、PR本文の `## Closing Issues` セクションに **1行ずつ** 以下を追加：

```text
Closes #123
Closes #456
```

### 9. PR作成/更新

まず既存PRを確認：

```bash
gh pr list --base main --head develop --state open --json number,title
```

#### 既存PRがある場合

以下を実行して、タイトル・ラベル・本文を更新（`## Closing Issues` を反映）：

```bash
gh pr edit {PR番号} \
  --title "chore(release): v{NEW_VERSION}" \
  --add-label release \
  --body "{PR_BODY}"
```

> 「既存のRelease PR（#{PR番号}）を更新しました。」
> 「URL: {PR URL}」

#### 既存PRがない場合

PRを作成：

```bash
gh pr create \
  --base main \
  --head develop \
  --title "chore(release): v{NEW_VERSION}" \
  --label release \
  --body "{PR_BODY}"
```

**PR_BODY の内容**（LLMが生成）：

PR bodyには以下を含めてください：
- `## Summary` - このリリースの概要（変更内容を要約）
- `## Changes` - 主な変更点をリスト形式で
- `## Version` - バージョン番号
- `## Closing Issues` - main マージ時にクローズしたい Issue を `Closes #<番号>` の生テキストで列挙（`ISSUE_NUMBERS` が空の場合は `None` と記載）

**重要**: `Closes #<番号>` はコードブロックに入れず、通常の本文として記載すること。

### 10. 完了メッセージ

> 「リリース準備が完了しました。」
> 「バージョン: v{NEW_VERSION}」
> 「PR URL: {PR URL}」
> 「タグ v{NEW_VERSION} をpush済みです。CIが自動でビルド・リリースを実行します。」
> 「PRがマージされた後、必要に応じて `cargo publish` を実行してください。」

## マージ後の自動処理

タグ push により、`.github/workflows/unity-cli-release.yml` が以下を自動実行：

1. テスト実行（Rust + LSP）
2. クロスプラットフォームビルド（Linux x64, macOS ARM64, Windows x64）
3. GitHub Release を作成
4. ビルド済みバイナリをアップロード

**手動後処理**: 必要に応じて `cargo publish` を実行して crates.io に公開してください。

## トラブルシューティング

### git-cliff がインストールされていない場合

```bash
cargo install git-cliff
```

### 認証エラーが発生した場合

```bash
gh auth login
```

### push が拒否された場合

ブランチ保護ルールを確認するか、管理者に連絡してください。
