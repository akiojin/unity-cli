# タスク: fast-sql SQLite最適化ラッパーライブラリ

**機能ID**: `SPEC-aa9bcffb`
**入力**: `/specs/SPEC-aa9bcffb/`の設計ドキュメント
**前提条件**: plan.md, research.md, data-model.md, quickstart.md

## フォーマット: `[ID] [P?] 説明`

- **[P]**: 並列実行可能 (異なるファイル、依存関係なし)
- 説明には正確なファイルパスを含める

---

## Phase 3.1: セットアップ

- [x] T001 `packages/fast-sql/` ディレクトリ構造を作成
  - `src/core/`, `src/optimizations/`, `src/backend/`, `src/utils/`
  - `tests/contract/`, `tests/integration/`, `tests/unit/`

- [x] T002 `packages/fast-sql/package.json` を作成
  - name: `@akiojin/fast-sql`
  - dependencies: `sql.js`
  - devDependencies: テストツール
  - type: `module`
  - exports: ESM設定

- [x] T003 [P] `packages/fast-sql/tsconfig.json` を作成
  - target: ES2022
  - module: ESNext
  - moduleResolution: node

- [x] T004 [P] npm workspaceにfast-sqlを追加
  - ルート `package.json` の workspaces に追加

---

## Phase 3.2: テストファースト (TDD) ⚠️ 3.3の前に完了必須

**重要: これらのテストは記述され、実装前に失敗する必要がある**

### sql.js互換性契約テスト

- [x] T005 [P] `packages/fast-sql/tests/contract/database.test.js` にDatabase契約テスト
  - `new SQL.Database()` コンストラクタの動作
  - `execSql(sql)` が QueryExecResult[] を返す
  - `run(sql, params)` がパラメータをバインドして実行
  - `close()` がデータベースを閉じる

- [x] T006 [P] `packages/fast-sql/tests/contract/statement.test.js` にStatement契約テスト
  - `prepare(sql)` が Statement を返す
  - `bind(params)` がパラメータをバインド
  - `step()` が行があれば true を返す
  - `get()` が Row を返す
  - `getAsObject()` が RowObject を返す
  - `reset()` が状態をリセット
  - `free()` がステートメントを解放

- [x] T007 [P] `packages/fast-sql/tests/contract/export.test.js` にエクスポート契約テスト
  - `exportDb()` が有効なSQLiteバイナリ (Uint8Array) を返す
  - マジックバイト検証 (`SQLite format 3\0`)

### 拡張API契約テスト

- [x] T008 [P] `packages/fast-sql/tests/contract/extended.test.js` に拡張API契約テスト
  - `Database.create(data, options)` が Promise<Database> を返す
  - `bulkInsert(sql, rows)` が挿入件数を返す
  - `transaction(fn)` がロールバックをサポート
  - `getCacheStats()` が CacheStats を返す

### 統合テスト

- [x] T009 [P] `packages/fast-sql/tests/integration/compatibility.test.js` にsql.js互換性統合テスト
  - テーブル作成 → 挿入 → クエリ → 削除のフルサイクル
  - PreparedStatement使用パターン
  - トランザクション内での複数操作

- [x] T010 [P] `packages/fast-sql/tests/integration/migration.test.js` に移行シナリオテスト
  - sql.jsパターンからの移行
  - better-sqlite3パターンからの移行

### パフォーマンステスト

- [x] T011 [P] `packages/fast-sql/tests/integration/benchmark.test.js` にベンチマークテスト
  - 50,000件バルク挿入: 100ms以下（sql.js素126msから20%改善）
  - 同一SQL反復1000回: 10μs以下（sql.js素21μsから50%改善）
  - LIKE検索: 4ms以下（sql.js素5msから20%改善）

---

## Phase 3.3: コア実装 (テストが失敗した後のみ)

### 型定義

- [ ] T012 `packages/fast-sql/src/types.ts` に型定義を作成
  - `SqlValue`, `BindParams`, `Row`, `RowObject`
  - `QueryExecResult`, `CacheStats`
  - `DatabaseOptions`, `PragmaOptions`, `StatementCacheOptions`

### バックエンド抽象化

- [ ] T013 `packages/fast-sql/src/backend/BackendInterface.ts` にインターフェース定義
  - `SqliteBackend` インターフェース
  - `NativeStatement` インターフェース

- [ ] T014 `packages/fast-sql/src/backend/SqlJsBackend.ts` にsql.js実装
  - sql.jsのラップ
  - `execSql()`, `prepare()`, `exportDb()`, `close()` 実装

### ステートメント

- [ ] T015 `packages/fast-sql/src/core/Statement.ts` にStatementクラス実装
  - sql.js互換API: `bind()`, `step()`, `get()`, `getAsObject()`, `run()`, `reset()`, `free()`
  - `getColumnNames()` 実装
  - 状態管理（未バインド → バインド済み → 実行中 → 完了 → 解放済み）

### キャッシュ

- [ ] T016 `packages/fast-sql/src/core/StatementCache.ts` にLRUキャッシュ実装
  - maxSize: 100エントリ
  - ttlMs: 300000 (5分)
  - `get()`, `set()`, `delete()`, `clear()`, `getStats()`
  - LRU eviction

### 最適化

- [ ] T017 [P] `packages/fast-sql/src/optimizations/PragmaOptimizer.ts` にPRAGMA最適化実装
  - `journalMode`, `synchronous`, `cacheSize`, `tempStore` 設定
  - デフォルト値適用

