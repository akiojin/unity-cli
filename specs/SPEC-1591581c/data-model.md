# Data Model: Unity Profilerパフォーマンス計測

**機能ID**: `SPEC-1591581c` | **日付**: 2025-01-17

## エンティティ概要

本機能では3つの主要エンティティを定義します：
1. **ProfilerSession**: プロファイリングセッションの状態管理
2. **ProfilerMetric**: 個別のパフォーマンスメトリクス
3. **ProfilerMetricsCategory**: メトリクスのカテゴリ分類

## 1. ProfilerSession (Unity側)

プロファイリングセッションの状態を保持するエンティティ。

### フィールド

| フィールド名 | 型 | 説明 | 検証ルール |
|------------|---|------|----------|
| sessionId | string | セッションの一意識別子（GUID形式） | 必須、32文字のhex文字列 |
| isRecording | bool | 記録中かどうか | 必須 |
| startedAt | DateTime | 記録開始時刻（UTC） | 必須 |
| outputPath | string | 保存先ファイルパス（.data） | recordToFile=trueの場合必須 |
| maxDurationSec | double | 自動停止時間（秒、0=無制限） | ≥0 |
| recorders | Dictionary<string, ProfilerRecorder> | アクティブなProfilerRecorderインスタンス | 内部使用のみ |

### 状態遷移

```
[Idle] --Start--> [Recording] --Stop--> [Completed]
                      |
                      +--Auto Stop (maxDurationSec)--> [Completed]
```

### 検証ルール

- `sessionId`: GUID形式（ハイフンなし、32文字hex）
- `startedAt`: 過去の時刻でなければならない
- `maxDurationSec`: 0以上（0は無制限）
- `outputPath`: `.unity/capture/`配下、`.data`拡張子

### C#実装例

```csharp
public class ProfilerSession
{
    public string SessionId { get; set; }
    public bool IsRecording { get; set; }
    public DateTime StartedAt { get; set; }
    public string OutputPath { get; set; }
    public double MaxDurationSec { get; set; }
    public Dictionary<string, ProfilerRecorder> Recorders { get; set; }

    public double ElapsedSeconds => (DateTime.UtcNow - StartedAt).TotalSeconds;
    public double? RemainingSeconds => MaxDurationSec > 0 ? Math.Max(0, MaxDurationSec - ElapsedSeconds) : null;
}
```

---

## 2. ProfilerMetric (Node/Unity共通)

個別のパフォーマンスメトリクスを表すエンティティ。

### フィールド

| フィールド名 | 型 | 説明 | 検証ルール |
|------------|---|------|----------|
| category | string | カテゴリ名（Memory、Rendering、GC等） | 必須、非空文字列 |
| name | string | メトリクス名（System Used Memory等） | 必須、非空文字列 |
| value | long | メトリクス値 | 必須 |
| unit | string | 単位（bytes、count、milliseconds等） | 必須、非空文字列 |

### 主要カテゴリとメトリクス

**Memory**:
- System Used Memory (bytes)
- GC Reserved Memory (bytes)
- Texture Memory (bytes)
- Mesh Memory (bytes)

**Rendering**:
- SetPass Calls Count (count)
- Draw Calls Count (count)
- Vertices Count (count)
- Triangles Count (count)

**CPU/Frame**:
- Main Thread Frame Time (milliseconds)
- Render Thread Frame Time (milliseconds)

**GC**:
- GC Allocated In Frame (bytes)

### 検証ルール

- `category`: ProfilerCategoryの有効な値
- `name`: ProfilerRecorderHandle.GetAvailable()で取得可能な名前
- `value`: 負数不可（ただし一部メトリクスでは0可）
- `unit`: 標準単位（bytes, count, milliseconds, seconds, percentage）

### JSON形式例

```json
{
  "category": "Memory",
  "name": "System Used Memory",
  "value": 524288000,
  "unit": "bytes"
}
```

---

## 3. ProfilerMetricsCategory (Unity側)

メトリクスをカテゴリ別に分類して返却するためのエンティティ。

### フィールド

| フィールド名 | 型 | 説明 | 検証ルール |
|------------|---|------|----------|
| name | string | カテゴリ名 | 必須、非空文字列 |
| metrics | ProfilerMetric[] | カテゴリ内のメトリクスリスト | 必須、空配列可 |

### JSON形式例

```json
{
  "categories": {
    "Memory": {
      "name": "Memory",
      "metrics": [
        {"category": "Memory", "name": "System Used Memory", "value": 524288000, "unit": "bytes"},
        {"category": "Memory", "name": "GC Reserved Memory", "value": 104857600, "unit": "bytes"}
      ]
    },
    "Rendering": {
      "name": "Rendering",
      "metrics": [
        {"category": "Rendering", "name": "Draw Calls Count", "value": 128, "unit": "count"}
      ]
    }
  }
}
```

---

## リクエスト/レスポンスモデル

### ProfilerStartRequest (Node → Unity)

