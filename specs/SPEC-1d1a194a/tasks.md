# タスク: MCP Capabilities正常認識

**機能ID**: SPEC-1d1a194a
**入力**: `/specs/SPEC-1d1a194a/`の設計ドキュメント
**前提条件**: plan.md, research.md, data-model.md, contracts/mcp-capabilities.schema.json, quickstart.md

## 実行フロー

このタスクリストはTDD (Test-Driven Development)原則に厳密に従います:

1. **RED**: テストを書く → テスト失敗を確認
2. **GREEN**: 最小限の実装でテスト合格
3. **REFACTOR**: コードをクリーンアップ

**重要**: テストコミットが実装コミットより先に記録される必要があります。

## Phase 3.1: セットアップ

- [ ] **T001** [P] `unity-cli/tests/contract/`ディレクトリ作成
- [ ] **T002** [P] `unity-cli/tests/integration/`ディレクトリ作成（既存の場合はスキップ）
- [ ] **T003** [P] `unity-cli/tests/unit/core/`ディレクトリ作成（既存の場合はスキップ）

**並列実行**: T001, T002, T003は同時実行可能

---

## Phase 3.2: テストファースト (TDD) ⚠️ Phase 3.3の前に完了必須

**重要**: これらのテストは記述され、実装前に失敗する必要があります（REDフェーズ）

### Contract Tests (並列実行可)

- [ ] **T004** [P] `unity-cli/tests/contract/mcp-capabilities.test.js`作成
  - **目的**: ServerCapabilitiesが正しい形式であることを検証
  - **テスト内容**:
    - `capabilities.tools`が宣言されている
    - `capabilities.tools.listChanged === true`
    - `capabilities.resources === undefined`（省略）
    - `capabilities.prompts === undefined`（省略）
  - **期待される結果**: ❌ REDフェーズ（テスト失敗）
  - **ファイルパス**: `unity-cli/tests/contract/mcp-capabilities.test.js`
  - **依存関係**: なし

- [ ] **T005** [P] `unity-cli/tests/contract/mcp-handler-registration.test.js`作成
  - **目的**: 未サポートcapabilityのハンドラーが登録されていないことを検証
  - **テスト内容**:
    - `ListResourcesRequestSchema`ハンドラーが登録されていない
    - `ListPromptsRequestSchema`ハンドラーが登録されていない
    - `ListToolsRequestSchema`ハンドラーが登録されている
    - `CallToolRequestSchema`ハンドラーが登録されている
  - **期待される結果**: ❌ REDフェーズ（テスト失敗）
  - **ファイルパス**: `unity-cli/tests/contract/mcp-handler-registration.test.js`
  - **依存関係**: なし

**並列実行**: T004, T005は同時実行可能

### Integration Tests

- [ ] **T006** `unity-cli/tests/integration/mcp-tools-list.test.js`作成
  - **目的**: MCP SDK経由でtools/listリクエストを送信し、107個のツール定義が返却されることを検証
  - **テスト内容**:
    - MCP Clientを作成
    - MCPサーバーに接続
    - `tools/list`リクエストを送信
    - 107個のツール定義が返却されることを確認
    - `ping`ツールが含まれることを確認
  - **期待される結果**: ✅ GREENフェーズ（実装後に合格）
  - **ファイルパス**: `unity-cli/tests/integration/mcp-tools-list.test.js`
  - **依存関係**: T004, T005完了後

### Unit Tests

- [ ] **T007** `unity-cli/tests/unit/core/server-capabilities.test.js`作成
  - **目的**: server.jsのcapabilities宣言が正しい形式であることを検証
  - **テスト内容**:
    - `createServer()`を呼び出し
    - server capabilitiesを取得
    - `tools`が宣言されていることを確認
    - `resources`が`undefined`であることを確認
    - `prompts`が`undefined`であることを確認
  - **期待される結果**: ❌ REDフェーズ（テスト失敗）
  - **ファイルパス**: `unity-cli/tests/unit/core/server-capabilities.test.js`
  - **依存関係**: T004, T005完了後

**テスト実行（REDフェーズ確認）**:

- [ ] **T008** テストを実行してREDフェーズを確認
  - **コマンド**: `cd unity-cli && npm run test:ci`
  - **期待される結果**: T004, T005, T007のテストが失敗（❌ RED）
  - **コミット**: `test(unity-cli): add capabilities contract tests (RED phase)`

---

## Phase 3.3: コア実装 (テストが失敗した後のみ)

**前提条件**: Phase 3.2のREDフェーズ確認完了（T008）

### Step 1: capabilities宣言修正

- [ ] **T009** `unity-cli/src/core/server.js`のcapabilities宣言を修正（1箇所目）
  - **ファイルパス**: `unity-cli/src/core/server.js` (Line 29-36付近)
  - **変更内容**:
    ```diff
    capabilities: {
      tools: { listChanged: true },
    -  resources: {},
    -  prompts: {}
    }
    ```
  - **依存関係**: T008完了後
  - **テスト**: T004, T007が合格に変わることを確認

