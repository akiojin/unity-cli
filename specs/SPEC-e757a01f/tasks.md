# タスク: C# LSP統合機能（コードインデックス）

**機能ID**: `SPEC-e757a01f` | **ステータス**: 完了

**統合SPEC**:

- SPEC-aa705b2b: コードインデックスビルドのバックグラウンド実行（実装完了）
- SPEC-eb99c755: Prebuilt better-sqlite3 配布（実装完了）

## 実装状況サマリー

| ユーザーストーリー | ステータス | テストカバレッジ |
|-----------------|---------|--------------|
| US-1: シンボル検索 | ✅ 完了 | ユニット + E2E |
| US-2: 参照検索 | ✅ 完了 | ユニット + E2E |
| US-3: 構造化編集 | ✅ 完了 | ユニット |
| US-4: リネーム | ✅ 完了 | ユニット |
| US-5: シンボル定義取得 | ✅ 完了 | ユニット |
| US-6: コードインデックス管理 | ✅ 完了 | ユニット |
| US-7: 軽量スニペット編集 | ✅ 完了 | ユニット (9/9) |
| US-8: バックグラウンドビルド | ✅ 完了 | ユニット + 統合 (20/20) |
| US-9: 初回起動高速化 | ✅ 完了 | ユニット (11) + 統合 (5) |
| US-10: LSPプロセス分離 | ✅ 完了 | ユニット |
| US-11: LSPバージョン切替 | ✅ 完了 | ユニット |
| US-12: サーバー識別 | ✅ 完了 | ユニット |

---

## US-1〜US-6: 基本機能（完了済み）

これらの機能は初期実装で完了しています。

### テストファイル

- `tests/unit/handlers/script/ScriptSymbolFindToolHandler.test.js`
- `tests/unit/handlers/script/ScriptSymbolsGetToolHandler.test.js`
- `tests/unit/handlers/script/ScriptRefsFindToolHandler.test.js`
- `tests/unit/handlers/script/ScriptEditStructuredToolHandler.test.js`
- `tests/unit/handlers/script/ScriptRefactorRenameToolHandler.test.js`
- `tests/unit/handlers/script/CodeIndexStatusToolHandler.test.js`
- `tests/unit/handlers/script/CodeIndexUpdateToolHandler.test.js`

---

## US-2: 参照検索ページング（完了）

### Phase 3: テスト ✅

- [x] T-201: `find_refs` の cursor/startAfter ページング動作（ユニット）

**テストファイル**: `tests/unit/handlers/script/ScriptRefsFindToolHandler.test.js`

---

## US-7: 軽量スニペット編集（完了済み）

### Phase 0: リサーチ ✅

- [x] R-001: Roslyn LSP側で構文診断を得るリクエストを特定
  - 結果: `LspRpcClient.validateText()` (mcp/validateTextEdits) が利用可能
- [x] R-002: 複数テキストEditを適用するAPI確認
  - 結果: 専用API不要、テキストレベルの順次適用で十分
- [x] R-003: searchのアンカー解決への再利用可能性
  - 結果: search不要、indexOfで十分
- [x] R-004: フォーマッタ呼び出しの必要性評価
  - 結果: 構文診断のみで十分、フォーマッタ不要

### Phase 1-2: 設計・実装 ✅ スキップ（実装済み）

Phase 0リサーチにてScriptEditSnippetToolHandler.jsが既に実装済みと判明。

### Phase 3: テスト ✅

- [x] T-001: 複数ガード削除のプレビュー生成
- [x] T-002: 80文字制限の拒否
- [x] T-003: 括弧不整合時のロールバック
- [x] T-004: アンカー一意性検証（複数マッチ時エラー）
- [x] T-005: replace操作
- [x] T-006: insert操作（position=after/before）
- [x] T-007: バッチ編集（3件の順次適用）
- [x] T-008: Applyモード（ファイル書き込み）

**テストファイル**: `tests/unit/handlers/script/ScriptEditSnippetToolHandler.test.js`
**テスト結果**: 9/9 成功

### Phase 4: ドキュメント ✅

- [x] DOC-001: CLAUDE.md更新（edit_snippet使用ガイドライン）
- [x] DOC-002: 使用例追加
- [x] DOC-003: トラブルシューティング追加

---

## US-8: バックグラウンドインデックスビルド（完了済み）

**統合元**: SPEC-aa705b2b

### Phase 0: リサーチ ✅

- [x] R-005: Node.jsバックグラウンドジョブパターン
  - 決定: オブジェクト参照による進捗共有
- [x] R-006: メモリ内ジョブ管理 vs 永続化
  - 決定: メモリ内Map（永続化なし）
- [x] R-007: IndexWatcherとの統合
  - 決定: JobManagerでの一元管理 + runningフラグ保持