```typescript
interface ProfilerStartRequest {
  mode?: 'normal' | 'deep';           // デフォルト: 'normal'
  recordToFile?: boolean;              // デフォルト: true
  metrics?: string[];                  // 空=すべて
  maxDurationSec?: number;             // デフォルト: 0 (無制限)
  workspaceRoot: string;               // 自動付与
}
```

**検証ルール**:
- `mode`: 'normal' または 'deep'のみ
- `recordToFile`: boolean
- `metrics`: 有効なメトリクス名の配列（ProfilerRecorderHandle.GetAvailable()で検証）
- `maxDurationSec`: 0以上

### ProfilerStartResponse (Unity → Node)

```typescript
interface ProfilerStartResponse {
  sessionId: string;
  startedAt: string;                   // ISO 8601形式
  isRecording: boolean;
  outputPath: string | null;           // recordToFile=falseの場合null
}
```

**検証ルール**:
- `sessionId`: GUID形式
- `startedAt`: ISO 8601形式（例: "2025-01-17T10:00:00.000Z"）
- `isRecording`: 常にtrue（成功時）
- `outputPath`: recordToFile=trueの場合は非null、.data拡張子

### ProfilerStopRequest (Node → Unity)

```typescript
interface ProfilerStopRequest {
  sessionId?: string;                  // 省略時は現在のセッション
  workspaceRoot: string;               // 自動付与
}
```

**検証ルール**:
- `sessionId`: 存在する場合は有効なGUID形式

### ProfilerStopResponse (Unity → Node)

```typescript
interface ProfilerStopResponse {
  sessionId: string;
  outputPath: string | null;
  duration: number;                    // 秒
  frameCount: number;
  metrics: ProfilerMetric[] | null;    // recordToFile=falseの場合のみ返却
}
```

**検証ルール**:
- `sessionId`: GUID形式
- `outputPath`: recordToFile=trueの場合は非null
- `duration`: 0以上
- `frameCount`: 0以上
- `metrics`: recordToFile=falseの場合のみ配列、それ以外はnull

### ProfilerStatusRequest (Node → Unity)

```typescript
interface ProfilerStatusRequest {
  // パラメータなし
}
```

### ProfilerStatusResponse (Unity → Node)

```typescript
interface ProfilerStatusResponse {
  isRecording: boolean;
  sessionId: string | null;
  startedAt: string | null;            // ISO 8601形式
  elapsedSec: number;
  remainingSec: number | null;         // maxDurationSec=0の場合null
}
```

**検証ルール**:
- `isRecording`: 記録中はtrue、それ以外はfalse
- `sessionId`: 記録中の場合は非null、GUID形式
- `startedAt`: 記録中の場合は非null、ISO 8601形式
- `elapsedSec`: 0以上
- `remainingSec`: maxDurationSec>0の場合は0以上、それ以外はnull

### ProfilerGetMetricsRequest (Node → Unity)

```typescript
interface ProfilerGetMetricsRequest {
  listAvailable?: boolean;             // デフォルト: false
  metrics?: string[];                  // 空=すべて
  workspaceRoot: string;               // 自動付与
}
```

**検証ルール**:
- `listAvailable`: boolean
- `metrics`: 有効なメトリクス名の配列

### ProfilerGetMetricsResponse (Unity → Node)

**listAvailable=trueの場合**:
```typescript
interface ProfilerGetMetricsResponse {
  categories: {
    [categoryName: string]: {
      name: string;
      metrics: ProfilerMetric[];
    }
  }
}
```

**listAvailable=falseの場合**:
```typescript
interface ProfilerGetMetricsResponse {
  metrics: ProfilerMetric[];
}
```

---

## エラーレスポンス

すべてのリクエストで以下の形式のエラーレスポンスを返却可能：

```typescript
interface ErrorResponse {
  error: string;                       // エラーメッセージ
  code?: string;                       // エラーコード（例: "E_ALREADY_RUNNING"）
  sessionId?: string;                  // 既存セッションID（重複開始時）
}
```

**エラーコード一覧**:
- `E_ALREADY_RUNNING`: 既にプロファイリング実行中
- `E_NOT_RECORDING`: プロファイリング未実行
- `E_INVALID_MODE`: 無効なmode指定
- `E_INVALID_METRICS`: 無効なメトリクス名
- `E_FILE_IO`: ファイル保存エラー
- `E_UNKNOWN`: その他のエラー

---

## 関係図

```
ProfilerSession (1) --- (*) ProfilerMetric
     |
     +--- (1) ProfilerMetricsCategory
                   |
                   +--- (*) ProfilerMetric
```

- ProfilerSessionは複数のProfilerMetricを記録
- ProfilerMetricsCategoryは複数のProfilerMetricをカテゴリ別にグループ化

---

## 永続化

- **ProfilerSession**: 静的フィールドで一時保持（エディタセッション内のみ）
- **ProfilerMetric**: メモリ内のみ（リアルタイム取得）
- **.dataファイル**: Unity標準形式で永続化（Unity Profiler Windowで開ける）

---

## 結論

すべてのエンティティは既存の機能仕様の要件を満たし、Unity標準APIと整合性があります。シンプルで拡張性のあるデータモデルです。