- [ ] **T010** `unity-cli/src/core/server.js`のcapabilities宣言を修正（2箇所目: createServer関数）
  - **ファイルパス**: `unity-cli/src/core/server.js` (Line 300-305付近)
  - **変更内容**:
    ```diff
    capabilities: {
      tools: { listChanged: true },
    -  resources: {},
    -  prompts: {}
    }
    ```
  - **依存関係**: T009完了後

### Step 2: ハンドラー削除

- [ ] **T011** `unity-cli/src/core/server.js`からListResourcesRequestSchemaハンドラー削除
  - **ファイルパス**: `unity-cli/src/core/server.js` (Line 66-71付近)
  - **削除対象**:
    ```javascript
    // Handle resources listing
    server.setRequestHandler(ListResourcesRequestSchema, async () => {
      logger.debug('[MCP] Received resources/list request');
      return { resources: [] };
    });
    ```
  - **依存関係**: T010完了後
  - **テスト**: T005が合格に変わることを確認

- [ ] **T012** `unity-cli/src/core/server.js`からListPromptsRequestSchemaハンドラー削除
  - **ファイルパス**: `unity-cli/src/core/server.js` (Line 73-78付近)
  - **削除対象**:
    ```javascript
    // Handle prompts listing
    server.setRequestHandler(ListPromptsRequestSchema, async () => {
      logger.debug('[MCP] Received prompts/list request');
      return { prompts: [] };
    });
    ```
  - **依存関係**: T011完了後

- [ ] **T013** `unity-cli/src/core/server.js`からListResourcesRequestSchemaハンドラー削除（createServer関数内）
  - **ファイルパス**: `unity-cli/src/core/server.js` (Line 314-316付近)
  - **削除対象**:
    ```javascript
    testServer.setRequestHandler(ListResourcesRequestSchema, async () => {
      return { resources: [] };
    });
    ```
  - **依存関係**: T012完了後

- [ ] **T014** `unity-cli/src/core/server.js`からListPromptsRequestSchemaハンドラー削除（createServer関数内）
  - **ファイルパス**: `unity-cli/src/core/server.js` (Line 318-320付近)
  - **削除対象**:
    ```javascript
    testServer.setRequestHandler(ListPromptsRequestSchema, async () => {
      return { prompts: [] };
    });
    ```
  - **依存関係**: T013完了後

### Step 3: import文修正

- [ ] **T015** `unity-cli/src/core/server.js`からListResourcesRequestSchema, ListPromptsRequestSchemaのimport削除
  - **ファイルパス**: `unity-cli/src/core/server.js` (Line 3-8付近)
  - **変更内容**:
    ```diff
    import {
      ListToolsRequestSchema,
      CallToolRequestSchema,
    -  ListResourcesRequestSchema,
    -  ListPromptsRequestSchema
    } from '@modelcontextprotocol/sdk/types.js';
    ```
  - **依存関係**: T014完了後

**テスト実行（GREENフェーズ確認）**:

- [ ] **T016** テストを実行してGREENフェーズを確認
  - **コマンド**: `cd unity-cli && npm run test:ci`
  - **期待される結果**: T004, T005, T006, T007のすべてのテストが合格（✅ GREEN）
  - **コミット**: `fix(unity-cli): remove empty capabilities causing "Capabilities: none"`

---

## Phase 3.4: 統合

- [ ] **T017** 既存68個のテストをすべて実行（リグレッション確認）
  - **コマンド**: `cd unity-cli && npm test`
  - **期待される結果**: 68/68 tests passed
  - **依存関係**: T016完了後

---

## Phase 3.5: 仕上げ

### Documentation

- [ ] **T018** [P] `unity-cli/README.md`にトラブルシューティングセクション追加
  - **ファイルパス**: `unity-cli/README.md` (Troubleshooting セクション)
  - **追加内容**:
    - 「Capabilities: none」問題の症状説明
    - 原因の説明（空オブジェクト`{}`の問題）
    - 解決策（最新版へのアップデート）
    - Unity Editor接続確認手順
    - MCP client互換性チェック
  - **依存関係**: T017完了後
  - **コミット**: `docs(unity-cli): add "Capabilities: none" troubleshooting guide`

### Code Quality

- [ ] **T019** ESLint実行
  - **コマンド**: `cd unity-cli && npm run lint`
  - **期待される結果**: エラー/ワーニングなし
  - **依存関係**: T018完了後

- [ ] **T020** Prettier実行
  - **コマンド**: `cd unity-cli && npm run format`
  - **期待される結果**: フォーマット完了
  - **依存関係**: T019完了後

### Manual Testing

- [ ] **T021** `quickstart.md`に従って手動検証を実施
  - **検証項目**:
    - Claude Codeで「Capabilities: tools」と表示される
    - 107個のツールすべてが認識される
    - pingツールが正常に実行される
    - サーバーログにエラー/ワーニングが出力されない
  - **ファイルパス**: `specs/SPEC-1d1a194a/quickstart.md`
  - **依存関係**: T020完了後

### Finalization

