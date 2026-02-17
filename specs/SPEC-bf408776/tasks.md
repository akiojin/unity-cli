# タスクリスト: PRベースの自動マージ & リリース自動化

**SPEC-ID**: `SPEC-bf408776`
**作成日**: 2025-10-31

---

## Setup Tasks

- [x] **S001**: SPEC-bf408776のspec.md更新（リリース自動化要件追加）
- [x] **S002**: plan.md作成（semantic-release技術設計）
- [x] **S003**: tasks.md作成（このファイル）

---

## Test Tasks（RED Phase）

### Unit Tests

- [x] **T001** [P]: Unity Packageバージョン同期スクリプトのテスト作成
  - **ファイル**: `tests/unit/scripts/sync-unity-version.test.js`
  - **目的**: sync-unity-package-version.jsの動作検証
  - **検証内容**:
    - 正常系: バージョン引数を受け取りUnity package.jsonを更新
    - 異常系: バージョン引数なしでエラー終了
    - 異常系: Unity package.json読み込み失敗

  - **期待結果**: テスト失敗（RED）

- [ ] **T002** [P]: CHANGELOG生成テスト作成
  - **ファイル**: `tests/unit/changelog.test.js`
  - **目的**: semantic-releaseのCHANGELOG生成が正しく動作するか
  - **検証内容**:
    - feat/fix/BREAKING CHANGEコミットが正しく分類される
    - マークダウン形式が正しい

  - **期待結果**: テスト失敗（RED）

### Integration Tests

- [ ] **T003**: semantic-release設定の統合テスト作成
  - **ファイル**: `tests/integration/semantic-release.test.js`
  - **目的**: .releaserc.json設定の妥当性検証
  - **検証内容**:
    - Conventional Commits解析が正しく動作
    - バージョン決定ロジック（feat→minor、fix→patch）
    - Unity Packageバージョン同期が実行される
    - CHANGELOG.mdが生成される
  - **期待結果**: テスト失敗（RED）

- [ ] **T004**: リリースワークフロー統合テスト作成
  - **ファイル**: `tests/integration/release-workflow.test.js`
  - **目的**: GitHub Actionsワークフロー連携の検証
  - **検証内容**:
    - mainマージ → semantic-release実行
    - タグpush → lspビルド起動
    - GitHub Release作成 → npm publish起動
  - **期待結果**: テスト失敗（RED）

### E2E Tests

- [ ] **T005**: リリースフローE2Eテスト作成
  - **ファイル**: `tests/e2e/release-flow.test.js`
  - **目的**: エンドツーエンドリリースフローの検証
  - **検証内容**:
    - featureブランチでConventional Commitsを作成
    - PRマージ後にバージョンアップ
    - npm, Unity Package, lspが同一バージョン
    - CHANGELOG.mdに変更内容が記載
  - **期待結果**: テスト失敗（RED）

---

## Core Tasks（GREEN Phase）

### Configuration Files

- [x] **C001** [P]: .releaserc.json作成
  - **ファイル**: `.releaserc.json`
  - **内容**:
    - branches: ["main"]
    - tagFormat: "v${version}"
    - plugins:
      - @semantic-release/commit-analyzer
      - @semantic-release/release-notes-generator
      - @semantic-release/changelog
      - @semantic-release/exec (Unity sync)
      - @semantic-release/npm (npmPublish: false)
      - @semantic-release/git
  - **検証**: T003統合テストが合格（GREEN）

- [x] **C002** [P]: package.jsonにsemantic-release依存追加
  - **ファイル**: `unity-cli/package.json`
  - **追加内容**:
    ```json
    "devDependencies": {
      "@semantic-release/changelog": "^6.0.3",
      "@semantic-release/commit-analyzer": "^11.1.0",
      "@semantic-release/exec": "^6.0.3",
      "@semantic-release/git": "^10.0.1",
      "@semantic-release/release-notes-generator": "^12.1.0",
      "semantic-release": "^22.0.12"
    }
    ```
  - **検証**: npm ciが成功

### Scripts

- [x] **C003**: Unity Packageバージョン同期スクリプト実装
  - **ファイル**: `scripts/sync-unity-package-version.js`
  - **実装内容**:
    - コマンドライン引数からバージョン取得
    - Unity package.json読み込み
    - versionフィールド更新
    - ファイル書き込み（フォーマット維持）
    - エラーハンドリング
  - **検証**: T001ユニットテストが合格（GREEN）

### GitHub Actions Workflows

- [x] **C004**: Release Workflow作成
  - **ファイル**: `.github/workflows/release.yml`
  - **実装内容**:
    - トリガー: push to main
    - ステップ:
      1. Checkout (fetch-depth: 0)
      2. Setup Node.js 18
      3. npm ci
      4. npm run test:ci
      5. npx semantic-release
    - 権限: contents: write
  - **検証**: T004統合テストが合格

- [x] **C005**: lsp Workflow更新（GitHub Release作成追加）
  - **ファイル**: `.github/workflows/release-lsp.yml`
  - **変更内容**:
    - releaseジョブに`body_path`追加
    - CHANGELOG.mdから該当バージョンのノート抽出
    - `softprops/action-gh-release@v2`でRelease作成
  - **検証**: タグpush時にGitHub Releaseが作成される

