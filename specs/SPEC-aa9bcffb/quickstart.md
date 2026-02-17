# クイックスタート: fast-sql

**機能ID**: `SPEC-aa9bcffb`
**日付**: 2025-12-08

## インストール

```bash
npm install @akiojin/fast-sql
```

## 基本的な使い方

### sql.js互換モード

既存のsql.jsコードからの移行が簡単:

```typescript
import initFastSql from '@akiojin/fast-sql';

// 初期化
const SQL = await initFastSql();
const db = new SQL.Database();

// テーブル作成
db.execSql(`
  CREATE TABLE users (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    email TEXT UNIQUE
  )
`);

// データ挿入
db.run('INSERT INTO users (name, email) VALUES (?, ?)', ['Alice', 'alice@example.com']);
db.run('INSERT INTO users (name, email) VALUES (?, ?)', ['Bob', 'bob@example.com']);

// クエリ実行
const results = db.execSql('SELECT * FROM users');
console.log(results);
// [{ columns: ['id', 'name', 'email'], values: [[1, 'Alice', 'alice@example.com'], [2, 'Bob', 'bob@example.com']] }]

// PreparedStatement使用
const stmt = db.prepare('SELECT * FROM users WHERE id = ?');
stmt.bind([1]);
while (stmt.step()) {
  console.log(stmt.getAsObject()); // { id: 1, name: 'Alice', email: 'alice@example.com' }
}
stmt.free();

// データベースエクスポート
const data = db.exportDb();
// data: Uint8Array (SQLiteバイナリ)

// クローズ
db.close();
```

### 拡張API（最適化機能）

パフォーマンスを最大化する場合:

```typescript
import { Database } from '@akiojin/fast-sql';

// カスタムオプションで作成
const db = await Database.create(undefined, {
  pragma: {
    journalMode: 'memory',
    synchronous: 'off',
    cacheSize: 20000  // 約80MB
  },
  statementCache: {
    maxSize: 200,
    ttlMs: 600000  // 10分
  }
});

// バルク挿入（自動トランザクション）
const rows = Array.from({ length: 50000 }, (_, i) => [i, `User ${i}`, `user${i}@example.com`]);
const inserted = db.bulkInsert('INSERT INTO users (id, name, email) VALUES (?, ?, ?)', rows);
console.log(`Inserted ${inserted} rows`);

// トランザクション
db.transaction(() => {
  db.run('UPDATE users SET name = ? WHERE id = ?', ['Updated', 1]);
  db.run('DELETE FROM users WHERE id = ?', [2]);
  // エラーが発生した場合、自動ロールバック
});

// キャッシュ統計
const stats = db.getCacheStats();
console.log(stats);
// { hits: 150, misses: 10, size: 5, evictions: 0 }
```

## 既存コードからの移行

### sql.jsからの移行

```diff
- import initSqlJs from 'sql.js';
+ import initFastSql from '@akiojin/fast-sql';

- const SQL = await initSqlJs();
+ const SQL = await initFastSql();

// 以下は変更不要
const db = new SQL.Database();
db.execSql('CREATE TABLE ...');
```

### better-sqlite3からの移行

```diff
- import Database from 'better-sqlite3';
+ import { Database } from '@akiojin/fast-sql';

- const db = new Database(':memory:');
+ const db = await Database.create();

// APIの違い
- db.runSql('CREATE TABLE ...');  // better-sqlite3風
+ db.execSql('CREATE TABLE ...');  // fast-sql

- const stmt = db.prepare('SELECT * FROM users');
- const rows = stmt.all();
+ const stmt = db.prepare('SELECT * FROM users');
+ const rows = [];
+ while (stmt.step()) {
+   rows.push(stmt.getAsObject());
+ }
+ stmt.free();

// または直接execSqlを使用
+ const results = db.execSql('SELECT * FROM users');
+ const rows = results[0]?.values || [];
```

## パフォーマンスのヒント

### 1. PreparedStatementを再利用

```typescript
// 悪い例: 毎回prepare
for (const id of ids) {
  const stmt = db.prepare('SELECT * FROM users WHERE id = ?');
  stmt.bind([id]);
  // ...
  stmt.free();
}

// 良い例: prepareを1回だけ（キャッシュされる）
const stmt = db.prepare('SELECT * FROM users WHERE id = ?');
for (const id of ids) {
  stmt.bind([id]);
  while (stmt.step()) {
    // ...
  }
  stmt.reset();
}
stmt.free();
```

### 2. バルク操作にはbulkInsertを使用

```typescript
// 悪い例: 1件ずつ挿入
for (const row of rows) {
  db.run('INSERT INTO users VALUES (?, ?, ?)', row);
}

// 良い例: バルク挿入
db.bulkInsert('INSERT INTO users VALUES (?, ?, ?)', rows);
```

### 3. 複数操作はトランザクションで

```typescript
// 悪い例: 暗黙の自動コミット
db.run('UPDATE users SET active = 0');
db.run('INSERT INTO audit_log VALUES (...)');

// 良い例: 明示的トランザクション
db.transaction(() => {
  db.run('UPDATE users SET active = 0');
  db.run('INSERT INTO audit_log VALUES (...)');
});
```

## トラブルシューティング

### WASMロードエラー

```
Error: Could not load sql.js WASM binary
```

**解決策**: sql.jsのWASMファイルが正しく配置されているか確認してください。

### メモリ不足

```
Error: Out of memory
```

**解決策**: `cacheSize`を小さくするか、大きなデータセットを分割処理してください。

### Statementが解放済み

```
Error: Statement has been freed
```

**解決策**: `free()`後にStatementを使用しないでください。新しくprepareしてください。
