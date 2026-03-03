# 実装計画: fast-sql SQLite最適化ラッパーライブラリ

**機能ID**: `SPEC-aa9bcffb` | **日付**: 2025-12-08 | **仕様**: [spec.md](./spec.md)
**入力**: `/specs/SPEC-aa9bcffb/spec.md`の機能仕様

## 概要

sql.jsをバックエンドとして使用し、PRAGMA最適化・PreparedStatementキャッシュ・トランザクション最適化により実用的なパフォーマンスを実現するラッパーライブラリ。better-sqlite3のネイティブビルドによる`npx`タイムアウト問題を解決しつつ、sql.js素より20%以上高速な動作を目指す。

## 技術コンテキスト

**言語/バージョン**: TypeScript 5.0+ / Node.js 18+
**主要依存関係**: sql.js (WASMベースSQLite)
**ストレージ**: SQLite (メモリDB / ファイルDB)
**テスト**: Node.js test runner (node:test)
**対象プラットフォーム**: Node.js (ESM)
**プロジェクトタイプ**: single (ライブラリ)
**パフォーマンス目標**:
- 50,000件バルク挿入: 100ms以下（sql.js素126msから20%改善）
- 同一SQL反復: 10μs以下（sql.js素21μsから50%改善）
- LIKE検索: 4ms以下（sql.js素5msから20%改善）
**制約**: WASMロード時間、メモリ使用量
**スケール/スコープ**: unity-cliのcodeIndex移行

## 憲章チェック

**シンプルさ**:
- プロジェクト数: 1 (packages/fast-sql) OK
- フレームワークを直接使用? sql.jsを直接ラップ、余計な抽象化なし OK
- 単一データモデル? Database/Statementのみ OK
- パターン回避? Repositoryパターン等は使用しない OK

**アーキテクチャ**:
- すべての機能をライブラリとして? OK fast-sqlは独立ライブラリ
- ライブラリリスト: fast-sql (SQLite最適化ラッパー)
- ライブラリごとのCLI: 該当なし（ライブラリのみ）
- ライブラリドキュメント: README.md + TypeScript型定義

**テスト (妥協不可)**:
- RED-GREEN-Refactorサイクルを強制? OK 必須
- Gitコミットはテストが実装より先に表示? OK 必須
- 順序: Contract→Integration→E2E→Unitを厳密に遵守? OK
- 実依存関係を使用? OK 実sql.jsを使用（モックなし）
- Integration testの対象: Database/Statementの動作 OK
- 禁止: テスト前の実装、REDフェーズのスキップ OK

**可観測性**:
- 構造化ロギング含む? OK キャッシュ統計API
- フロントエンドログ → バックエンド? N/A
- エラーコンテキスト十分? OK SQL文、パラメータを含む

**バージョニング**:
- バージョン番号割り当て済み? 0.1.0から開始
- 変更ごとにBUILDインクリメント? OK
- 破壊的変更を処理? 初期リリースのため該当なし

## プロジェクト構造

### ドキュメント (この機能)

```
specs/SPEC-aa9bcffb/
├── spec.md              # 機能仕様
├── plan.md              # このファイル
├── research.md          # Phase 0 出力
├── data-model.md        # Phase 1 出力
├── quickstart.md        # Phase 1 出力
├── contracts/           # Phase 1 出力
└── tasks.md             # Phase 2 出力 (/speckit.tasks)
```

### ソースコード

```
packages/fast-sql/
├── package.json
├── tsconfig.json
├── README.md
├── src/
│   ├── index.ts                    # エントリポイント (initFastSql)
│   ├── types.ts                    # TypeScript型定義
│   ├── core/
│   │   ├── Database.ts             # 最適化済みDatabaseクラス
│   │   ├── Statement.ts            # PreparedStatementラッパー
│   │   └── StatementCache.ts       # LRUキャッシュ機構
│   ├── optimizations/
│   │   ├── PragmaOptimizer.ts      # PRAGMA設定最適化
│   │   ├── TransactionManager.ts   # トランザクション管理
│   │   └── BatchProcessor.ts       # バルク操作最適化
│   ├── backend/
│   │   ├── BackendInterface.ts     # 抽象インターフェース
│   │   └── SqlJsBackend.ts         # sql.js実装
│   └── utils/
│       └── hash.ts                 # SQLハッシュ生成
└── tests/
    ├── contract/                   # 契約テスト
    ├── integration/                # 統合テスト
    └── unit/                       # ユニットテスト
```

## Phase 0: アウトライン＆リサーチ

### 解決済みの技術選択

1. **SQLiteバックエンド**: sql.js
   - 理由: WASMベースでビルド不要、Node.jsで実績あり
   - 代替案検討: wa-sqlite（より高速だがAPIが異なる）、libsql（未成熟）

2. **キャッシュアルゴリズム**: LRU (Least Recently Used)
   - 理由: シンプルで効果的、実装が容易
   - 代替案検討: LFU（実装が複雑）、ARC（メモリ効率は良いが複雑）

3. **PRAGMA設定**: メモリDB最適化
   - `journal_mode = memory`: ジャーナルをメモリに
   - `synchronous = off`: 同期書き込み無効
   - `cache_size = 10000`: キャッシュ拡大（~40MB）
   - `temp_store = memory`: 一時テーブルをメモリに

4. **API設計**: sql.js互換 + 拡張API
   - 理由: 移行コスト最小化、必要に応じて最適化API使用
   - 代替案検討: 完全独自API（移行コスト高）

### リサーチ結果