- [x] R-008: 既存ハンドラの拡張パターン
  - 決定: 下位互換性完全維持

### Phase 3.1: セットアップ ✅

- [x] T001: ESLint/Prettier設定確認
- [x] T002: テスト環境確認
- [x] T003: jobManager.js配置場所確認 (`src/core/jobManager.js`)

### Phase 3.2: テストファースト (TDD) ✅

**Contract Tests**:

- [x] T004: `tests/unit/core/jobManager.test.js` - JobManager contract tests (13テスト)
- [x] T005: `tests/unit/handlers/script/CodeIndexBuildToolHandler.test.js` - バックグラウンド実行契約
- [x] T006: `tests/unit/handlers/script/CodeIndexStatusToolHandler.test.js` - buildJob拡張契約

**Integration Tests**:

- [x] T007: `tests/integration/code-index-background.test.js` - US-1: 非ブロッキングビルド
- [x] T008: 同上 - US-2: 進捗状況の可視化
- [x] T009: 同上 - US-3: 重複実行の防止

### Phase 3.3: コア実装 ✅

- [x] T010: `src/core/jobManager.js` - JobManagerクラス実装 (164行)
- [x] T011: `src/handlers/script/CodeIndexBuildToolHandler.js` - バックグラウンド化
- [x] T012: `src/handlers/script/CodeIndexStatusToolHandler.js` - buildJob拡張
- [x] T013: `src/core/indexWatcher.js` - JobManager統合

### Phase 3.4: 統合 ✅

- [x] T014: IndexWatcher統合E2Eテスト (4シナリオ)
- [x] T015: エラーハンドリング改善

### Phase 3.5: 仕上げ ✅

- [x] T016: Unit testsカバレッジ確認 (80%以上)
- [x] T017: パフォーマンステスト
- [x] T018: ドキュメント更新
- [x] T019: コードクリーンアップ
- [x] T020: 最終検証

**全体進捗**: 20/20タスク完了 ✅
**実装完了日**: 2025-10-29

### テスト結果サマリー

- ✅ JobManager: 13/13 contract tests合格
- ✅ Integration tests: US-1, US-2, US-3すべてカバー
- ✅ IndexWatcher E2E: 4つの統合シナリオテスト
- ✅ パフォーマンス: build_index < 1秒、get_index_status < 100ms

### 実装ファイル

- `src/core/jobManager.js` (新規作成)
- `src/handlers/script/CodeIndexBuildToolHandler.js` (変更)
- `src/handlers/script/CodeIndexStatusToolHandler.js` (変更)
- `src/core/indexWatcher.js` (変更)

---

## US-9: 初回起動高速化（完了）

**統合元**: SPEC-eb99c755

### Phase 0: リサーチ ✅

- [x] R-009: Prebuildツールチェーン
  - 決定: npm package内にprebuiltを同梱、postinstallで展開

### Phase 3.1: セットアップ ✅

- [x] **T101** [P] prebuilt/better-sqlite3/ディレクトリ構造作成
- [x] **T102** [P] prebuildifyツール評価・選定
- [x] **T103** [P] GitHub Actions用ビルドマトリクス設計

### Phase 3.2: テストファースト (TDD)

**Contract Tests**:

- [x] **T104** [P] `tests/unit/scripts/ensure-better-sqlite3.test.js` - postinstallスクリプト契約
  - 契約: プラットフォーム検出 (linux/darwin/win32 × x64/arm64)
  - 契約: Node ABIバージョン検出 (18.x=115, 20.x=120, 22.x=131)
  - 契約: 同梱バイナリ優先展開
  - 契約: WASMフォールバック
  - **結果**: 6/6テスト成功

- [x] **T105** [P] `tests/integration/prebuilt-sqlite.test.js` - 初回起動時間テスト
  - シナリオ: クリーンインストール → 30秒以内に起動完了
  - シナリオ: 未対応プラットフォーム → WASMフォールバック成功
  - **結果**: 1/1テスト成功

### Phase 3.3: コア実装 ✅

- [x] **T106** `unity-cli/scripts/ensure-better-sqlite3.mjs` - postinstallスクリプト実装
  - 実装: プラットフォーム・アーキテクチャ検出
  - 実装: prebuilt/better-sqlite3/からバイナリコピー
  - 実装: 環境変数制御 (UNITY_CLI_SKIP_NATIVE_BUILD, UNITY_CLI_FORCE_NATIVE)
  - 実装: WASMフォールバック

- [x] **T107** [P] `.github/workflows/prebuild.yml` - CI用ビルドワークフロー
  - 実装: ビルドマトリクス (linux/darwin/win32 × x64/arm64 × Node18/20/22)
  - 実装: クロスコンパイル対応（Linux arm64）
  - 実装: prebuilt/better-sqlite3/へのアーティファクト保存・マニフェスト生成

