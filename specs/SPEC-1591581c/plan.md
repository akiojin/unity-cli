# 実装計画: Unity Profilerパフォーマンス計測

**機能ID**: `SPEC-1591581c` | **日付**: 2025-01-17 | **仕様**: [spec.md](../../SPEC-1591581c/spec.md)
**入力**: `/specs/SPEC-1591581c/spec.md`の機能仕様

## 実行フロー (/speckit.plan コマンドのスコープ)
```
1. 入力パスから機能仕様を読み込み ✅
2. 技術コンテキストを記入 ✅
3. 憲章チェックセクションを評価 → 次のステップ
4. Phase 0 を実行 → research.md
5. Phase 1 を実行 → contracts, data-model.md, quickstart.md
6. 憲章チェックセクションを再評価
7. Phase 2 を計画 → タスク生成アプローチを記述
8. 停止 - /speckit.tasks コマンドの準備完了
```

## 概要

Unity Profilerパフォーマンス計測機能をMCPツールとして提供する。LLMがUnityアプリケーションの実行中にプロファイリングを開始/停止し、.dataファイルとして保存したり、リアルタイムメトリクス（CPU、メモリ、GC、描画コール数など）を取得できるようにする。既存のVideoCapture/Screenshot機能と同様のアーキテクチャで実装し、.unity/capture/配下に統一的に保存する。

**技術アプローチ**:
- Unity側: `UnityEditorInternal.ProfilerDriver`（.data保存）+ `Unity.Profiling.ProfilerRecorder`（リアルタイムメトリクス）
- Node側: BaseToolHandler継承の標準パターン
- 4つのMCPツール: `profiler_start`, `profiler_stop`, `profiler_status`, `profiler_get_metrics`

## 技術コンテキスト

**言語/バージョン**:
- Unity C# (Unity 2021.3 LTS以上)
- Node.js 18+ (unity-cli)

**主要依存関係**:
- Unity: `UnityEditorInternal.ProfilerDriver`, `Unity.Profiling.ProfilerRecorder`
- Node: `@modelcontextprotocol/sdk`, `tcp-jsonrpc-client`

**ストレージ**:
- ファイルシステム（`.unity/capture/*.data` および `.json`）

**テスト**:
- Node: Vitest (既存テストインフラ)
- Unity: NUnit (Unity Test Framework)

**対象プラットフォーム**:
- Unity Editor (エディタ専用機能)

**プロジェクトタイプ**:
- 既存プロジェクト拡張（Unity MCP Server）

**パフォーマンス目標**:
- リアルタイムメトリクス取得: <1秒
- プロファイリング開始/停止: <500ms
- 長時間記録（10分以上）でメモリリークなし

**制約**:
- Unity Editor環境でのみ動作（ビルド済みアプリ不可）
- 保存先は`.unity/capture/`固定
- 既存のVideoCaptureHandler/ScreenshotHandlerと同様のパターン遵守

**スケール/スコープ**:
- 4つの新規MCPツール
- Unity側1つのHandler（ProfilerHandler.cs）
- Node側4つのToolHandler

## 憲章チェック
*ゲート: Phase 0 research前に合格必須。Phase 1 design後に再チェック。*

**シンプルさ**:
- プロジェクト数: 既存プロジェクトに統合（追加なし）✅
- フレームワークを直接使用? Unity API直接使用、Node SDK直接使用 ✅
- 単一データモデル? セッション状態とメトリクスのみ ✅
- パターン回避? 既存のHandlerパターン踏襲（実証済み） ✅

**アーキテクチャ**:
- すべての機能をライブラリとして? MCP Serverアーキテクチャ遵守 ✅
- ライブラリリスト:
  - `unity-cli`: MCPツール公開
  - `unity-cli`: Unity Editor拡張
- ライブラリごとのCLI: 既存のMCP Server CLI ✅
- ライブラリドキュメント: 既存のREADME.md/CLAUDE.md拡張 ✅

