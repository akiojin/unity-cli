# データモデル: 3層リリースフロー

**機能ID**: `SPEC-1ac96646` | **日付**: 2025-11-07 | **仕様**: [spec.md](./spec.md)

## 概要

本機能はインフラストラクチャ変更（CI/CDワークフロー、ブランチ戦略）のため、従来のエンティティ・リレーションシップモデルではなく、**ワークフロー状態**と**Git操作のデータフロー**をモデル化します。

## ブランチモデル

### ブランチ構造

```
main (安定版リリース専用)
  ↑ (手動マージ、PR必須)
develop (統合ブランチ、デフォルトブランチ)
  ↑ (自動マージ、CI/CD成功時)
feature/SPEC-xxxxxxxx (機能開発ブランチ)
```

### ブランチ状態遷移

```
[feature作成] → [開発] → [finish-feature.sh実行]
                             ↓
                    [PR作成: feature→develop]
                             ↓
                    [CI/CDチェック実行]
                        ↙         ↘
                [成功]              [失敗]
                  ↓                   ↓
            [自動マージ]          [コメント通知]
                  ↓
          [developに統合]
                  ↓
          [変更蓄積 (複数feature)]
                  ↓
          [/release実行]
                  ↓
        [semantic-release dry-run]
                  ↓
        [リリースノートプレビュー]
                  ↓
        [PR作成: develop→main]
                  ↓
          [手動レビュー・承認]
                  ↓
          [mainにマージ]
                  ↓
        [semantic-release実行]
                  ↓
        [GitHub Release作成]
            ↙         ↘
    [npm publish]  [LSPビルド]
```

## ワークフロー状態モデル

### PR状態 (Pull Request State)

```yaml
PullRequest:
  id: string                    # PR番号 (例: "65")
  title: string                 # PRタイトル
  source_branch: string         # feature/SPEC-xxxxxxxx
  target_branch: string         # develop | main
  status: enum                  # open | merged | closed
  auto_merge_enabled: boolean   # developへのPRのみtrue
  ci_status: enum               # pending | success | failure
  created_at: datetime
  merged_at: datetime | null
```

**状態遷移**:

```
[open] → [CI pending]
         ↓
      [CI success] → (developベース) → [auto-merge] → [merged]
         |           (mainベース)   → [manual review] → [merged]
         ↓
      [CI failure] → [comment added] → [open (待機)]
```

### リリース状態 (Release State)

```yaml
Release:
  version: string               # semantic-releaseが決定 (例: "2.32.0")
  tag: string                   # vX.Y.Z (例: "v2.32.0")
  branch: string                # main
  changelog: string             # CHANGELOG.mdの内容
  artifacts:
    mcp_server:
      npm_published: boolean
      npm_url: string           # https://www.npmjs.com/package/...
    lsp_server:
      platforms:
        - rid: string           # linux-x64, osx-x64, ...
          binary_path: string   # GitHub Release添付URL
          build_status: enum    # success | failure
    unity_package:
      version_synced: boolean
      package_json_path: string
  created_at: datetime
  status: enum                  # draft | published | failed
```

**状態遷移**:

```
[/release実行] → [dry-run]
                   ↓
              [version算出]
                   ↓
              [プレビュー表示]
                   ↓
              [PR作成: develop→main]
                   ↓
              [手動マージ]
                   ↓
              [semantic-release実行]
                   ↓
              [GitHub Release draft作成]
                   ↓
          [npm publish] + [LSPビルド (並列)]
                   ↓
              [Release published]
```

### ワークフロージョブ状態 (Workflow Job State)

```yaml
WorkflowJob:
  workflow_name: string         # auto-merge.yml | release.yml | publish.yml
  run_id: string                # GitHub Actions Run ID
  trigger: enum                 # push | pull_request | release
  branch: string                # develop | main
  status: enum                  # queued | in_progress | success | failure
  steps:
    - name: string
      status: enum              # success | failure | skipped
      duration_seconds: number
  started_at: datetime
  completed_at: datetime | null
  logs_url: string              # GitHub Actions ログURL
```

## コマンド入出力モデル

### /release コマンド

**入力**: なし（現在のdevelopブランチ状態を使用）

**処理フロー**:

```bash
1. git fetch --tags origin
2. semantic-release --dry-run --no-ci
   ↓
3. バージョン番号抽出 (例: 2.33.0)
4. CHANGELOG生成プレビュー
5. 成果物リスト生成
   ↓
6. ユーザーにプレビュー表示
   ↓
7. release/vX.Y.Z ブランチ作成
8. gh pr create --base main --head release/vX.Y.Z
```

**出力**:

```yaml
ReleasePreview:
  next_version: string          # "2.33.0"
  current_version: string       # "2.32.0"
  version_bump: enum            # major | minor | patch
  changelog_preview: string     # マークダウン形式
  artifacts:
    - unity-cli (npm)
    - lsp-server (6 platforms)
    - Unity Package
    - CHANGELOG.md
  pr_url: string | null         # PRが作成された場合のURL
  skipped: boolean              # 変更なしの場合true
  skip_reason: string | null    # "No changes since last release"
```

### finish-feature.sh コマンド

**入力**:

```bash
--base <branch>     # develop (デフォルト)
--draft             # ドラフトPRとして作成
```

**処理フロー**:

