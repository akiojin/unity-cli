# 実装計画: PRベースの自動マージ & リリース自動化

**SPEC-ID**: `SPEC-bf408776`
**作成日**: 2025-10-31
**ステータス**: Phase 2 (実装計画)

---

## Phase 0: 技術リサーチ

### semantic-release調査結果

- **参考実装**: [@akiojin/claude-worktree](https://github.com/akiojin/claude-worktree)
- **コア機能**: Conventional Commits解析、自動バージョニング、CHANGELOG生成、タグ作成、npm publish
- **プラグインシステム**: モジュール化された処理（commit-analyzer, release-notes-generator, changelog, npm, git, github）

### プロジェクト固有の要件

本プロジェクトは3つのリリース対象を持つ：

1. **unity-cli** (Node.js package) → npm registry
2. **Unity Package** (UPM package) → バージョン同期のみ（npm非公開）
3. **lsp** (C# CLI) → マルチプラットフォームバイナリ（GitHub Release）

### 技術的課題

1. Unity Packageのバージョンをunity-cliと同期する仕組み
2. lspビルドが完了してからnpm publishする順序制御
3. GitHub Releaseの作成をsemantic-releaseではなくlspワークフローに委譲

---

## Phase 1: 設計とコントラクト

### アーキテクチャ概要

```
┌────────────────────────────────────────────────────────────────┐
│                     Developer Workflow                          │
└────────────────────────────────────────────────────────────────┘
                              │
                              ↓
                    [ finish-feature.sh ]
                              │
                              ↓
┌────────────────────────────────────────────────────────────────┐
│                   Auto-Merge Workflow                           │
│  (既存: .github/workflows/auto-merge.yml)                       │
│   - Required Checks監視                                         │
│   - 自動マージ実行                                               │
└────────────────────────────────────────────────────────────────┘
                              │
                              ↓ main branch
┌────────────────────────────────────────────────────────────────┐
│              Release Workflow (新規)                            │
│  (.github/workflows/release.yml)                                │
│                                                                  │
│  1. Conventional Commits解析 (semantic-release)                 │
│  2. バージョン決定 (major/minor/patch)                          │
│  3. package.json更新                                             │
│     - unity-cli/package.json                                   │
│     - Unity Package package.json (sync script)                  │
│  4. CHANGELOG.md生成                                             │
│  5. リリースコミット作成                                         │
│  6. タグpush (v*)                                                │
└────────────────────────────────────────────────────────────────┘
                              │
                              ↓ tag push
                 ┌────────────┴────────────┐
                 │                          │
                 ↓                          ↓
┌──────────────────────────────┐   ┌────────────────────────────┐
│  lsp Build Workflow   │   │   (GitHub Release作成待機)  │
│  (既存: release-lsp   │   │                             │
│           .yml)               │   └────────────────────────────┘
│                               │
│  1. 6プラットフォームビルド  │
│  2. lsp-manifest.json │
│     生成                      │
│  3. GitHub Release作成        │
│  4. 全アセット添付            │
└──────────────────────────────┘
                 │
                 ↓ release.published
┌──────────────────────────────────────────────────────────────┐
│          npm Publish Workflow (既存: unity-cli-publish.yml) │
│                                                               │
│  1. lsp-manifest.json存在確認                         │
│  2. テスト実行                                                │
│  3. npm publish                                               │
└──────────────────────────────────────────────────────────────┘
```

### データモデル

#### Conventional Commit 形式

```
<type>([scope]): <subject>

[body]

[footer]
```

**type決定ルール**:
- `feat:` → minor version up (例: 2.16.3 → 2.17.0)
- `fix:` → patch version up (例: 2.16.3 → 2.16.4)
- `BREAKING CHANGE:` → major version up (例: 2.16.3 → 3.0.0)
- `chore:`, `docs:`, `test:` → version up なし

#### バージョン同期

```
unity-cli/package.json
  ↓ semantic-release
  version: "2.17.0"
  ↓ sync script (prepareCmd)
UnityCliBridge/Packages/unity-cli-bridge/package.json
  version: "2.17.0"
```

#### リリース依存関係

```
semantic-release
  ↓
  tag push (v2.17.0)
  ↓
  ├─→ lsp build (parallel, ~5分)
  │    ↓
  │    GitHub Release作成
  │
  └─→ npm publish workflow待機
       ↓
       manifest確認 (最大20分)
       ↓
       npm publish実行
```

### コントラクト定義

#### 1. semantic-release設定 (`.releaserc.json`)

```json
{
  "branches": ["main"],
  "tagFormat": "v${version}",
  "plugins": [
    "@semantic-release/commit-analyzer",
    "@semantic-release/release-notes-generator",
    [
      "@semantic-release/changelog",
      {
        "changelogFile": "CHANGELOG.md"
      }
    ],
    [
      "@semantic-release/exec",
      {
        "prepareCmd": "node scripts/sync-unity-package-version.js ${nextRelease.version}"
      }
    ],
    [
      "@semantic-release/npm",
      {
        "npmPublish": false,
        "pkgRoot": "unity-cli"
      }
    ],
    [
      "@semantic-release/git",
      {
        "assets": [
          "unity-cli/package.json",
          "UnityCliBridge/Packages/unity-cli-bridge/package.json",
          "CHANGELOG.md"
        ],
        "message": "chore(release): ${nextRelease.version} [skip ci]\n\n${nextRelease.notes}"
      }
    ]
  ]
}
```

**重要**: `@semantic-release/github`プラグインは使用しない（lspワークフローがRelease作成）

#### 2. Unity Packageバージョン同期スクリプト

**ファイル**: `scripts/sync-unity-package-version.js`

```javascript
/**
 * Sync Unity Package version with unity-cli version
 * Called by semantic-release prepareCmd
 */
const fs = require('fs');
const path = require('path');

const version = process.argv[2];
if (!version) {
  console.error('Version argument is required');
  process.exit(1);
}

const unityPackageJsonPath = path.join(
  __dirname,
  '../UnityCliBridge/Packages/unity-cli-bridge/package.json'
);

const packageJson = JSON.parse(fs.readFileSync(unityPackageJsonPath, 'utf8'));
packageJson.version = version;
fs.writeFileSync(unityPackageJsonPath, JSON.stringify(packageJson, null, 2) + '\n');

console.log(`Unity Package version synced to ${version}`);
```

#### 3. Release Workflow

**ファイル**: `.github/workflows/release.yml`

```yaml
name: Release

on:
  push:
    branches:
      - main

permissions:
  contents: write
  issues: write
  pull-requests: write

jobs:
  release:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
          persist-credentials: false

      - name: Setup Node.js
        uses: actions/setup-node@v4
        with:
          node-version: '18'

      - name: Install dependencies
        working-directory: unity-cli
        run: npm ci

      - name: Run tests
        working-directory: unity-cli
        run: npm run test:ci

      - name: Semantic Release
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: npx semantic-release
```

#### 4. lsp Workflow更新

**ファイル**: `.github/workflows/release-lsp.yml`

**変更点**:
- GitHub Release作成ステップを追加
- `softprops/action-gh-release@v2`でリリース作成
- CHANGELOG.mdから該当バージョンのノートを抽出

#### 5. npm Publish Workflow更新

**ファイル**: `.github/workflows/unity-cli-publish.yml`

**変更点**:
- トリガーを `release.published`に変更
- 既存のmanifest待機ロジックは維持

### 憲章チェック

#### ✅ TDD遵守

- テストファイルを先に作成（RED）
- 実装後にテスト合格（GREEN）
- リファクタリング（REFACTOR）

#### ✅ ハンドラーアーキテクチャ

semantic-releaseはプラグインベースで拡張性があり、既存のハンドラーアーキテクチャと整合

#### ✅ LLM最適化

- CHANGELOG.mdで変更履歴を自動文書化
- Conventional Commitsで意図が明確
- コミットメッセージがそのままリリースノートに

#### ✅ シンプルさの原則

- 既存ツール（semantic-release）を活用
- 最小限のカスタムスクリプト（sync-unity-package-version.js）
- 既存ワークフロー（auto-merge, lsp, npm-publish）を最大限再利用

---

## Phase 2: タスク計画

### Setup Tasks

- [ ] SPEC-bf408776のspec.md更新完了
- [ ] plan.md作成完了
- [ ] tasks.md作成

### Test Phase (RED)

- [ ] `tests/unit/scripts/sync-unity-version.test.js` 作成
  - Unity Package version syncのロジックテスト
  - ファイル読み書きのモックテスト
- [ ] `tests/integration/semantic-release.test.js` 作成
  - semantic-release設定の妥当性テスト
  - Conventional Commits解析テスト
- [ ] `tests/e2e/release-flow.test.js` 作成
  - featureブランチでのドライラン
  - バージョンアップフロー E2E テスト

### Implementation Phase (GREEN)

- [ ] `.releaserc.json` 作成
- [ ] `scripts/sync-unity-package-version.js` 実装
- [ ] `.github/workflows/release.yml` 作成
- [ ] `.github/workflows/release-lsp.yml` 更新（Release作成追加）
- [ ] `.github/workflows/unity-cli-publish.yml` 更新（トリガー変更）
- [ ] `unity-cli/package.json` に semantic-release 依存追加

### Documentation Phase

- [ ] `README.md` 更新（リリースプロセスセクション追加）
- [ ] `CLAUDE.md` 更新（バージョン管理セクション更新）
- [ ] SPEC完了確認

### Integration Phase

- [ ] featureブランチでドライラン
- [ ] mainマージ＆本番リリース検証
- [ ] finish-feature.sh実行（PR作成）

---

## Quickstart

### 開発者向けリリースフロー

1. **通常の開発**: featureブランチでConventional Commitsを使用
   ```bash
   git commit -m "feat: Add new screenshot mode"
   git commit -m "fix: Resolve path resolution issue"
   ```

2. **PR作成**: finish-feature.sh実行
   ```bash
   .specify/scripts/bash/finish-feature.sh
   ```

3. **自動マージ**: Required Checks成功後、自動的にmainへマージ

4. **自動リリース**: mainマージ後、以下が自動実行
   - semantic-releaseがコミット解析
   - バージョン決定（feat→minor、fix→patch）
   - package.json更新（unity-cli + Unity Package）
   - CHANGELOG.md生成
   - タグpush (v2.17.0)
   - lspビルド開始
   - GitHub Release作成
   - npm publish実行

5. **完了確認**: npmレジストリとGitHub Releaseを確認

### 注意事項

- **[skip ci]付きコミット**: リリースコミット自体は`[skip ci]`でCIスキップ
- **ブランチ保護**: semantic-releaseボットによる`chore(release)`コミットを許可
- **シークレット**: `NPM_TOKEN`必須、`GITHUB_TOKEN`は自動提供

---

## リスク＆軽減策

### リスク1: lspビルド失敗でnpm publishが中断

**軽減策**:
- unity-cli-publish.ymlでmanifest待機（最大20分）
- タイムアウト時はエラー通知
- 手動再実行可能（workflow_dispatch）

### リスク2: Unity Packageバージョン同期失敗

**軽減策**:
- sync-unity-package-version.jsでエラーハンドリング
- 失敗時はsemantic-releaseプロセス全体が中断
- ユニットテストで検証

### リスク3: Conventional Commits規約違反

**軽減策**:
- commitlint導入（PRチェック）
- ドキュメント整備（README.md）
- 違反時は自動リリーススキップ（警告ログ）

---

## 参考資料

- [semantic-release公式ドキュメント](https://semantic-release.gitbook.io/)
- [Conventional Commits仕様](https://www.conventionalcommits.org/)
- [@akiojin/claude-worktree実装](https://github.com/akiojin/claude-worktree)
