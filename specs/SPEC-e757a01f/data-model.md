# データモデル - C# LSP統合機能（コードインデックス）

**機能ID**: SPEC-e757a01f | **日付**: 2025-10-17 | **更新**: 2025-11-26

## エンティティ定義

### Symbol (既存)

C#コード内のシンボル（クラス、メソッド、フィールド等）を表す。

**フィールド**:

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `name` | string | ✓ | シンボル名 |
| `kind` | enum | ✓ | 種別: `class`/`method`/`field`/`property`等 |
| `filePath` | string | ✓ | ファイルパス |
| `line` | number | ✓ | 行番号 |
| `column` | number | ✓ | 列番号 |
| `container` | string | - | 親コンテナ名 |

---

### BuildJob (バックグラウンドビルド用)

バックグラウンドで実行中または完了したインデックスビルドジョブを表す。

**フィールド**:

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `id` | string | ✓ | ジョブID（形式: `build-<timestamp>-<random>`または`watcher-<timestamp>`） |
| `status` | enum | ✓ | ジョブ状態: `'running'` \| `'completed'` \| `'failed'` |
| `progress` | object | ✓ | 進捗情報 |
| `progress.processed` | number | ✓ | 処理済みファイル数 |
| `progress.total` | number | ✓ | 全ファイル数 |
| `progress.rate` | number | ✓ | 処理速度（files/sec） |
| `result` | object \| null | - | 完了時の結果（`status='completed'`の場合のみ） |
| `result.updatedFiles` | number | ✓ | 更新されたファイル数 |
| `result.removedFiles` | number | ✓ | 削除されたファイル数 |
| `result.totalIndexedSymbols` | number | ✓ | 総シンボル数 |
| `result.lastIndexedAt` | string | ✓ | 最終更新時刻（ISO 8601） |
| `error` | string \| null | - | エラーメッセージ（`status='failed'`の場合のみ） |
| `startedAt` | string | ✓ | ジョブ開始時刻（ISO 8601） |
| `completedAt` | string \| null | - | ジョブ完了時刻（ISO 8601、`status='completed'`の場合のみ） |
| `failedAt` | string \| null | - | ジョブ失敗時刻（ISO 8601、`status='failed'`の場合のみ） |

**検証ルール**:

1. `status`は`'running'`、`'completed'`、`'failed'`のいずれかのみ
2. `progress.processed` ≤ `progress.total`
3. `progress.rate` ≥ 0
4. `status='completed'`の場合、`result`必須、`completedAt`必須
5. `status='failed'`の場合、`error`必須、`failedAt`必須
6. `status='running'`の場合、`result`、`error`、`completedAt`、`failedAt`はnull

**状態遷移**:

```
          success
running ---------> completed
   |
   | error
   +-------------> failed
```

**ジョブID命名規則**:

- **手動ビルド**: `build-<timestamp>-<random6chars>`
  - 例: `build-1730188800000-abc123`

- **自動ビルド（Watcher）**: `watcher-<timestamp>`
  - 例: `watcher-1730188800000`

**例**:

```javascript
// 実行中ジョブ
{
  id: 'build-1730188800000-abc123',
  status: 'running',
  progress: {
    processed: 1200,
    total: 1500,
    rate: 12.5
  },
  result: null,
  error: null,
  startedAt: '2025-10-29T10:00:00Z',
  completedAt: null,
  failedAt: null
}

// 完了ジョブ
{
  id: 'build-1730188800000-abc123',
  status: 'completed',
  progress: {
    processed: 1500,
    total: 1500,
    rate: 15.2
  },
  result: {
    updatedFiles: 50,
    removedFiles: 0,
    totalIndexedSymbols: 15500,
    lastIndexedAt: '2025-10-29T10:05:00Z'
  },
  error: null,
  startedAt: '2025-10-29T10:00:00Z',
  completedAt: '2025-10-29T10:05:00Z',
  failedAt: null
}
```