**テスト (妥協不可)**:
- RED-GREEN-Refactorサイクルを強制? ✅ TDD必須
- Gitコミットはテストが実装より先に表示? ✅ コミット順序遵守
- 順序: Contract→Integration→E2E→Unitを厳密に遵守? ✅
- 実依存関係を使用? ✅ 実Unity接続使用
- Integration testの対象: 新しいProfilerHandler、MCPツール統合 ✅
- 禁止: テスト前の実装、REDフェーズのスキップ ✅

**可観測性**:
- 構造化ロギング含む? ✅ 既存のログインフラ使用
- エラーコンテキスト十分? ✅ エラーコード、メッセージ、推奨解決策

**バージョニング**:
- バージョン番号割り当て済み? 既存のsemantic-release使用 ✅
- 変更ごとにBUILDインクリメント? `npm version`コマンド使用 ✅
- 破壊的変更を処理? 新機能追加のみ（非破壊的） ✅

**憲章チェック結果**: ✅ 合格（違反なし）

## プロジェクト構造

### ドキュメント (この機能)
```
specs/SPEC-1591581c/
├── spec.md              # 機能仕様書 ✅
├── plan.md              # このファイル (進行中)
├── research.md          # Phase 0 出力
├── data-model.md        # Phase 1 出力
├── quickstart.md        # Phase 1 出力
├── contracts/           # Phase 1 出力
│   ├── profiler-start.json
│   ├── profiler-stop.json
│   ├── profiler-status.json
│   └── profiler-get-metrics.json
└── tasks.md             # Phase 2 出力 (/speckit.tasks)
```

### ソースコード (リポジトリルート)
```
unity-cli/
├── src/
│   └── handlers/
│       └── profiler/
│           ├── ProfilerStartToolHandler.js
│           ├── ProfilerStopToolHandler.js
│           ├── ProfilerStatusToolHandler.js
│           └── ProfilerGetMetricsToolHandler.js
└── tests/
    ├── integration/
    │   └── profiler.test.js
    └── unit/
        └── handlers/
            └── profiler/
                ├── ProfilerStartToolHandler.test.js
                ├── ProfilerStopToolHandler.test.js
                ├── ProfilerStatusToolHandler.test.js
                └── ProfilerGetMetricsToolHandler.test.js

UnityCliBridge/
└── Packages/
    └── unity-cli/
        └── Editor/
            └── Handlers/
                └── ProfilerHandler.cs
```

**構造決定**: 既存プロジェクト拡張のため、既存ディレクトリ構造に従う

## Phase 0: アウトライン＆リサーチ

### リサーチタスク

1. **ProfilerDriver API詳細調査**:
   - 決定: `ProfilerDriver.enabled`, `ProfilerDriver.SaveProfile()` を使用
   - 理由: Unity標準API、.data形式で保存可能
   - 代替案: カスタムプロファイリング実装 → 却下（車輪の再発明）

2. **ProfilerRecorder API詳細調査**:
   - 決定: `ProfilerRecorder.StartNew()` でメトリクス取得
   - 理由: Unity 2020.2以降の新API、リアルタイム取得に最適
   - 代替案: Profiler.GetRuntimeMemorySizeLong等の個別API → 却下（メトリクス種類が限定的）

3. **利用可能メトリクス一覧取得方法**:
   - 決定: `ProfilerRecorderHandle.GetAvailable()` を使用
   - 理由: すべての利用可能メトリクスを動的に取得可能
   - 代替案: ハードコードされたメトリクスリスト → 却下（Unityバージョン間で差異あり）

4. **EditorApplication.updateでの定期処理**:
   - 決定: VideoCapture同様にEditorApplication.updateで定期処理
   - 理由: 既存パターン、自動停止機能に必要
   - 代替案: コルーチン → 却下（EditorApplication.updateが標準）

5. **セッション状態管理**:
   - 決定: 静的フィールドでセッション状態保持
   - 理由: VideoCaptureHandlerと同じパターン、シンプル
   - 代替案: ScriptableObject → 却下（過剰設計）