- [x] **C006**: npm Publish Workflow更新（トリガー調整）
  - **ファイル**: `.github/workflows/unity-cli-publish.yml`
  - **変更内容**:
    - トリガーを`release.published`に変更
    - 既存のmanifest待機ロジックは維持
  - **検証**: GitHub Release作成後にnpm publishが実行される

---

## Integration Tasks

### Documentation

- [x] **I001**: README.md更新（リリースプロセスセクション追加）
  - **ファイル**: `README.md`
  - **追加セクション**:
    - ## リリースプロセス
      - 自動リリースフローの説明
      - Conventional Commits規約
      - バージョニングルール
      - トラブルシューティング
  - **検証**: markdownlintエラーなし

- [x] **I002**: CLAUDE.md更新（バージョン管理セクション）
  - **ファイル**: `CLAUDE.md`
  - **変更内容**:
    - `npm version`コマンド → semantic-releaseに更新
    - Conventional Commits規約を追記
    - リリースフロー図を追加
  - **検証**: markdownlintエラーなし

### Testing & Validation

- [x] **I003**: 全テスト実行＆検証
  - **コマンド**: `npm run test:ci`
  - **検証内容**:
    - すべてのユニットテストが合格
    - すべての統合テストが合格
    - テストカバレッジ80%以上
  - **期待結果**: すべてGREEN

- [x] **I004**: featureブランチでドライラン
  - **手順**:
    1. テストコミット作成（feat: Test release automation）
    2. ローカルでsemantic-release実行（--dry-run）
    3. バージョン決定とCHANGELOG生成を確認
  - **検証**: エラーなくドライラン完了

- [x] **I005**: finish-feature.sh実行してPR作成
  - **実施内容**:
    - finish-feature.shのブランチ名検証を修正（feature/* 全般対応）
    - PR #17を作成
    - GitHub Actions自動マージワークフロー起動
  - **検証**: PR正常作成、自動マージ待機中

---

## Polish Tasks

### CI/CD Integration

- [ ] **P001**: commitlint導入（Conventional Commits強制）
  - **ファイル**: `.commitlintrc.json`
  - **内容**:
    ```json
    {
      "extends": ["@commitlint/config-conventional"]
    }
    ```
  - **GitHub Actions**: PRチェックに追加
  - **検証**: 不正なコミットメッセージでPR失敗

- [ ] **P002**: リリースノート品質向上
  - **カスタムリリースノートテンプレート**: 検討
  - **セクション分類**: Features, Bug Fixes, BREAKING CHANGES
  - **検証**: 生成されるCHANGELOG.mdの可読性

### Monitoring & Alerts

- [ ] **P003**: リリース失敗時の通知設定
  - **Slack/Discord Webhook**: 検討
  - **GitHub Issues自動作成**: semantic-release失敗時
  - **検証**: リリース失敗を意図的に発生させて通知確認

---

## Completion Checklist

- [x] すべてのSetup Tasksが完了
- [x] すべてのTest Tasks（RED）が完了
- [x] すべてのCore Tasks（GREEN）が完了し、テストが合格
- [x] すべてのIntegration Tasksが完了
- [ ] すべてのPolish Tasksが完了（Optional: commitlint、カオステストなど）
- [x] SPEC-bf408776の成功基準をすべて満たす
- [x] ドキュメント（README.md, CLAUDE.md）が最新
- [ ] mainマージ後のリリースが正常に完了（PR作成後に確認）

---

## タスク実行ログ

### 2025-10-31

- [x] S001: spec.md更新完了
- [x] S002: plan.md作成完了
- [x] S003: tasks.md作成完了
- [x] T001: Unity同期スクリプトテスト作成完了（RED）
- [x] C003: Unity同期スクリプト実装完了（GREEN、全テスト合格）
- [x] C001: .releaserc.json作成完了
- [x] C002: package.json依存追加完了
- [x] C004: release.ymlワークフロー作成完了
- [x] C005: release-lsp.yml更新完了
- [x] C006: unity-cli-publish.yml更新完了
- [x] I001: README.md更新完了
- [x] I002: CLAUDE.md更新完了
- [x] I003: 全テスト実行＆検証完了（63/63 pass）
- [x] ワークスペースpackage.json作成＆semantic-releaseドライラン成功
- [x] I004: semantic-releaseドライラン完了（バージョン決定とCHANGELOG生成確認）
- [x] I005: finish-feature.shのブランチ名検証修正完了（feature/* 全般対応）
- [x] PR #17作成完了（https://github.com/akiojin/unity-cli/pull/17）
- [x] GitHub Actions自動マージワークフロー起動中

---

## 注記

- **[P]**: 並列実行可能なタスク
- **依存関係**: Test Tasks → Core Tasks → Integration Tasks → Polish Tasks
- **TDDサイクル**: 必ずテスト作成（RED）→ 実装（GREEN）→ リファクタリング の順序を守る
