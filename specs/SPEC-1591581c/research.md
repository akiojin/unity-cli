# Research: Unity Profilerパフォーマンス計測

**機能ID**: `SPEC-1591581c` | **日付**: 2025-01-17

## 技術調査結果

### 1. ProfilerDriver API詳細調査

**決定**: `UnityEditorInternal.ProfilerDriver`を使用して.dataファイル保存を実現

**理由**:
- Unity標準APIでエディタ専用のプロファイリング制御が可能
- `.data`形式で保存することでUnity Profiler Windowで直接開ける
- `ProfilerDriver.enabled`で記録開始/停止をシンプルに制御
- `ProfilerDriver.SaveProfile(string path)`で指定パスに保存

**主要API**:
```csharp
using UnityEditorInternal;

// 記録開始
ProfilerDriver.ClearAllFrames();
ProfilerDriver.enabled = true;

// 記録停止と保存
ProfilerDriver.enabled = false;
string outputPath = ".unity/capture/profile_timestamp.data";
ProfilerDriver.SaveProfile(outputPath);
```

**検討した代替案**:
- カスタムプロファイリング実装 → 却下理由: 車輪の再発明、Unity標準形式との互換性なし
- Profiler.BeginSample/EndSample → 却下理由: 手動計装が必要、既存コードへの侵入的変更

