# 実装計画: 3層リリースフロー（feature → develop → main）

**機能ID**: `SPEC-1ac96646` | **日付**: 2025-11-07 | **仕様**: [spec.md](./spec.md)
**入力**: `/specs/SPEC-1ac96646/spec.md`の機能仕様

## 実行フロー (/speckit.plan コマンドのスコープ)
```
1. 入力パスから機能仕様を読み込み ✅
2. 技術コンテキストを記入 ✅
3. 憲章チェックセクションを評価 ✅
4. Phase 0 を実行 → research.md
5. Phase 1 を実行 → contracts, data-model.md, quickstart.md
6. 憲章チェックセクションを再評価
7. Phase 2 を計画 → タスク生成アプローチを記述
8. 停止 - /speckit.tasks コマンドの準備完了
```

## 概要

本機能は、現在の2層ブランチフロー（feature → main）を3層フロー（feature → develop → main）に変更することで、リリース頻度を削減し、リリース品質を向上させます。

**主要要件**:
- developブランチで変更を蓄積し、意図的なタイミングでmainにリリース
- feature → develop は自動マージ、develop → main は手動トリガー（/releaseコマンド）
- mainマージ時にsemantic-release、MCPサーバー（npm）、LSPサーバー（全プラットフォーム）を自動配信

**技術アプローチ**:
- GitHub Actionsワークフローの調整（auto-merge, release, unity-cli-publish）
- Bashスクリプトの修正（finish-feature.sh、新規PR移行スクリプト）
- 新規/releaseコマンドの実装（semantic-release dry-run、PR作成）

## 技術コンテキスト

**言語/バージョン**: Bash 4.x+, Node.js 18+, YAML（GitHub Actions）
**主要依存関係**:
- gh CLI（GitHub CLI）: PR操作、ブランチ操作
- semantic-release: 自動バージョニング、リリースノート生成
- npm: MCPサーバー配信
- .NET SDK 6.0+: LSPサーバービルド

**ストレージ**: N/A（ファイルシステムのみ）
**テスト**: bash-test-framework, GitHub Actions local runner（act）
**対象プラットフォーム**: Linux（CI/CD）、macOS/Linux/Windows（開発環境）
**プロジェクトタイプ**: インフラストラクチャ/CI/CD（既存リポジトリの拡張）
**パフォーマンス目標**:
- /releaseコマンド → GitHub Release作成: 30分以内
- PR自動マージ: CI/CDチェック完了後5分以内

**制約**:
- GitHub Actionsの実行時間制限（無料プランで6時間）
- semantic-releaseはConventional Commits規約に依存
- gh CLI認証が必要（`gh auth login`）

**スケール/スコープ**:
- 既存16個のfeature PR移行
- GitHub Actionsワークフロー3個の調整
- Bashスクリプト2個の修正・新規作成
- ドキュメント3個の更新

## 憲章チェック

**シンプルさ**:
- プロジェクト数: 1（既存unity-cliリポジトリの拡張）✅
- フレームワークを直接使用? はい（GitHub Actions、semantic-release、gh CLIを直接使用）✅
- 単一データモデル? N/A（インフラストラクチャ変更、データモデルなし）✅
- パターン回避? はい（Repository/UoWパターンなし、シンプルなBashスクリプト）✅

**アーキテクチャ**:
- すべての機能をライブラリとして? N/A（インフラストラクチャ変更）
- ライブラリリスト: N/A
- ライブラリごとのCLI:
  - `/release`コマンド: Claudeスラッシュコマンド（.claude/commands/release.md）
  - finish-feature.sh: 既存スクリプト、修正のみ
  - migrate-pr-to-develop.sh: 新規スクリプト、--help/--dry-run提供
- ライブラリドキュメント: CLAUDE.mdに統合（llms.txt形式ではない）

**テスト (妥協不可)**:
- RED-GREEN-Refactorサイクルを強制? はい（契約テスト → 失敗確認 → 実装）✅
- Gitコミットはテストが実装より先に表示? はい（TDD厳守）✅
- 順序: Contract→Integration→E2E→Unitを厳密に遵守? はい✅
- 実依存関係を使用? はい（実GitHub Actions、実gh CLI、実semantic-release）✅
- Integration testの対象: GitHub Actionsワークフロー、Bashスクリプト、PR操作✅
- 禁止: テスト前の実装、REDフェーズのスキップ ✅

