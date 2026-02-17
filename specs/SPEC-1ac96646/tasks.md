# タスク: 3層リリースフロー（feature → develop → main）

**入力**: `/specs/SPEC-1ac96646/`の設計ドキュメント
**前提条件**: plan.md (必須), research.md, quickstart.md

## 実行フロー (main)
```
1. 機能ディレクトリからplan.mdを読み込み
   → 見つからない場合: ERROR "実装計画が見つかりません"
   → 抽出: 技術スタック、ライブラリ、構造
2. オプション設計ドキュメントを読み込み:
   → research.md: 決定を抽出 → setupタスク
   → quickstart.md: テストシナリオを確認
3. カテゴリ別にタスクを生成:
   → Setup: developブランチ作成、GitHub設定、環境準備
   → Tests: contract tests (5個), integration tests (2個), e2e tests (1個)
   → Core: GitHub Actionsワークフロー修正、Bashスクリプト修正/新規作成
   → Integration: ドキュメント更新、/releaseコマンド実装
   → Polish: 既存PR移行、テスト検証、完了確認
4. タスクルールを適用:
   → 異なるファイル = [P]をマーク (並列実行可能)
   → 同じファイル = 順次実行 ([P]なし)
   → テストが実装より先 (TDD)
5. タスクを順次番号付け (T001, T002...)
6. 依存関係グラフを生成
7. 並列実行例を作成
8. タスク完全性を検証
9. 戻り値: SUCCESS (タスク実行準備完了)
```

## フォーマット: `[ID] [P?] 説明`
- **[P]**: 並列実行可能 (異なるファイル、依存関係なし)
- 説明には正確なファイルパスを含める

## Phase 3.1: セットアップ (Setup)
- [ ] T001 mainブランチからdevelopブランチを作成してpush
- [ ] T002 GitHubデフォルトブランチをdevelopに変更
- [ ] T003 [P] mainブランチにBranch Protection Rules設定
- [ ] T004 [P] developブランチにBranch Protection Rules設定（オプション）
- [ ] T005 [P] gh CLI認証確認（`gh auth status`）

## Phase 3.2: テストファースト (TDD) ⚠️ 3.3の前に完了必須
**重要: これらのテストは記述され、実装前に失敗する必要がある**

### 契約テスト (Contract Tests)
- [ ] T006 [P] specs/SPEC-1ac96646/contracts/auto-merge.contract.test.sh を作成
  - test_develop_pr_auto_merges()
  - test_main_pr_does_not_auto_merge()
  - test_auto_merge_waits_for_checks()
  - test_auto_merge_comments_on_failure()
- [ ] T007 [P] specs/SPEC-1ac96646/contracts/release-command.contract.test.sh を作成
  - test_release_command_exists()
  - test_release_shows_preview()
  - test_release_creates_pr_without_auto_merge()
  - test_release_skips_if_no_changes()
- [ ] T008 [P] specs/SPEC-1ac96646/contracts/semantic-release.contract.test.sh を作成
  - test_semantic_release_runs_on_main_merge()
  - test_package_json_updated()
  - test_unity_package_version_synced()
  - test_changelog_generated()
  - test_tag_created()
- [ ] T009 [P] specs/SPEC-1ac96646/contracts/mcp-publish.contract.test.sh を作成
  - test_mcp_publish_on_release()
  - test_version_matches_tag()
  - test_npmjs_package_available()
- [ ] T010 [P] specs/SPEC-1ac96646/contracts/lsp-build.contract.test.sh を作成
  - test_lsp_manifest_exists()
  - test_all_platforms_built()
  - test_binaries_attached_to_release()

### 統合テスト (Integration Tests)
- [ ] T011 [P] tests/integration/full-release-cycle.test.sh を作成
  - feature → develop PR → 自動マージ
  - /release実行 → develop → main PR
  - PRマージ → semantic-release → MCPサーバー → LSPサーバー → GitHub Release
- [ ] T012 [P] tests/integration/pr-migration.test.sh を作成
  - 既存mainベースPRをdevelopベースに変更
  - CI/CDチェック再実行確認
  - auto-merge動作確認

### E2Eテスト (End-to-End Tests)
- [ ] T013 [P] tests/e2e/new-feature-to-release.test.sh を作成
  - 新規featureブランチ作成 → develop PR → マージ → /release → main PR → GitHub Release

## Phase 3.3: コア実装 (テストが失敗した後のみ)

### GitHub Actionsワークフロー修正
- [ ] T014 [P] .github/workflows/auto-merge.yml を修正
  - developブランチのみを対象にする（branches: [develop]）
  - mainへのPRは自動マージしない
- [ ] T015 [P] .github/workflows/release.yml を確認
  - mainブランチでのsemantic-release実行を確認
  - 変更不要なら確認のみ
- [ ] T016 [P] .github/workflows/unity-cli-publish.yml を確認
  - GitHub Release作成時のnpm publish確認
  - 変更不要なら確認のみ

### Bashスクリプト修正/新規作成
- [ ] T017 .specify/scripts/bash/finish-feature.sh を修正
  - PRベースブランチをdevelopに変更（--base develop）
  - エラーハンドリング追加
- [ ] T018 .specify/scripts/bash/migrate-pr-to-develop.sh を新規作成
  - 既存16個のfeature PRのベースブランチをdevelopに変更
  - --help, --dry-run オプション実装
  - べき等性を保証（移行済みPRスキップ）

