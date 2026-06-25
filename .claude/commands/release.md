---
description: develop または develop に fast-forward 可能な worktree からバージョン更新を行い、mainへのRelease PRを作成します（LLMベース）。
tags: [project]
---

# リリースコマンド（LLMベース）

`origin/develop` に対してバージョン更新・CHANGELOG 更新の commit を land させ、main への Release PR を作成します。develop ブランチからも、develop に fast-forward 可能な gwt feature worktree からも実行可能です。

## フロー概要

```
develop (バージョン更新・CHANGELOG更新) → main (PR)
                                          ↓
                                CI: タグ自動作成 → GitHub Release & バイナリビルド (自動)
```

## 前提条件

- 現在の HEAD が `origin/develop` の ancestor であること (= fast-forward 可能、または既に origin/develop と一致)。gwt の feature worktree からでも実行可能
- working tree が clean、または release に関係しないファイル編集を stash / commit / discard で除去済みであること
- `git-cliff` がインストールされていること（`cargo install git-cliff`）
- `gh` CLI が認証済み（`gh auth login`）
- 前回リリースタグ以降にコミットがあること

## 処理フロー

以下の手順を **順番に** 実行してください。エラーが発生した場合は即座に中断し、エラーメッセージを日本語で表示してください。

### 1. fast-forward 可能性確認

まず origin/develop を fetch し、現在の HEAD が ancestor かどうか確認：

```bash
git fetch origin develop
git merge-base --is-ancestor HEAD origin/develop
```

**判定**:
- ancestor check 成功 (exit 0): 続行（HEAD は origin/develop の祖先または同一）
- 失敗 (exit 1): 以下のメッセージを表示して中断
  > 「エラー: 現在の HEAD は origin/develop の ancestor ではありません。release は origin/develop に新 commit を land させる必要があるため、まず現 worktree の作業を develop に merge してから再実行してください。現在のブランチ: {ブランチ名}」

加えて、working tree の dirty 検知：

```bash
git status --porcelain
```

release に無関係な変更（`Cargo.toml` / `Cargo.lock` / `package.json` / `UnityCliBridge/Packages/unity-cli-bridge/package.json` / `CHANGELOG.md` 以外）が含まれていれば中断：
> 「エラー: working tree に release と無関係な未 commit の変更があります。stash / commit / discard で除去してから再実行してください。」

### 2. リモート同期と HEAD 前進

```bash
git fetch origin main develop
git pull --ff-only origin develop
```

`--ff-only` により、fast-forward できない場合は失敗して止まる（Step 1 の ancestor check で事前に確認済みのため通常は成功）。

これは **branch 切替ではなく、現在の branch の HEAD を origin/develop tip まで前進させる操作** であり、AGENTS.md「branch / worktree を手動で作成・切替・削除しない」に抵触しない。HEAD が既に origin/develop と一致している場合は no-op で成功する。

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

### 7. push

```bash
git push origin HEAD:develop
```

`HEAD:develop` の refspec push により、現 branch の名前に依存せず remote develop に release commit を land させる。develop ブランチ上で実行した場合も従来通り動作する（HEAD == develop の場合 `git push origin develop` と同義）。

**失敗時**: 最大3回リトライ。それでも失敗した場合：
> 「エラー: push に失敗しました。他 agent / human が同時に develop を更新していないか、ネットワーク接続を確認してください。」

### 8. Closing Issue の収集

`develop` 向けPRに書かれた `Closes #...` は自動クローズされないため、release PR（`develop -> main`）本文に再掲します。
gwt-spec Issue も release PR の Closing Issues に含めることで、main マージ時の GitHub 自動 close に任せます。

まず、今回のリリース範囲を決定：

```bash
if [ -n "$PREV_TAG" ]; then
  RANGE="${PREV_TAG}..HEAD"
else
  RANGE="HEAD"
fi
```

次に、tracked helper を使って release PR 用の closing issue 行を生成：

```bash
CLOSING_ISSUE_LINES="$(bash scripts/release/collect-closing-issues.sh --range "$RANGE")"
```

この helper のルール:

- 各 develop 向け PR の `## Closing Issues` セクションを最優先で採用
- `## Closing Issues` が無い古い PR のみ、PR 本文全体の closing keyword (`Closes #...`, `Fixes #...`, `Resolves #...`) にフォールバック
- リリース範囲のコミット本文にある closing keyword も加味
- `## Related Issues / Links` にある通常 Issue の bare `#123` は **参照専用** として扱い、auto-close 候補に昇格させない
- gwt-spec Issue も release PR の Closing Issues に含める
- Related Issues / Links にある gwt-spec も Closing Issues に昇格する
- PR 本文または commit 本文で参照された `gwt-spec` ラベル付き Issue / `gwt-spec:` タイトル Issue は、closing keyword がなくても `Closes #...` に昇格する

`CLOSING_ISSUE_LINES` が `None` でなければ、PR本文の `## Closing Issues` セクションにそのまま挿入：

```text
Closes #123
Closes #456
```

`CLOSING_ISSUE_LINES` が `None` の場合は、`## Closing Issues` に `None` と記載します。

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
- `## Closing Issues` - main マージ時にクローズしたい Issue を `Closes #<番号>` の生テキストで列挙（gwt-spec を含む。`CLOSING_ISSUE_LINES` が `None` の場合は `None` と記載）
- `## Related Issues / Links` - SPEC Issue や参照専用 Issue を列挙（ここに書かれた bare `#<番号>` は auto-close しない）

**重要**: `Closes #<番号>` はコードブロックに入れず、通常の本文として記載すること。
**重要**: auto-close したい gwt-spec Issue は `## Closing Issues` に入れること。`## Related Issues / Links` にだけ書いた Issue は参照専用であり、自動 close されません。

### 10. 完了メッセージ

> 「リリース準備が完了しました。」
> 「バージョン: v{NEW_VERSION}」
> 「PR URL: {PR URL}」
> 「PRがマージされると、CIが自動でタグ作成・ビルド・リリースを実行します。」
> 「必要に応じて `cargo publish` を実行してください。」

## マージ後の自動処理

PR が main にマージされると、`.github/workflows/release.yml`（main push トリガー）が以下を自動実行：

1. `chore(release):` コミットメッセージを検出
2. Cargo.toml からバージョンを読み取り、タグを自動作成
3. クロスプラットフォームビルド（Linux x64, macOS ARM64, Windows x64）
4. GitHub Release を作成し、ビルド済みバイナリをアップロード

**手動後処理**: 必要に応じて `cargo publish` を実行して crates.io に公開してください。

## worktree からの実行について

gwt で feature worktree（例: `work/YYYYMMDD-HHMM`）上にあるときも本コマンドは実行可能。具体的には:

- Step 1 の `git merge-base --is-ancestor HEAD origin/develop` で、現 HEAD が origin/develop の ancestor であることを確認
- Step 2 の `git pull --ff-only origin develop` で現 branch の HEAD を origin/develop tip まで前進させる
- Step 7 の `git push origin HEAD:develop` の refspec push で remote develop に release commit を land させる
- 結果、現 branch の名前は何であっても、操作は常に origin/develop を対象にする

注意: 現 HEAD が origin/develop の ancestor でない場合（例: feature worktree が develop と独立した編集を持っている場合）は Step 1 で中断する。release 前に該当作業を develop に merge し終える必要がある。

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