**可観測性**:
- 構造化ロギング含む? はい（GitHub Actionsログ、スクリプトecho出力）
- フロントエンドログ → バックエンド? N/A
- エラーコンテキスト十分? はい（失敗時にPRコメント、エラーログ出力）

**バージョニング**:
- バージョン番号割り当て済み? はい（semantic-releaseが自動管理、Conventional Commits準拠）
- 変更ごとにBUILDインクリメント? はい（feat/fixコミットごとにバージョン自動更新）
- 破壊的変更を処理? はい（既存PR移行計画、テスト並列実行）

## プロジェクト構造

### ドキュメント (この機能)
```
specs/SPEC-1ac96646/
├── spec.md               # 機能仕様（完成）
├── plan.md               # このファイル（進行中）
├── research.md           # Phase 0 出力（未作成）
├── data-model.md         # Phase 1 出力（N/A - インフラ変更）
├── quickstart.md         # Phase 1 出力（未作成）
├── contracts/            # Phase 1 出力（契約テスト定義）
└── tasks.md              # Phase 2 出力（/speckit.tasksで作成）
```

### ソースコード (リポジトリルート)
```
.github/workflows/
├── auto-merge.yml        # 修正: developのみ対象
├── release.yml           # 現状維持: mainでsemantic-release
└── unity-cli-publish.yml # 現状維持: GitHub Releaseでnpm publish

.specify/scripts/bash/
├── finish-feature.sh     # 修正: --base develop
└── migrate-pr-to-develop.sh  # 新規: 既存PR移行

.claude/commands/
└── release.md            # 新規: /releaseコマンド実装

tests/
├── contracts/
│   ├── auto-merge.contract.test.sh
│   ├── release-command.contract.test.sh
│   ├── semantic-release.contract.test.sh
│   ├── mcp-publish.contract.test.sh
│   └── lsp-build.contract.test.sh
├── integration/
│   ├── full-release-cycle.test.sh
│   └── pr-migration.test.sh
└── e2e/
    └── new-feature-to-release.test.sh

CLAUDE.md                 # 修正: 3層フロー説明
README.md                 # 修正: リリースフロー図追加
```

**構造決定**: 既存リポジトリ構造を維持、新規ファイル最小限

## Phase 0: アウトライン＆リサーチ

**リサーチタスク**:

1. **GitHub Actions ブランチフィルタリング**:
   - 調査: `branches:` フィルタで複数ブランチを条件分岐する方法
   - 目的: auto-merge.ymlでdevelopのみ対象にする方法確認

2. **semantic-release ブランチ設定**:
   - 調査: `.releaserc.json`の`branches`設定とmainブランチ専用化
   - 目的: developブランチでsemantic-releaseが実行されないことを確認

3. **gh CLI PR操作**:
   - 調査: `gh pr create`の`--base`オプション、PRのベースブランチ変更方法
   - 目的: finish-feature.sh修正、PR移行スクリプト実装の前提知識

4. **Claudeスラッシュコマンド実装**:
   - 調査: `.claude/commands/*.md`のフォーマット、semantic-release dry-run実行方法
   - 目的: /releaseコマンドの実装仕様決定

5. **既存PR一括操作**:
   - 調査: `gh pr edit`でベースブランチ変更、複数PR処理のベストプラクティス
   - 目的: migrate-pr-to-develop.sh実装の設計

**出力**: `research.md`（各調査結果、決定事項、代替案の記録）

## Phase 1: 設計＆契約

**データモデル**: N/A（インフラストラクチャ変更のため、エンティティなし）

**API契約**: N/A（REST/GraphQL APIなし、CI/CDワークフローのみ）

**契約テスト** (`/contracts/`):

1. **auto-merge.contract.test.sh**:
   ```bash
   # 契約: developへのPRは自動マージ、mainへのPRは自動マージしない
   test_develop_pr_auto_merges()
   test_main_pr_does_not_auto_merge()
   test_auto_merge_waits_for_checks()
   test_auto_merge_comments_on_failure()
   ```

2. **release-command.contract.test.sh**:
   ```bash
   # 契約: /releaseコマンドがリリースノートプレビュー、PR作成
   test_release_command_exists()
   test_release_shows_preview()
   test_release_creates_pr_without_auto_merge()
   test_release_skips_if_no_changes()
   ```