### /releaseコマンド実装
- [ ] T019 .claude/commands/release.md を新規作成
  - semantic-release dry-runを実行してバージョン判定
  - リリースノートプレビュー表示（バージョン、CHANGELOG、成果物リスト）
  - ユーザー確認後にdevelop → main PRを作成（auto-merge無効）

## Phase 3.4: 統合 (Integration)

### ドキュメント更新
- [ ] T020 [P] CLAUDE.md を更新
  - Worktree＆ブランチ運用セクション更新（3層フロー反映）
  - リリースフローセクション更新（/releaseコマンド説明追加）
  - 自動リリース（semantic-release）セクション更新
- [ ] T021 [P] README.md を更新
  - リリースフロー図追加（3層ブランチ構造）
  - developブランチ説明追加
  - /releaseコマンド使用例追加
- [ ] T022 [P] specs/SPEC-1ac96646/quickstart.md を確認
  - 既に作成済み、内容確認のみ
  - 不足があれば追記

## Phase 3.5: 仕上げ (Polish)

### 既存PR移行
- [ ] T023 .specify/scripts/bash/migrate-pr-to-develop.sh を実行
  - --dry-run で移行対象PR確認
  - 実行して16個のPRをdevelopベースに変更
  - 移行後のCI/CDチェック成功確認

### テスト検証
- [ ] T024 契約テスト5個を実行して合格確認
  - auto-merge.contract.test.sh
  - release-command.contract.test.sh
  - semantic-release.contract.test.sh
  - mcp-publish.contract.test.sh
  - lsp-build.contract.test.sh
- [ ] T025 統合テスト2個を実行して合格確認
  - full-release-cycle.test.sh
  - pr-migration.test.sh
- [ ] T026 E2Eテスト1個を実行して合格確認
  - new-feature-to-release.test.sh

### 最終確認
- [ ] T027 パフォーマンス検証
  - /releaseコマンド → GitHub Release作成: 30分以内
  - PR自動マージ: CI/CDチェック完了後5分以内
- [ ] T028 ドキュメント整合性確認
  - CLAUDE.md、README.md、quickstart.mdが3層フローを正確に反映
  - 開発者が迷わずフローを理解できるか確認
- [ ] T029 成功基準達成確認
  - リリース頻度の削減: GitHub Release作成間隔が平均7日以上
  - 自動マージ成功率: 95%以上
  - リリース成果物の完全性: 100%
- [ ] T030 SPEC-1ac96646ステータスを「実装完了」に更新

## 依存関係
- Setup (T001-T005) が Tests (T006-T013) と Core (T014-T019) より先
- Tests (T006-T013) が Core (T014-T019) より先 (TDD)
- Core (T014-T019) が Integration (T020-T022) より先
- Integration (T020-T022) が Polish (T023-T030) より先
- T001 が T002, T003, T004 をブロック
- T006-T010 が T014-T019 をブロック
- T014-T019 が T023 をブロック

## 並列実行例
```
# T003-T005 を一緒に起動:
Task: "mainブランチにBranch Protection Rules設定"
Task: "developブランチにBranch Protection Rules設定"
Task: "gh CLI認証確認"

# T006-T010 を一緒に起動:
Task: "specs/SPEC-1ac96646/contracts/auto-merge.contract.test.sh を作成"
Task: "specs/SPEC-1ac96646/contracts/release-command.contract.test.sh を作成"
Task: "specs/SPEC-1ac96646/contracts/semantic-release.contract.test.sh を作成"
Task: "specs/SPEC-1ac96646/contracts/mcp-publish.contract.test.sh を作成"
Task: "specs/SPEC-1ac96646/contracts/lsp-build.contract.test.sh を作成"

# T011-T013 を一緒に起動:
Task: "tests/integration/full-release-cycle.test.sh を作成"
Task: "tests/integration/pr-migration.test.sh を作成"
Task: "tests/e2e/new-feature-to-release.test.sh を作成"

# T014-T016 を一緒に起動:
Task: ".github/workflows/auto-merge.yml を修正"
Task: ".github/workflows/release.yml を確認"
Task: ".github/workflows/unity-cli-publish.yml を確認"

# T020-T022 を一緒に起動:
Task: "CLAUDE.md を更新"
Task: "README.md を更新"
Task: "specs/SPEC-1ac96646/quickstart.md を確認"
```

## 注意事項
- [P] タスク = 異なるファイル、依存関係なし
- 実装前にテストが失敗することを確認 (TDD厳守)
- 各タスク後にコミット (Conventional Commits規約)
- 回避: 曖昧なタスク、同じファイルの競合

## タスク生成ルール
*main()実行中に適用*

1. **Contractsから**:
   - 各contractファイル → contract testタスク [P]
   - 各endpoint/workflow → 実装タスク

2. **User Storiesから**:
   - 各story → integration test [P]
   - クイックスタートシナリオ → 検証タスク

3. **順序**:
   - Setup → Tests → Core → Integration → Polish
   - 依存関係は並列実行をブロック

## 検証チェックリスト
*ゲート: 戻る前にmain()でチェック*

- [x] すべてのcontractsに対応するテストがある (T006-T010)
- [x] すべてのテストが実装より先にある (T006-T013 → T014-T019)
- [x] 並列タスクは本当に独立している ([P]マーク確認済み)
- [x] 各タスクは正確なファイルパスを指定
- [x] 同じファイルを変更する[P]タスクがない