```bash
1. 現在のブランチ取得 (feature/SPEC-xxxxxxxx)
2. git push origin <current_branch>
3. gh pr create --base develop --fill
   ↓
4. auto-merge有効化 (--draftでない場合)
```

**出力**:

```yaml
FeatureFinish:
  branch: string                # feature/SPEC-xxxxxxxx
  pr_number: string             # "66"
  pr_url: string                # https://github.com/.../pull/66
  auto_merge_enabled: boolean   # true | false
  base_branch: string           # develop
```

## GitHub Actions イベントモデル

### auto-merge.yml トリガー

```yaml
Trigger:
  event: pull_request
  action: [opened, synchronize, reopened]
  branches: [develop]           # mainは除外
  conditions:
    - statusCheckRollup == "success"
```

### release.yml トリガー

```yaml
Trigger:
  event: push
  branches: [main, "release/**"]
  conditions:
    - semantic-release dry-runでバージョン決定可能
```

### publish.yml トリガー

```yaml
Trigger:
  event: release
  types: [published]
  conditions:
    - tagがv*形式
```

## データフロー図

### 完全リリースサイクル

```
[開発者] → [feature開発]
             ↓
         [finish-feature.sh]
             ↓
         [GitHub: PR作成]
             ↓
         [GitHub Actions: CI/CD]
             ↓
         [auto-merge.yml: マージ]
             ↓
         [develop: 変更蓄積]
             ↓
[リリース担当] → [/release実行]
             ↓
         [semantic-release: dry-run]
             ↓
         [プレビュー表示]
             ↓
         [GitHub: PR作成 (develop→main)]
             ↓
         [手動レビュー]
             ↓
         [mainマージ]
             ↓
         [release.yml: semantic-release実行]
             ↓
         [package.json更新]
         [CHANGELOG.md生成]
         [タグ作成]
             ↓
         [GitHub Release作成]
             ↓
         [publish.yml: 並列実行]
            ↙              ↘
    [npm publish]    [LSPビルド (6 platforms)]
         ↓                  ↓
    [npmjs.com]      [GitHub Release添付]
```

## エラー状態モデル

### PR自動マージ失敗

```yaml
AutoMergeFailure:
  pr_number: string
  failure_reason: enum
    - ci_check_failed
    - merge_conflict
    - approval_required
    - network_error
  recovery_action: enum
    - notify_comment          # PRにコメント追加
    - disable_auto_merge      # 自動マージ無効化
    - manual_intervention     # 手動介入必要
  notified_at: datetime
```

### semantic-release失敗

```yaml
SemanticReleaseFailure:
  run_id: string
  failure_stage: enum
    - version_calculation     # バージョン決定失敗
    - changelog_generation    # CHANGELOG生成失敗
    - git_push                # タグpush失敗
    - npm_publish             # npm publish失敗
  error_message: string
  recovery_action: enum
    - retry                   # リトライ
    - manual_fix              # 手動修正必要
  logs_url: string
```

### LSPビルド部分失敗

```yaml
LspBuildPartialFailure:
  platforms_success: [string]   # ["linux-x64", "osx-x64"]
  platforms_failure: [string]   # ["win-x64"]
  failure_details:
    - platform: string
      error_message: string
      build_log_url: string
  recovery_action: enum
    - attach_partial          # 成功分のみ添付
    - rebuild_failed          # 失敗分のみリビルド
```

## 制約とバリデーション

### ブランチ命名規則

```yaml
BranchNaming:
  feature: "feature/SPEC-[a-f0-9]{8}"
  release: "release/v[0-9]+\\.[0-9]+\\.[0-9]+"
  main: "main"
  develop: "develop"
```

### バージョン形式

```yaml
VersionFormat:
  pattern: "^[0-9]+\\.[0-9]+\\.[0-9]+$"
  example: "2.32.0"
  tag_pattern: "^v[0-9]+\\.[0-9]+\\.[0-9]+$"
  tag_example: "v2.32.0"
```

### Conventional Commits

```yaml
CommitMessage:
  pattern: "^(feat|fix|chore|docs|test|refactor)(\\([a-z]+\\))?: .+$"
  version_bump:
    "feat:": minor
    "fix:": patch
    "feat!:": major
    "BREAKING CHANGE:": major
```

## 永続化

### Git リポジトリ (ソース・オブ・トゥルース)

- **ブランチ**: develop, main, feature/*, release/*
- **タグ**: v*
- **コミット履歴**: Conventional Commits準拠

### GitHub (メタデータ)

- **Pull Requests**: PR状態、CI/CD結果、コメント
- **Releases**: バージョン、CHANGELOG、成果物添付
- **Actions**: ワークフロー実行ログ

### npm (配信)

- **パッケージ**: @akiojin/unity-cli
- **バージョン履歴**: npmjs.com

### ローカルファイル (一時)

- **.git/**: ローカルブランチ、リモート追跡
- **CHANGELOG.md**: 自動生成、Gitコミット対象
- **package.json**: バージョン番号、semantic-releaseが更新

---

**注**: 本データモデルはステートフルなエンティティではなく、Git/GitHub/CI/CDの状態遷移とデータフローを表現しています。インフラストラクチャ変更の性質上、RDBやORMは使用せず、Gitリポジトリ自体がデータストアとなります。