- [x] **T108** `unity-cli/package.json` - postinstall設定
  - 変更: `"postinstall": "node scripts/ensure-better-sqlite3.mjs"`
  - 変更: prebuilt/をnpm publishに含める
  - 現状: `postinstall` は `node scripts/ensure-better-sqlite3.mjs`

### Phase 3.4: 統合

- [ ] **T109** CI統合テスト
  - テスト: 各プラットフォームでのインストール検証
  - テスト: postinstall < 5秒
  - **結果**: 未実施

- [ ] **T110** npm publish検証
  - テスト: パッケージサイズ < 50MB
  - テスト: prebuilt/better-sqlite3/が正しく含まれている
  - **結果**: ローカル `npm pack --workspace unity-cli` で一覧/サイズ確認（package size 167.4 kB）。prebuilt/better-sqlite3 の実バイナリはCIで要検証。

### Phase 3.5: 仕上げ

- [x] **T111** [P] ドキュメント更新
  - 更新: README.mdにインストール手順追加
  - 更新: トラブルシューティング（WASMフォールバック）

- [ ] **T112** 最終検証
  - 実行: クリーンインストールテスト
  - 確認: 30秒タイムアウト内に完了
  - 確認: 全対応プラットフォームで動作
  - **結果**: ローカルで `npm cache clean --force` 後に `npx --yes @akiojin/unity-cli@latest --help` を実行。計測 1.69 秒で完了。全プラットフォームの検証はCIで要実施。

---

## 依存関係

### US-9タスクの順序制約

```
Setup (T101-T103)
    ↓
Tests (T104-T105) ← すべて並列実行可能
    ↓
Core (T106-T108)
  T106 (postinstall) ← T104の合格が条件
  T107 (CI workflow) ← 並列実行可能
  T108 (package.json) ← T106完了が条件
    ↓
Integration (T109-T110) ← T106-T108完了が条件
    ↓
Polish (T111-T112) ← T109-T110完了が条件
```

---

## TDDカバレッジ分析

### テストファイル一覧

| カテゴリ | ファイル | テスト数 |
|---------|---------|---------|
| Core | `tests/unit/core/jobManager.test.js` | 15 |
| Core | `tests/unit/core/codeIndex.test.js` | 3 |
| Core | `tests/unit/core/codeIndexDb.test.js` | 2 |
| Core | `tests/unit/core/indexWatcher.test.js` | 6 |
| Handler | `tests/unit/handlers/script/*.test.js` | 14ファイル |
| Integration | `tests/integration/code-index-background.test.js` | 15 |
| Scripts | `tests/unit/scripts/ensure-better-sqlite3.test.js` | 11 |
| Integration | `tests/integration/prebuilt-sqlite.test.js` | 5 |

### カバレッジ状況

- **US-1〜US-7**: ユニットテストでカバー
- **US-8**: ユニット + 統合テストで完全カバー
- **US-9**: ユニット(11) + 統合(5)テストでカバー（CI統合テストはprebuild.yml実行待ち）

---

## US-10: LSPプロセス分離（完了済み）

### Phase 3: テスト ✅

- [x] T-301: 目的別のLSPインスタンスが分離されること
- [x] T-302: symbols/rename/edit_structured/remove_symbol/update_index が専用LSPを使用すること
- [x] T-303: C# LSP並列実行のロック契約（file/write/limiter）を検証すること

**テストファイル**:
- `tests/unit/lsp/LspIsolationRouting.test.js`
- `lsp/ConcurrencyTests.cs`

---

## US-11: LSPバージョン切替（完了済み）

### Phase 3: テスト ✅

- [x] T-401: 実行中LSPのバージョンと希望バージョンが異なる場合は再起動すること

**テストファイル**:
- `tests/unit/lsp/LspProcessManager.test.js`

---

## US-12: サーバー識別（完了済み）

### Phase 3: テスト ✅

- [x] T-501: get_server_info で pid/projectRoot/workspaceRoot を取得できること
- [x] T-502: projectRoot 不一致時は tools/call がエラーを返すこと（requireClientRoot 有効時）

**テストファイル**:
- `tests/unit/handlers/system/SystemGetServerInfoToolHandler.test.js`
- `tests/unit/core/startServer.test.js`

---

## 参考

- `spec.md`: 機能要件とユーザーストーリー
- `plan.md`: 技術設計とアーキテクチャ
- `research.md`: リサーチ結果
- `data-model.md`: エンティティ定義
- `contracts/`: API契約定義

---

*本ドキュメントは2025-10-24に作成され、2025-11-26にSPEC統合に伴い更新されました*
