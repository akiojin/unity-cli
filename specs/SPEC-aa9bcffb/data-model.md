# データモデル: fast-sql

**機能ID**: `SPEC-aa9bcffb`
**日付**: 2025-12-08

## エンティティ一覧

### 1. Database

SQLiteデータベースインスタンスを表す主要エンティティ。

```typescript
interface Database {
  // 内部状態
  readonly closed: boolean;

  // sql.js互換メソッド
  execSql(sql: string): QueryExecResult[];
  run(sql: string, params?: BindParams): void;
  prepare(sql: string): Statement;
  exportDb(): Uint8Array;
  close(): void;

  // 拡張メソッド
  bulkInsert(sql: string, rows: BindParams[]): number;
  transaction<T>(fn: () => T): T;
  getCacheStats(): CacheStats;
}

interface DatabaseOptions {
  pragma?: PragmaOptions;
  statementCache?: StatementCacheOptions;
}
```

**状態遷移**:

```
[未初期化] --create()--> [オープン] --close()--> [クローズ]
                              |
                              +-- 各操作 --> [オープン]
```

### 2. Statement

PreparedStatementを表すエンティティ。

```typescript
interface Statement {
  // 状態
  readonly sql: string;

  // sql.js互換メソッド
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

**状態遷移**:

```
[未バインド] --bind()--> [バインド済み] --step()--> [実行中]
     ^                        |                      |
     |                        v                      v
     +------reset()----------[完了]<------step()----+
                               |
                               +--free()--> [解放済み]
```

### 3. StatementCache

PreparedStatementのキャッシュを管理するエンティティ。

```typescript
interface StatementCache {
  get(sql: string): Statement | undefined;
  set(sql: string, stmt: Statement): void;
  delete(sql: string): boolean;
  clear(): void;
  getStats(): CacheStats;
}

interface StatementCacheOptions {
  maxSize: number;   // デフォルト: 100
  ttlMs: number;     // デフォルト: 300000 (5分)
}

interface CacheStats {
  hits: number;
  misses: number;
  size: number;
  evictions: number;
}
```

### 4. Backend (インターフェース)

SQLiteバックエンドの抽象化。

```typescript
interface SqliteBackend {
  execSql(sql: string): QueryExecResult[];
  prepare(sql: string): NativeStatement;
  exportDb(): Uint8Array;
  close(): void;
}

interface NativeStatement {
  bind(params?: BindParams): boolean;
  step(): boolean;
  get(): Row | undefined;
  getAsObject(): RowObject | undefined;
  run(params?: BindParams): void;
  reset(): void;
  free(): boolean;
  getColumnNames(): string[];
}
```

## 型定義

### 共通型

```typescript
// バインドパラメータ
type BindParams = Record<string, SqlValue> | SqlValue[];

// SQL値
type SqlValue = string | number | Uint8Array | null;

// クエリ結果行
type Row = SqlValue[];

// オブジェクト形式の結果行
type RowObject = Record<string, SqlValue>;

// クエリ実行結果
interface QueryExecResult {
  columns: string[];
  values: Row[];
}
```

### PRAGMA設定

```typescript
interface PragmaOptions {
  journalMode?: 'memory' | 'wal' | 'delete' | 'truncate' | 'persist' | 'off';
  synchronous?: 'off' | 'normal' | 'full' | 'extra';
  cacheSize?: number;
  tempStore?: 'default' | 'file' | 'memory';
  pageSize?: number;
  mmapSize?: number;
}
```

## 関係図

```
┌──────────────────────────────────────────────────────┐
│                      Database                         │
│  ┌─────────────────┐  ┌─────────────────────────────┐│
│  │ StatementCache  │  │    PragmaOptions            ││
│  │  - maxSize      │  │    - journalMode            ││
│  │  - ttlMs        │  │    - synchronous            ││
│  │  - entries      │  │    - cacheSize              ││
│  └────────┬────────┘  └─────────────────────────────┘│
│           │                                          │
│           v                                          │
│  ┌─────────────────┐                                 │
│  │   Statement     │ * (キャッシュされた複数のStmt)   │
│  │  - sql          │                                 │
│  │  - native       │                                 │
│  └────────┬────────┘                                 │
│           │                                          │
│           v                                          │
│  ┌─────────────────────────────────────────────────┐ │
│  │              SqliteBackend                       │ │
│  │  (SqlJsBackend / WaSqliteBackend / etc.)        │ │
│  └─────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────┘
```

## 検証ルール

### Database

- `closed`が`true`の場合、すべての操作は`Error`をスロー
- `sql`パラメータは空文字列不可
- `exportDb()`は有効なSQLiteバイナリ（マジックバイト検証）を返す

### Statement

- `free()`後の操作は`Error`をスロー
- `bind()`のパラメータ数はプレースホルダー数と一致必須
- `step()`は結果がある場合`true`、ない場合`false`を返す

### StatementCache

- `maxSize`は1以上
- `ttlMs`は0以上（0=TTL無効）
- キャッシュ満杯時はLRUでeviction

## デフォルト値

| 設定 | デフォルト値 | 説明 |
|------|-------------|------|
| `pragma.journalMode` | `'memory'` | メモリDB最適化 |
| `pragma.synchronous` | `'off'` | 最大パフォーマンス |
| `pragma.cacheSize` | `10000` | 約40MB |
| `pragma.tempStore` | `'memory'` | 一時テーブルをメモリに |
| `statementCache.maxSize` | `100` | 100エントリ |
| `statementCache.ttlMs` | `300000` | 5分 |