| 項目 | 決定 | 理由 |
|------|------|------|
| バックエンド | sql.js | ビルド不要、実績あり |
| キャッシュ | LRU (100エントリ, TTL 5分) | シンプル、効果的 |
| PRAGMA | メモリDB最適化セット | ベンチマーク実証済み |
| TypeScript | ESM出力 | Node.js 18+ターゲット |

**出力**: [research.md](./research.md)に詳細を記載

## Phase 1: 設計＆契約

### データモデル

#### Database エンティティ

```typescript
interface DatabaseOptions {
  pragma?: PragmaOptions;
  statementCache?: StatementCacheOptions;
}

interface PragmaOptions {
  journalMode?: 'memory' | 'wal' | 'delete';
  synchronous?: 'off' | 'normal' | 'full';
  cacheSize?: number;
  tempStore?: 'memory' | 'file';
}

interface StatementCacheOptions {
  maxSize?: number;  // デフォルト: 100
  ttlMs?: number;    // デフォルト: 300000 (5分)
}
```

#### Statement エンティティ

```typescript
interface Statement {
  bind(params?: BindParams): boolean;
  step(): boolean;
  get(params?: BindParams): Row | undefined;
  getAsObject(params?: BindParams): RowObject | undefined;
  run(params?: BindParams): void;
  reset(): void;
  free(): void;
  getColumnNames(): string[];
}
```

#### Backend インターフェース

```typescript
interface SqliteBackend {
  execSql(sql: string): void;
  prepare(sql: string): NativeStatement;
  exportDb(): Uint8Array;
  close(): void;
}
```

### API契約

#### sql.js互換API

```typescript
// 初期化
declare function initFastSql(): Promise<SqlJsStatic>;

interface SqlJsStatic {
  Database: typeof Database;
}

// Databaseクラス
class Database {
  constructor(data?: ArrayLike<number>);
  execSql(sql: string): QueryExecResult[];
  run(sql: string, params?: BindParams): void;
  prepare(sql: string): Statement;
  exportDb(): Uint8Array;
  close(): void;
}
```

#### 拡張API

```typescript
class Database {
  // sql.js互換APIに加えて...

  static create(data?: ArrayLike<number>, options?: DatabaseOptions): Promise<Database>;

  bulkInsert(sql: string, rows: BindParams[]): number;
  transaction<T>(fn: () => T): T;
  getCacheStats(): CacheStats;
}

interface CacheStats {
  hits: number;
  misses: number;
  size: number;
  evictions: number;
}
```

### 契約テスト設計

1. **sql.js互換性テスト** (`tests/contract/compatibility.test.ts`)
   - execSql()が同じ戻り値を返す
   - run()がパラメータを正しくバインド
   - prepare()がStatementを返す
   - exportDb()が有効なSQLiteバイナリを生成

2. **Statement互換性テスト** (`tests/contract/statement.test.ts`)
   - bind/step/get/runが期待通り動作
   - getAsObject()がオブジェクト形式で返す
   - reset()が状態をクリア

3. **拡張API契約テスト** (`tests/contract/extended.test.ts`)
   - bulkInsert()がトランザクション内で実行
   - transaction()がロールバックをサポート
   - getCacheStats()が統計を返す

**出力**: [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

## Phase 2: タスク計画アプローチ

**タスク生成戦略**:
- `/templates/tasks-template.md` をベースとして読み込み
- Phase 1設計ドキュメント (contracts, data model, quickstart) からタスクを生成
- 各contract → contract testタスク [P]
- 各entity → model作成タスク [P]
- 各ユーザーストーリー → integration testタスク
- テストを合格させる実装タスク

**カテゴリ別タスク概要**:

### Setup (3タスク)

- packages/fast-sql/ディレクトリ構造作成
- package.json/tsconfig.json設定
- テストインフラ設定

### Test First (8タスク) [P]

- sql.js互換性契約テスト
- Statement契約テスト
- 拡張API契約テスト
- StatementCacheユニットテスト
- PragmaOptimizerユニットテスト
- TransactionManagerユニットテスト
- パフォーマンスベンチマークテスト
- codeIndex移行統合テスト

### Core Implementation (8タスク)

- BackendInterface定義
- SqlJsBackend実装
- Statement実装
- StatementCache実装
- Database実装（sql.js互換API）
- PragmaOptimizer実装
- TransactionManager実装
- BatchProcessor実装

### Integration (3タスク)

- index.ts（initFastSql）実装
- 型定義（types.ts）完成
- codeIndex.js移行検証

### Polish (3タスク)

- README.md作成
- パフォーマンスベンチマーク実行・記録
- 最終テスト・リファクタリング

**推定出力**: tasks.mdに約25個の番号付き、順序付きタスク

**重要**: このフェーズは/speckit.tasksコマンドで実行、/speckit.planではない

## 複雑さトラッキング

| 違反 | 必要な理由 | より単純な代替案が却下された理由 |
|------|-----------|--------------------------------|
| なし | - | - |

## 進捗トラッキング

**フェーズステータス**:

- [x] Phase 0: Research完了 (/speckit.plan コマンド)
- [x] Phase 1: Design完了 (/speckit.plan コマンド)
- [x] Phase 2: Task planning完了 (/speckit.plan コマンド - アプローチのみ記述)
- [x] Phase 3: Tasks生成済み (/speckit.tasks コマンド)
- [ ] Phase 4: 実装完了
- [ ] Phase 5: 検証合格

**ゲートステータス**:

- [x] 初期憲章チェック: 合格
- [x] 設計後憲章チェック: 合格
- [x] すべての要明確化解決済み
- [x] 複雑さの逸脱を文書化済み (なし)

---

*憲章 v1.0.0 に基づく - `/docs/constitution.md` 参照*