- [ ] T018 [P] `packages/fast-sql/src/optimizations/TransactionManager.ts` にトランザクション管理実装
  - `BEGIN IMMEDIATE` 自動ラップ
  - ネストされたトランザクション (SAVEPOINT)
  - エラー時の自動ロールバック

- [ ] T019 [P] `packages/fast-sql/src/optimizations/BatchProcessor.ts` にバッチ処理実装
  - `bulkInsert()` 最適化
  - 1000件ごとのコミット

### データベース

- [ ] T020 `packages/fast-sql/src/core/Database.ts` にDatabaseクラス実装
  - sql.js互換API: `execSql()`, `run()`, `prepare()`, `exportDb()`, `close()`
  - 拡張API: `create()`, `bulkInsert()`, `transaction()`, `getCacheStats()`
  - StatementCache統合
  - PragmaOptimizer統合
  - TransactionManager統合

### ユーティリティ

- [ ] T021 [P] `packages/fast-sql/src/utils/hash.ts` にSQLハッシュ生成実装
  - キャッシュキー用の高速ハッシュ

---

## Phase 3.4: 統合

- [ ] T022 `packages/fast-sql/src/index.ts` にエントリポイント実装
  - `initFastSql()` 関数（sql.js互換初期化）
  - `Database` クラスエクスポート
  - 型定義エクスポート

- [ ] T023 sql.js WASMローダー統合
  - sql.jsの非同期初期化
  - WASMバイナリのロード

---

## Phase 3.5: 仕上げ

### ユニットテスト

- [x] T024 [P] `packages/fast-sql/tests/unit/StatementCache.test.js` にキャッシュユニットテスト（新規）
  - LRU動作検証
  - TTL検証
  - eviction検証
  - 結果: 3/3テスト成功

- [x] T025 [P] `packages/fast-sql/tests/unit/PragmaOptimizer.test.js` にPRAGMAユニットテスト（新規）
  - デフォルト値検証
  - カスタム値検証
  - 結果: 2/2テスト成功

- [x] T026 [P] `packages/fast-sql/tests/unit/TransactionManager.test.js` にトランザクションユニットテスト（新規）
  - コミット検証
  - ロールバック検証
  - ネスト検証
  - 結果: 4/4テスト成功

### ドキュメント

- [ ] T027 [P] `packages/fast-sql/README.md` を作成
  - インストール手順
  - 基本的な使い方
  - sql.jsからの移行ガイド
  - API リファレンス

### 最終検証

- [ ] T028 パフォーマンスベンチマーク実行・結果記録
  - sql.js素との比較
  - 目標達成確認

- [ ] T029 codeIndex.js移行検証
  - 既存パターンの互換性確認
  - パフォーマンス劣化がないことを確認

---

## 依存関係

```
T001 → T002 → T003, T004 (Setup完了後にTests開始可能)

Tests (T005-T011) は並列実行可能 [P]
  ↓
T012 (型定義) → T013-T021 (実装)
  ↓
T013 → T014 (BackendInterface → SqlJsBackend)
T014 → T15, T20 (SqlJsBackend → Statement, Database)
T016 → T20 (StatementCache → Database)
T17, T18, T19 → T20 (最適化 → Database)
  ↓
T20, T21 → T22 (Database, hash → index.ts)
T22 → T23 (index.ts → WASMローダー)
  ↓
T24-T27 は並列実行可能 [P]
  ↓
T28 → T29 (ベンチマーク → 移行検証)
```

## 並列実行例

```bash
# Phase 3.2 契約テスト (すべて並列)
Task: "packages/fast-sql/tests/contract/database.test.js にDatabase契約テスト"
Task: "packages/fast-sql/tests/contract/statement.test.js にStatement契約テスト"
Task: "packages/fast-sql/tests/contract/export.test.js にエクスポート契約テスト"
Task: "packages/fast-sql/tests/contract/extended.test.js に拡張API契約テスト"
Task: "packages/fast-sql/tests/integration/compatibility.test.js にsql.js互換性統合テスト"
Task: "packages/fast-sql/tests/integration/migration.test.js に移行シナリオテスト"
Task: "packages/fast-sql/tests/integration/benchmark.test.js にベンチマークテスト"

# Phase 3.3 最適化 (並列)
Task: "packages/fast-sql/src/optimizations/PragmaOptimizer.ts"
Task: "packages/fast-sql/src/optimizations/TransactionManager.ts"
Task: "packages/fast-sql/src/optimizations/BatchProcessor.ts"
Task: "packages/fast-sql/src/utils/hash.ts"

# Phase 3.5 仕上げ (並列)
Task: "packages/fast-sql/tests/unit/StatementCache.test.js"
Task: "packages/fast-sql/tests/unit/PragmaOptimizer.test.js"
Task: "packages/fast-sql/tests/unit/TransactionManager.test.js"
Task: "packages/fast-sql/README.md"
```

## 検証チェックリスト

- [x] すべてのcontractsに対応するテストがある (T005-T008)
- [x] すべてのentitiesにmodelタスクがある (Database: T020, Statement: T015, StatementCache: T016)
- [x] すべてのテストが実装より先にある (T005-T011 → T012-T023)
- [x] 並列タスクは本当に独立している
- [x] 各タスクは正確なファイルパスを指定
- [x] 同じファイルを変更する[P]タスクがない

## 注意事項

- [P] タスク = 異なるファイル、依存関係なし
- 実装前にテストが失敗することを確認
- 各タスク後にコミット
- 回避: 曖昧なタスク、同じファイルの競合
- **TDD必須**: RED → GREEN → REFACTOR サイクルを厳守

---

*憲章 v1.0.0 に基づく - `/docs/constitution.md` 参照*