**参考**: [Unity Documentation - ProfilerDriver](https://docs.unity3d.com/ScriptReference/UnityEditorInternal.ProfilerDriver.html)

---

### 2. ProfilerRecorder API詳細調査

**決定**: `Unity.Profiling.ProfilerRecorder`を使用してリアルタイムメトリクス取得

**理由**:
- Unity 2020.2以降の新しいProfiler APIで、リアルタイムメトリクス取得に最適化
- `ProfilerRecorder.StartNew()`で特定のメトリクスを記録開始
- `ProfilerRecorder.LastValue`で最新の値を即座に取得可能
- オーバーヘッドが小さく、長時間実行に適している

**主要API**:
```csharp
using Unity.Profiling;

// メトリクス記録開始
var memoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
var drawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");

// 値取得
long memoryUsed = memoryRecorder.LastValue; // bytes
long drawCalls = drawCallsRecorder.LastValue; // count

// 記録停止
memoryRecorder.Dispose();
drawCallsRecorder.Dispose();
```

**検討した代替案**:
- `Profiler.GetRuntimeMemorySizeLong()`等の個別API → 却下理由: メトリクス種類が限定的（メモリのみ）
- `Profiler.GetTotalAllocatedMemoryLong()`等の旧API → 却下理由: ProfilerRecorderの方が統一的で拡張性が高い

**参考**: [Unity Documentation - ProfilerRecorder](https://docs.unity3d.com/ScriptReference/Unity.Profiling.ProfilerRecorder.html)

---

### 3. 利用可能メトリクス一覧取得方法

**決定**: `ProfilerRecorderHandle.GetAvailable()`を使用して動的にメトリクス一覧取得

**理由**:
- すべての利用可能なProfilerメトリクスを実行時に取得可能
- Unityバージョン間の差異を吸収（ハードコードされたリスト不要）
- カテゴリ別に分類して返却可能（Memory、Rendering、GC等）

**主要API**:
```csharp
using Unity.Profiling;

// すべての利用可能メトリクスを取得
var handles = ProfilerRecorderHandle.GetAvailable();

foreach (var handle in handles)
{
    var description = ProfilerRecorderHandle.GetDescription(handle);
    Debug.Log($"{description.Category}: {description.Name}");
}
```

**主要メトリクス例**:
- **Memory**: System Used Memory, GC Reserved Memory, Texture Memory, Mesh Memory
- **Rendering**: SetPass Calls Count, Draw Calls Count, Vertices Count, Triangles Count
- **CPU/Frame**: Main Thread Frame Time, Render Thread Frame Time
- **GC**: GC Allocated In Frame

**検討した代替案**:
- ハードコードされたメトリクスリスト → 却下理由: Unityバージョン間で差異があり、メンテナンスコストが高い

**参考**: [Unity Documentation - ProfilerRecorderHandle](https://docs.unity3d.com/ScriptReference/Unity.Profiling.ProfilerRecorderHandle.html)

---

### 4. EditorApplication.updateでの定期処理

**決定**: `EditorApplication.update`イベントを使用して定期処理を実装

**理由**:
- VideoCaptureHandlerで既に使用されている実証済みパターン
- 自動停止機能（maxDurationSec）の実装に必要
- リアルタイムメトリクスのサンプリングに使用可能
- Unity Editorのメインスレッドで実行され、安全

**実装パターン**:
```csharp
using UnityEditor;

private static bool s_IsRecording = false;
private static DateTime s_StartedAt;
private static double s_MaxDurationSec = 0;

public static object Start(JObject parameters)
{
    s_MaxDurationSec = parameters["maxDurationSec"]?.ToObject<double>() ?? 0;

    // 定期処理登録
    EditorApplication.update += OnUpdate;

    s_IsRecording = true;
    s_StartedAt = DateTime.UtcNow;

    return new { sessionId = Guid.NewGuid().ToString("N"), isRecording = true };
}

private static void OnUpdate()
{
    if (!s_IsRecording) return;

    var elapsed = (DateTime.UtcNow - s_StartedAt).TotalSeconds;

    // 自動停止チェック
    if (s_MaxDurationSec > 0 && elapsed >= s_MaxDurationSec)
    {
        Stop(new JObject());
    }
}

public static object Stop(JObject parameters)
{
    // 定期処理解除
    EditorApplication.update -= OnUpdate;

    s_IsRecording = false;

    return new { sessionId = "...", duration = (DateTime.UtcNow - s_StartedAt).TotalSeconds };
}
```

**検討した代替案**:
- コルーチン → 却下理由: EditorApplicationでコルーチンは非標準、EditorApplication.updateの方が一般的
- async/await → 却下理由: 定期処理には不向き、EditorApplication.updateの方がシンプル

**参考**: [Unity Documentation - EditorApplication.update](https://docs.unity3d.com/ScriptReference/EditorApplication-update.html)

---

### 5. セッション状態管理

**決定**: 静的フィールドでセッション状態を保持

**理由**:
- VideoCaptureHandlerと同じパターンでシンプル
- Unityエディタのドメインリロード間で状態をリセット（意図した挙動）
- プロファイリングはエディタセッション内で完結する一時的な操作

**状態管理フィールド**:
```csharp
public static class ProfilerHandler
{
    private static bool s_IsRecording = false;
    private static string s_SessionId = null;
    private static DateTime s_StartedAt;
    private static string s_OutputPath = null;
    private static double s_MaxDurationSec = 0;
    private static Dictionary<string, ProfilerRecorder> s_Recorders = new Dictionary<string, ProfilerRecorder>();
}
```

**検討した代替案**:
- ScriptableObject → 却下理由: 過剰設計、ファイルI/Oが不要
- EditorPrefs → 却下理由: セッション永続化は不要、エディタ再起動後は状態リセットが正しい挙動

**参考**: VideoCaptureHandler.csの実装パターン

---

## ベストプラクティス

### 1. ワークスペースルート解決

既存の`ResolveWorkspaceRoot()`ユーティリティを使用:
```csharp
private static string ResolveWorkspaceRoot(string projectRoot)
{
    string dir = projectRoot;
    for (int i = 0; i < 3 && !string.IsNullOrEmpty(dir); i++)
    {
        if (Directory.Exists(Path.Combine(dir, ".unity")))
        {
            return dir;
        }
        var parent = Directory.GetParent(dir);
        if (parent == null) break;
        dir = parent.FullName;
    }
    return projectRoot;
}
```

### 2. エラーハンドリング

VideoCaptureHandlerと同様のエラーハンドリングパターン:
```csharp
try
{
    if (s_IsRecording)
    {
        return new { error = "A profiling session is already running.", sessionId = s_SessionId };
    }

    // 実装...

    return new { sessionId = Guid.NewGuid().ToString("N"), isRecording = true };
}
catch (Exception ex)
{
    Debug.LogError($"[ProfilerHandler] Start error: {ex.Message}");
    return new { error = $"Failed to start profiling: {ex.Message}", code = "E_UNKNOWN" };
}
```

### 3. ファイル命名規則

既存パターンに従った命名:
```csharp
string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
string outputPath = Path.Combine(workspaceRoot, ".unity", "capture", $"profile_{timestamp}.data");
```

---

## 統合ポイント

### Node側ハンドラ

既存のBaseToolHandlerパターンを踏襲:
```javascript
export class ProfilerStartToolHandler extends BaseToolHandler {
  constructor(unityConnection) {
    super('profiler_start', 'Start Unity Profiler recording', {
      type: 'object',
      properties: {
        mode: { type: 'string', enum: ['normal', 'deep'], default: 'normal' },
        recordToFile: { type: 'boolean', default: true },
        metrics: { type: 'array', items: { type: 'string' } },
        maxDurationSec: { type: 'number', minimum: 0 }
      }
    });
    this.unityConnection = unityConnection;
  }

  async execute(params) {
    const { WORKSPACE_ROOT } = await import('../../core/config.js');
    return await this.unityConnection.sendCommand('profiler_start', {
      ...params,
      workspaceRoot: WORKSPACE_ROOT
    });
  }
}
```

### Unity側コマンドルーティング

UnityCliBridge.csに追加:
```csharp
case "profiler_start":
    var pStart = ProfilerHandler.Start(command.Parameters);
    response = Response.SuccessResult(command.Id, pStart);
    break;
case "profiler_stop":
    var pStop = ProfilerHandler.Stop(command.Parameters);
    response = Response.SuccessResult(command.Id, pStop);
    break;
case "profiler_status":
    var pStatus = ProfilerHandler.Status(command.Parameters);
    response = Response.SuccessResult(command.Id, pStatus);
    break;
case "profiler_get_metrics":
    var pMetrics = ProfilerHandler.GetAvailableMetrics(command.Parameters);
    response = Response.SuccessResult(command.Id, pMetrics);
    break;
```

---

## 結論

すべての技術的不明点を解決し、既存のVideoCaptureHandler/ScreenshotHandlerと同様のアーキテクチャで実装可能であることを確認しました。Unity標準APIを使用することで、シンプルかつ保守性の高い実装が可能です。

**次のステップ**: Phase 1（設計＆契約）に進む準備完了