**出力**: `research.md` にすべての技術決定を文書化

## Phase 1: 設計＆契約

### 1. データモデル (`data-model.md`)

**エンティティ:**

1. **ProfilerSession** (Unity側):
   ```
   - sessionId: string (GUID)
   - isRecording: bool
   - startedAt: DateTime
   - outputPath: string
   - maxDurationSec: double
   - recorders: Dictionary<string, ProfilerRecorder>
   ```

2. **ProfilerMetric** (Node/Unity共通):
   ```
   - category: string (CPU, Memory, Rendering, GC)
   - name: string (System Used Memory, Draw Calls Count)
   - value: long
   - unit: string (bytes, count, milliseconds)
   ```

3. **ProfilerStartRequest** (Node側):
   ```
   - mode: string (normal, deep) - デフォルト: normal
   - recordToFile: bool - デフォルト: true
   - metrics: string[] - 記録対象メトリクス（空=すべて）
   - maxDurationSec: number - 自動停止時間（0=無制限）
   ```

4. **ProfilerStartResponse** (Unity側):
   ```
   - sessionId: string
   - startedAt: string (ISO 8601)
   - isRecording: bool
   - outputPath: string | null
   ```

5. **ProfilerStopResponse** (Unity側):
   ```
   - sessionId: string
   - outputPath: string | null
   - duration: number (秒)
   - frameCount: number
   - metrics: ProfilerMetric[] | null
   ```

6. **ProfilerStatusResponse** (Unity側):
   ```
   - isRecording: bool
   - sessionId: string | null
   - startedAt: string | null
   - elapsedSec: number
   - remainingSec: number | null
   ```

7. **ProfilerMetricsResponse** (Unity側):
   ```
   - categories: {
       [categoryName]: {
         name: string,
         metrics: ProfilerMetric[]
       }
     }
   ```

### 2. API契約 (`/contracts/`)

**profiler-start.json** (MCPツール):
```json
{
  "name": "profiler_start",
  "description": "Start Unity Profiler recording session",
  "inputSchema": {
    "type": "object",
    "properties": {
      "mode": {
        "type": "string",
        "enum": ["normal", "deep"],
        "default": "normal",
        "description": "Profiling mode (normal or deep profiling)"
      },
      "recordToFile": {
        "type": "boolean",
        "default": true,
        "description": "Save profiling data to .data file"
      },
      "metrics": {
        "type": "array",
        "items": {"type": "string"},
        "description": "Specific metrics to record (empty = all)"
      },
      "maxDurationSec": {
        "type": "number",
        "minimum": 0,
        "description": "Auto-stop after N seconds (0 = unlimited)"
      }
    }
  }
}
```

**profiler-stop.json** (MCPツール):
```json
{
  "name": "profiler_stop",
  "description": "Stop Unity Profiler recording and save data",
  "inputSchema": {
    "type": "object",
    "properties": {
      "sessionId": {
        "type": "string",
        "description": "Optional session ID to stop (defaults to current)"
      }
    }
  }
}
```

**profiler-status.json** (MCPツール):
```json
{
  "name": "profiler_status",
  "description": "Get current profiling session status",
  "inputSchema": {
    "type": "object",
    "properties": {}
  }
}
```

**profiler-get-metrics.json** (MCPツール):
```json
{
  "name": "profiler_get_metrics",
  "description": "Get available profiler metrics or current values",
  "inputSchema": {
    "type": "object",
    "properties": {
      "listAvailable": {
        "type": "boolean",
        "default": false,
        "description": "Return list of available metrics instead of current values"
      },
      "metrics": {
        "type": "array",
        "items": {"type": "string"},
        "description": "Specific metrics to query (empty = all)"
      }
    }
  }
}
```

### 3. 契約テスト (TDD - RED phase)