- [ ] **T022** 最終コミット作成（Conventional Commits準拠）
  - **コミットメッセージ**:
    ```
    fix(unity-cli): resolve MCP capabilities recognition issue

    Fixed "Capabilities: none" problem in Claude Code by removing empty
    capability objects and unused handlers. MCP SDK v0.6.1 requires
    unsupported capabilities to be omitted, not set to empty objects.

    Changes:
    - Remove empty resources/prompts from capabilities declaration (2 places)
    - Remove ListResourcesRequestSchema handler (2 places)
    - Remove ListPromptsRequestSchema handler (2 places)
    - Remove unused imports
    - Add troubleshooting guide to README.md

    Test results:
    - All 68 existing tests passed
    - 4 new contract/integration/unit tests added and passed

    Closes SPEC-1d1a194a

    🤖 Generated with [Claude Code](https://claude.com/claude-code)

    Co-Authored-By: Claude <noreply@anthropic.com>
    ```
  - **依存関係**: T021完了後

- [ ] **T023** リモートリポジトリにpush
  - **コマンド**: `git push origin bugfix/non-register-tools`
  - **依存関係**: T022完了後

---

## 依存関係グラフ

```
Setup (T001-T003) [並列実行可]
    ↓
Contract Tests (T004-T005) [並列実行可]
    ↓
Integration & Unit Tests (T006-T007)
    ↓
RED Phase確認 (T008)
    ↓
Implementation (T009-T015) [順次実行]
    ↓
GREEN Phase確認 (T016)
    ↓
Regression Test (T017)
    ↓
Documentation (T018) [並列実行可]
    ↓
Code Quality (T019-T020) [順次実行]
    ↓
Manual Testing (T021)
    ↓
Finalization (T022-T023)
```

---

## 並列実行例

### Setup Phase

```bash
# T001, T002, T003を同時実行:
mkdir -p unity-cli/tests/contract
mkdir -p unity-cli/tests/integration
mkdir -p unity-cli/tests/unit/core
```

### Contract Tests Phase

```bash
# T004, T005を同時実行:
Task 1: "unity-cli/tests/contract/mcp-capabilities.test.js作成"
Task 2: "unity-cli/tests/contract/mcp-handler-registration.test.js作成"
```

---

## 2026-02-14 追補タスク（Issue #381: カテゴリ指定 + TDD）

- [x] **T024** [P] [US4] `unity-cli/tests/unit/core/toolCategoryFilter.test.js` を追加（カテゴリ分類・include/exclude・未知カテゴリを検証）
- [x] **T025** [P] [US4] `unity-cli/tests/unit/core/startServer.test.js` にカテゴリフィルタ時の `tools/list` / `tools/call` 整合テストを追加
- [x] **T026** [P] [US4] `unity-cli/tests/unit/core/config.test.js` にカテゴリ環境変数読込テストを追加
- [x] **T027** [US4] `unity-cli/src/core/toolCategoryFilter.js` を実装
- [x] **T028** [US4] `unity-cli/src/core/config.js` に `UNITY_CLI_TOOL_INCLUDE_CATEGORIES` / `UNITY_CLI_TOOL_EXCLUDE_CATEGORIES` を実装
- [x] **T029** [US4] `unity-cli/src/core/server.js` で `tools/list` と `tools/call` に公開ポリシーを適用
- [x] **T030** [US4] `docs/configuration.md`, `docs/tools.md`, `README.md`, `README.ja.md` を更新
- [x] **T031** [US4] `node --test tests/unit/core/toolCategoryFilter.test.js tests/unit/core/config.test.js tests/unit/core/startServer.test.js` を実行し、GREENを確認

---

## 検証チェックリスト

- [x] すべてのcontractsに対応するテストがある（T004, T005）
- [x] すべてのentitiesにmodelタスクがある（N/A - データモデル変更なし）
- [x] すべてのテストが実装より先にある（T004-T008 → T009-T015）
- [x] 並列タスクは本当に独立している（T001-T003, T004-T005は異なるファイル）
- [x] 各タスクは正確なファイルパスを指定
- [x] 同じファイルを変更する[P]タスクがない

---

## 注意事項

- **TDD厳守**: テストコミットが実装コミットより先に記録される
- **REDフェーズ確認**: T008でテストが失敗することを確認してから実装開始
- **GREENフェーズ確認**: T016ですべてのテストが合格することを確認
- **リグレッションテスト**: T017で既存68個のテストがすべて合格することを確認
- **Conventional Commits**: すべてのコミットメッセージは`fix:`, `test:`, `docs:`形式を使用
- **並列実行**: [P]マーク付きタスクは同時実行可能

---

## タスク完了基準

すべてのタスク（T001-T023）が完了し、以下の条件を満たすこと:

- ✅ Claude Codeで「Capabilities: tools」と表示される
- ✅ 107個のツールすべてが認識される
- ✅ 既存68個 + 新規4個 = 72個のテストすべて合格
- ✅ ESLint/Prettier警告なし
- ✅ README.mdにトラブルシューティングガイド記載
- ✅ quickstart.md検証手順すべてクリア