3. **semantic-release.contract.test.sh**:
   ```bash
   # 契約: mainマージ時にsemantic-release実行、成果物生成
   test_semantic_release_runs_on_main_merge()
   test_package_json_updated()
   test_unity_package_version_synced()
   test_changelog_generated()
   test_tag_created()
   ```

4. **mcp-publish.contract.test.sh**:
   ```bash
   # 契約: GitHub Release時にnpm publish実行
   test_mcp_publish_on_release()
   test_version_matches_tag()
   test_npmjs_package_available()
   ```

5. **lsp-build.contract.test.sh**:
   ```bash
   # 契約: LSPサーバー全プラットフォームビルド
   test_lsp_manifest_exists()
   test_all_platforms_built()
   test_binaries_attached_to_release()
   ```

**統合テストシナリオ** (`/tests/integration/`):

1. **full-release-cycle.test.sh**:
   - feature → develop PR → 自動マージ
   - /release実行 → develop → main PR
   - PRマージ → semantic-release → MCPサーバー → LSPサーバー → GitHub Release

2. **pr-migration.test.sh**:
   - 既存mainベースPRをdevelopベースに変更
   - CI/CDチェック再実行確認
   - auto-merge動作確認

**Quickstart** (`quickstart.md`):
- developブランチ作成手順
- GitHub上でデフォルトブランチ変更手順
- /releaseコマンド使用例
- 既存PR移行手順

**エージェントファイル更新**: `CLAUDE.md`
- Worktree＆ブランチ運用セクション更新
- リリースフローセクション更新
- /releaseコマンド説明追加

**出力**: contracts/*, quickstart.md, CLAUDE.md更新

## Phase 2: タスク計画アプローチ

**タスク生成戦略**:
- Setup（5タスク）: developブランチ作成、GitHub設定、環境準備
- Test（10タスク）: 契約テスト5個、統合テスト2個、E2Eテスト1個実装
- Core（8タスク）: GitHub Actions修正、スクリプト修正/新規作成、/releaseコマンド実装
- Integration（3タスク）: CLAUDE.md更新、README.md更新、quickstart.md作成
- Polish（4タスク）: 既存PR移行、テスト検証、ドキュメント最終確認、完了確認

**順序戦略**:
- TDD順序: 契約テスト → 実装 → 統合テスト → E2Eテスト
- 依存関係順序: developブランチ作成 → ワークフロー修正 → スクリプト修正 → /releaseコマンド → PR移行
- 並列実行[P]: 契約テスト5個、ワークフロー修正3個、ドキュメント更新3個

**推定出力**: tasks.mdに30個の番号付き、順序付きタスク

**重要**: このフェーズは/speckit.tasksコマンドで実行、/speckit.planではない

## Phase 3+: 今後の実装

**Phase 3**: タスク実行 (/speckit.tasksコマンドがtasks.mdを作成)
**Phase 4**: 実装 (憲章原則に従ってtasks.mdを実行)
**Phase 5**: 検証 (テスト実行、quickstart.md実行、パフォーマンス検証)

## 複雑さトラッキング

*憲章チェックに正当化が必要な違反なし*

| 違反 | 必要な理由 | より単純な代替案が却下された理由 |
|------|-----------|--------------------------------|
| なし | - | - |

## 進捗トラッキング

**フェーズステータス**:
- [x] Phase 0: Research完了 (/speckit.plan コマンド) ✅ 2025-11-07
- [x] Phase 1: Design完了 (/speckit.plan コマンド) ✅ 2025-11-09
- [x] Phase 2: Task planning完了 (/speckit.plan コマンド - アプローチのみ記述) ✅ 2025-11-09
- [x] Phase 3: Tasks生成済み (/speckit.tasks コマンド) ✅ 2025-11-09
- [x] Phase 4: 実装完了 ✅ 2025-11-09
- [x] Phase 5: 検証合格 ✅ 2025-11-09

**ゲートステータス**:
- [x] 初期憲章チェック: 合格
- [x] 設計後憲章チェック: 合格 ✅ 2025-11-09
- [x] すべての要明確化解決済み ✅ research.md参照
- [x] 複雑さの逸脱を文書化済み（違反なし）

---
*憲章 v1.0.0 に基づく - `/docs/constitution.md` 参照*