**tests/integration/profiler.test.js**:
```javascript
describe('Profiler Integration Tests', () => {
  describe('profiler_start', () => {
    it('should start profiling and return session ID', async () => {
      // RED: この時点では実装なし、テスト失敗
      const result = await mcpClient.callTool('profiler_start', {
        mode: 'normal',
        recordToFile: true
      });

      expect(result).toHaveProperty('sessionId');
      expect(result).toHaveProperty('isRecording', true);
      expect(result).toHaveProperty('startedAt');
    });

    it('should reject duplicate start', async () => {
      await mcpClient.callTool('profiler_start', {});
      const result = await mcpClient.callTool('profiler_start', {});

      expect(result).toHaveProperty('error');
      expect(result.error).toContain('already running');
    });
  });

  describe('profiler_stop', () => {
    it('should stop profiling and save .data file', async () => {
      await mcpClient.callTool('profiler_start', {recordToFile: true});
      const result = await mcpClient.callTool('profiler_stop', {});

      expect(result).toHaveProperty('outputPath');
      expect(result.outputPath).toMatch(/\.data$/);
      expect(fs.existsSync(result.outputPath)).toBe(true);
    });
  });

  describe('profiler_status', () => {
    it('should return idle status when not recording', async () => {
      const result = await mcpClient.callTool('profiler_status', {});
      expect(result).toHaveProperty('isRecording', false);
    });

    it('should return active status when recording', async () => {
      await mcpClient.callTool('profiler_start', {});
      const result = await mcpClient.callTool('profiler_status', {});

      expect(result).toHaveProperty('isRecording', true);
      expect(result).toHaveProperty('sessionId');
      expect(result).toHaveProperty('elapsedSec');
    });
  });

  describe('profiler_get_metrics', () => {
    it('should return available metrics list', async () => {
      const result = await mcpClient.callTool('profiler_get_metrics', {
        listAvailable: true
      });

      expect(result).toHaveProperty('categories');
      expect(result.categories).toHaveProperty('Memory');
      expect(result.categories).toHaveProperty('Rendering');
    });

    it('should return current metric values', async () => {
      await mcpClient.callTool('profiler_start', {});
      const result = await mcpClient.callTool('profiler_get_metrics', {
        metrics: ['System Used Memory', 'Draw Calls Count']
      });

      expect(result.metrics).toHaveLength(2);
      expect(result.metrics[0]).toHaveProperty('name');
      expect(result.metrics[0]).toHaveProperty('value');
    });
  });
});
```

### 4. Quickstart (`quickstart.md`)