---

### IndexStats (既存)

プロジェクト全体のコードインデックス統計情報。

**フィールド**:

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `total` | number | ✓ | 総シンボル数 |
| `lastIndexedAt` | string | ✓ | 最終更新時刻（ISO 8601） |

---

### PrebuiltBinary (better-sqlite3配布用)

プラットフォーム別のprebuiltバイナリ情報。

**フィールド**:

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `platform` | enum | ✓ | OS: `linux`/`darwin`/`win32` |
| `arch` | enum | ✓ | CPU: `x64`/`arm64` |
| `nodeAbi` | number | ✓ | Node.js ABI番号 (115=18.x, 120=20.x, 131=22.x) |
| `filename` | string | ✓ | バイナリファイル名 |
| `checksum` | string | ✓ | SHA256ハッシュ |

**対応プラットフォーム**:

| OS | CPU | Node 18.x | Node 20.x | Node 22.x |
|----|-----|-----------|-----------|-----------|
| linux | x64 | ✓ | ✓ | ✓ |
| linux | arm64 | ✓ | ✓ | ✓ |
| darwin | x64 | ✓ | ✓ | ✓ |
| darwin | arm64 | ✓ | ✓ | ✓ |
| win32 | x64 | ✓ | ✓ | ✓ |

---

## エンティティ関係

```
BuildJob 1 ----completes----> 1 IndexStats
                              (result.totalIndexedSymbols → total)
                              (result.lastIndexedAt → lastIndexedAt)

PrebuiltBinary ----loads----> better-sqlite3
                              (platform+arch+nodeAbi → 使用バイナリ決定)
```

---

## ストレージ戦略

### BuildJob (メモリ内)

**ストレージ**: メモリ内Map (`jobManager.js`)

**ライフサイクル**:
1. ジョブ作成時: Mapに追加
2. 実行中: `progress`を随時更新
3. 完了/失敗時: 最終状態を保存
4. 完了5分後: Mapから削除（自動クリーンアップ）

### IndexStats / Symbol (SQLite)

**ストレージ**: SQLite（`.unity/cache/code-index/code-index.db`）

**テーブル**: `files`、`symbols`

### PrebuiltBinary (npm package)

**ストレージ**: npm package内の`prebuilt/`ディレクトリ

**構造**:
```
unity-cli/
├── prebuilt/
│   └── better-sqlite3/
│       ├── linux-x64/
│       │   └── better_sqlite3.node
│       ├── linux-arm64/
│       │   └── better_sqlite3.node
│       ├── darwin-x64/
│       │   └── better_sqlite3.node
│       ├── darwin-arm64/
│       │   └── better_sqlite3.node
│       └── win32-x64/
│           └── better_sqlite3.node
└── scripts/
    └── ensure-better-sqlite3.mjs
```

---

## 検証ルール詳細

### BuildJob検証

**TypeScript型定義** (参考):

```typescript
type JobStatus = 'running' | 'completed' | 'failed';

interface BuildJobProgress {
  processed: number;  // >= 0
  total: number;      // > 0
  rate: number;       // >= 0 (files/sec)
}

interface BuildJobResult {
  updatedFiles: number;
  removedFiles: number;
  totalIndexedSymbols: number;
  lastIndexedAt: string; // ISO 8601
}

interface BuildJob {
  id: string;           // match /^(build|watcher)-\d+-[a-z0-9]{6}$/
  status: JobStatus;
  progress: BuildJobProgress;
  result: BuildJobResult | null;
  error: string | null;
  startedAt: string;    // ISO 8601
  completedAt: string | null;
  failedAt: string | null;
}
```

---

## まとめ

- **シンボル関連**: Symbol, IndexStats（SQLite永続化）
- **ビルドジョブ**: BuildJob（メモリ内、一時的）
- **Prebuilt配布**: PrebuiltBinary（npm package同梱）