```markdown
# Quickstart: Unity Profilerパフォーマンス計測

## 前提条件
- Unity Editor起動中
- MCP Server接続済み

## 基本的な使い方

### 1. プロファイリング開始
\`\`\`bash
# LLMに指示
"パフォーマンス計測を開始してください"

# 内部的に実行されるMCPツール
profiler_start({
  mode: "normal",
  recordToFile: true
})
# → {sessionId: "abc123", isRecording: true, startedAt: "2025-01-17T10:00:00Z"}
\`\`\`

### 2. プロファイリング停止と保存
\`\`\`bash
# LLMに指示
"パフォーマンス計測を停止してください"

# 内部的に実行されるMCPツール
profiler_stop({})
# → {sessionId: "abc123", outputPath: ".unity/capture/profile_2025-01-17_10-05-00.data", duration: 300}
\`\`\`

### 3. .dataファイルをUnity Profiler Windowで開く
1. Unity Editor: Window > Analysis > Profiler
2. Profiler Window: Load > `.unity/capture/profile_2025-01-17_10-05-00.data`
3. CPU、メモリ、レンダリング等のグラフを時系列で確認

### 4. リアルタイムメトリクス取得
\`\`\`bash
# LLMに指示
"現在のメモリ使用量と描画コール数を教えてください"

# 内部的に実行されるMCPツール
profiler_get_metrics({
  metrics: ["System Used Memory", "Draw Calls Count"]
})
# → {metrics: [{name: "System Used Memory", value: 524288000, unit: "bytes"}, ...]}
\`\`\`

## 検証ステップ

### ユーザーストーリー1検証 (P1)
1. ✅ プロファイリング開始 → セッションID返却
2. ✅ プロファイリング停止 → .dataファイル保存
3. ✅ Unity Profiler Windowで.dataファイルを開く
4. ✅ 重複開始 → エラーメッセージ返却

### ユーザーストーリー2検証 (P2)
1. ✅ 利用可能メトリクス一覧取得
2. ✅ リアルタイムメトリクス取得（JSON形式）
3. ✅ 特定メトリクスのみ取得

### ユーザーストーリー3検証 (P3)
1. ✅ 非記録中の状態確認
2. ✅ 記録中の状態確認（セッションID、経過時間）
3. ✅ 残り時間の確認（maxDurationSec設定時）
\`\`\`

**出力**: data-model.md, /contracts/*, 失敗するテスト, quickstart.md

## Phase 2: タスク計画アプローチ
*このセクションは/speckit.tasksコマンドが実行することを記述*

**タスク生成戦略**:
1. **Setup Tasks**:
   - Unity ProfilerHandler.cs作成（空実装）
   - Node ProfilerToolHandler 4ファイル作成（空実装）
   - ハンドラ登録（unity-cli/src/handlers/index.js、UnityCliBridge.cs）

2. **Contract Test Tasks** [P]:
   - profiler_start contract test作成（RED）
   - profiler_stop contract test作成（RED）
   - profiler_status contract test作成（RED）
   - profiler_get_metrics contract test作成（RED）

3. **Core Implementation Tasks**:
   - Unity ProfilerHandler.Start実装（GREEN）
   - Unity ProfilerHandler.Stop実装（GREEN）
   - Unity ProfilerHandler.Status実装（GREEN）
   - Unity ProfilerHandler.GetAvailableMetrics実装（GREEN）
   - Node ProfilerStartToolHandler実装（GREEN）
   - Node ProfilerStopToolHandler実装（GREEN）
   - Node ProfilerStatusToolHandler実装（GREEN）
   - Node ProfilerGetMetricsToolHandler実装（GREEN）

4. **Integration Tasks**:
   - ワークスペースルート解決機能統合
   - EditorApplication.update定期処理実装
   - ProfilerRecorderによるメトリクス取得実装

5. **Polish Tasks**:
   - エラーハンドリング強化
   - ログ追加
   - README.md更新

**順序戦略**:
- TDD順序: Contract Tests → Implementation
- 並列実行: Contract Tests はすべて [P]
- 依存関係: Unity Handler → Node Handler

**推定出力**: tasks.mdに約25-30個のタスク

## Phase 3+: 今後の実装
*これらのフェーズは/planコマンドのスコープ外*

**Phase 3**: タスク実行 (/speckit.tasksコマンドがtasks.mdを作成)
**Phase 4**: 実装 (TDDサイクルでtasks.mdを実行)
**Phase 5**: 検証 (統合テスト実行、quickstart.md実行、.dataファイル検証)

## 複雑さトラッキング
*憲章チェックに正当化が必要な違反がある場合のみ記入*

| 違反 | 必要な理由 | より単純な代替案が却下された理由 |
|------|-----------|--------------------------------|
| なし | N/A | N/A |

## 進捗トラッキング
*このチェックリストは実行フロー中に更新される*

**フェーズステータス**:
- [ ] Phase 0: Research完了
- [ ] Phase 1: Design完了
- [ ] Phase 2: Task planning完了（アプローチのみ記述）
- [ ] Phase 3: Tasks生成済み (/speckit.tasks)
- [ ] Phase 4: 実装完了
- [ ] Phase 5: 検証合格

**ゲートステータス**:
- [x] 初期憲章チェック: 合格
- [ ] 設計後憲章チェック: 合格
- [ ] すべての要明確化解決済み
- [x] 複雑さの逸脱を文書化済み（違反なし）

---
*憲章 v1.0.0 に基づく - `/docs/constitution.md` 参照*
