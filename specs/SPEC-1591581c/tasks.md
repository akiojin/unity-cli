# タスク: Unity Profilerパフォーマンス計測

**機能ID**: `SPEC-1591581c` | **入力**: `/specs/SPEC-1591581c/`の設計ドキュメント
**前提条件**: plan.md, research.md, data-model.md, contracts/, quickstart.md

## 実行フロー概要

```
1. Setup (T001-T003): プロジェクト構造作成、ハンドラ登録
2. Contract Tests (T004-T007): 4つのMCPツールのcontract tests作成 [RED]
3. Core Implementation (T008-T015): Unity Handler + Node Handlers実装 [GREEN]
4. Integration (T016-T018): EditorUpdate、メトリクス取得、ワークスペース解決
5. Polish (T019-T023): Unit tests、エラーハンドリング、ドキュメント
```

**TDD厳守**: Contract Tests (T004-T007) がすべて失敗することを確認してから Core Implementation (T008-T015) を開始

---

## Phase 3.1: セットアップ

### T001 Unity ProfilerHandler.cs作成（空実装）

**ファイル**: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ProfilerHandler.cs`

**タスク**:
- ProfilerHandler.csファイルを作成
- BaseHandlerを継承
- 4つの空メソッドを定義:
  - `Start(string mode, bool recordToFile, string[] metrics, double maxDurationSec)`
  - `Stop(string sessionId)`
  - `GetStatus()`
  - `GetAvailableMetrics(bool listAvailable, string[] metrics)`
- コンパイルエラーがないことを確認

**検証**:
- Unity Editorでコンパイルエラーが発生しない
- ProfilerHandlerクラスがBaseHandlerを継承している

**依存関係**: なし

---

### T002 [P] Node ProfilerStartToolHandler.js作成（空実装）

**ファイル**: `unity-cli/src/handlers/profiler/ProfilerStartToolHandler.js`

**タスク**:
- ProfilerStartToolHandler.jsファイルを作成
- BaseToolHandlerを継承
- `handle(args)`メソッドを空実装
- `contracts/profiler-start.json`をスキーマとして参照

**検証**:
- `npm run lint --workspace=unity-cli`がエラーなく完了
- BaseToolHandlerを正しく継承している

**依存関係**: なし [P]

---

### T003 [P] Node ProfilerStopToolHandler.js作成（空実装）

**ファイル**: `unity-cli/src/handlers/profiler/ProfilerStopToolHandler.js`

**タスク**:
- ProfilerStopToolHandler.jsファイルを作成
- BaseToolHandlerを継承
- `handle(args)`メソッドを空実装
- `contracts/profiler-stop.json`をスキーマとして参照

**検証**:
- `npm run lint --workspace=unity-cli`がエラーなく完了
- BaseToolHandlerを正しく継承している

**依存関係**: なし [P]

---

### T004 [P] Node ProfilerStatusToolHandler.js作成（空実装）

**ファイル**: `unity-cli/src/handlers/profiler/ProfilerStatusToolHandler.js`

**タスク**:
- ProfilerStatusToolHandler.jsファイルを作成
- BaseToolHandlerを継承
- `handle(args)`メソッドを空実装
- `contracts/profiler-status.json`をスキーマとして参照

**検証**:
- `npm run lint --workspace=unity-cli`がエラーなく完了
- BaseToolHandlerを正しく継承している

**依存関係**: なし [P]

---

### T005 [P] Node ProfilerGetMetricsToolHandler.js作成（空実装）

**ファイル**: `unity-cli/src/handlers/profiler/ProfilerGetMetricsToolHandler.js`

**タスク**:
- ProfilerGetMetricsToolHandler.jsファイルを作成
- BaseToolHandlerを継承
- `handle(args)`メソッドを空実装
- `contracts/profiler-get-metrics.json`をスキーマとして参照

**検証**:
- `npm run lint --workspace=unity-cli`がエラーなく完了
- BaseToolHandlerを正しく継承している

**依存関係**: なし [P]

---

### T006 ハンドラ登録（Node側）

**ファイル**: `unity-cli/src/handlers/index.js`

**タスク**:
- ProfilerStartToolHandler, ProfilerStopToolHandler, ProfilerStatusToolHandler, ProfilerGetMetricsToolHandlerをインポート
- 4つのhandlerをMCPサーバーに登録
- 既存のVideoCapture/Screenshotハンドラと同じパターンで実装

**検証**:
- MCPサーバー起動時に4つのツールが登録される
- `profiler_start`, `profiler_stop`, `profiler_status`, `profiler_get_metrics`がツールリストに表示される

**依存関係**: T002, T003, T004, T005

---

### T007 ハンドラ登録（Unity側）

**ファイル**: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Core/UnityCliBridge.cs`

**タスク**:
- ProfilerHandlerをインスタンス化
- Unityコマンドハンドラーに`profiler_start`, `profiler_stop`, `profiler_status`, `profiler_get_metrics`を登録
- 既存のVideoCapture/Screenshotハンドラと同じパターンで実装

**検証**:
- Unity Editor起動時にProfilerHandlerが初期化される
- TCPサーバーが4つのプロファイラーコマンドを受信できる

**依存関係**: T001

---

## Phase 3.2: Contract Tests (TDD - RED) ⚠️ Phase 3.3の前に完了必須

**重要**: これらのテストは記述され、実装前に失敗する必要があります。すべてのContract Testsが失敗することを確認してからCore Implementationに進んでください。

---

### T008 [P] profiler_start contract test作成

**ファイル**: `unity-cli/tests/integration/profiler/profiler-start.test.js`

**タスク**:
- `profiler_start`のcontract testを作成
- `contracts/profiler-start.json`に基づいてテストケースを作成:
  1. 正常ケース: mode=normal, recordToFile=true → sessionId, isRecording, startedAt, outputPathを返却
  2. 正常ケース: mode=deep, recordToFile=false → outputPath=null
  3. エラーケース: 既にプロファイリング実行中 → E_ALREADY_RUNNING
  4. エラーケース: 無効なmode → E_INVALID_MODE
  5. エラーケース: 無効なメトリクス名 → E_INVALID_METRICS

**検証**:
- テストを実行すると5つすべてが失敗する（RED）
- `npm run test:integration --workspace=unity-cli`で実行可能

**依存関係**: T006, T007 [P]

---

### T009 [P] profiler_stop contract test作成

**ファイル**: `unity-cli/tests/integration/profiler/profiler-stop.test.js`

**タスク**:
- `profiler_stop`のcontract testを作成
- `contracts/profiler-stop.json`に基づいてテストケースを作成:
  1. 正常ケース: recordToFile=true → sessionId, outputPath, duration, frameCountを返却
  2. 正常ケース: recordToFile=false → outputPath=null, metricsが返却される
  3. エラーケース: プロファイリング未実行 → E_NOT_RECORDING
  4. エラーケース: 無効なsessionId → E_INVALID_SESSION
  5. エラーケース: ファイル保存失敗 → E_FILE_IO

**検証**:
- テストを実行すると5つすべてが失敗する（RED）
- `npm run test:integration --workspace=unity-cli`で実行可能

**依存関係**: T006, T007 [P]

---

### T010 [P] profiler_status contract test作成

**ファイル**: `unity-cli/tests/integration/profiler/profiler-status.test.js`

**タスク**:
- `profiler_status`のcontract testを作成
- `contracts/profiler-status.json`に基づいてテストケースを作成:
  1. 正常ケース: 未記録 → isRecording=false, sessionId=null, startedAt=null, elapsedSec=0
  2. 正常ケース: 記録中 → isRecording=true, sessionId, startedAt, elapsedSec>0
  3. 正常ケース: 自動停止設定あり → remainingSec>0

**検証**:
- テストを実行すると3つすべてが失敗する（RED）
- `npm run test:integration --workspace=unity-cli`で実行可能

**依存関係**: T006, T007 [P]

---

### T011 [P] profiler_get_metrics contract test作成

**ファイル**: `unity-cli/tests/integration/profiler/profiler-get-metrics.test.js`

**タスク**:
- `profiler_get_metrics`のcontract testを作成
- `contracts/profiler-get-metrics.json`に基づいてテストケースを作成:
  1. 正常ケース: listAvailable=true → categoriesオブジェクトを返却（Memory, Rendering, CPU, GC等）
  2. 正常ケース: listAvailable=false, metrics=[] → 全メトリクスの現在値を返却
  3. 正常ケース: listAvailable=false, metrics=["System Used Memory", "Draw Calls Count"] → 指定メトリクスのみ返却
  4. エラーケース: 無効なメトリクス名 → E_INVALID_METRICS

**検証**:
- テストを実行すると4つすべてが失敗する（RED）
- `npm run test:integration --workspace=unity-cli`で実行可能

**依存関係**: T006, T007 [P]

---

### T012 すべてのContract Testsが失敗することを確認（RED確認）

**タスク**:
- T008-T011のすべてのテストを実行
- すべてのテストが失敗することを確認（RED）
- テスト失敗の原因が「実装がない」ことであることを確認

**検証**:
- `npm run test:integration --workspace=unity-cli -- --profiler`で17個のテストすべてが失敗
- エラーメッセージが「機能未実装」または「ハンドラーが空」であることを確認

**依存関係**: T008, T009, T010, T011

**⚠️ 重要**: このタスクが完了するまでPhase 3.3に進んではいけません

---

## Phase 3.3: Core Implementation (TDD - GREEN) ⚠️ T012完了後のみ

**前提条件**: T012ですべてのContract Testsが失敗していることを確認済み

---

### T013 Unity ProfilerHandler.Start実装

**ファイル**: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ProfilerHandler.cs`

**タスク**:
- `Start()`メソッドを実装:
  1. 既にプロファイリング実行中の場合 → E_ALREADY_RUNNINGエラーを返却
  2. ProfilerDriver.enabledをtrueに設定
  3. ProfilerDriver.deepProfilingを設定（mode="deep"の場合）
  4. セッションID生成（GUID形式、ハイフンなし）
  5. 開始時刻記録（DateTime.UtcNow）
  6. recordToFile=trueの場合、outputPath生成（`.unity/capture/profiler_{sessionId}_{timestamp}.data`）
  7. maxDurationSec>0の場合、EditorApplication.updateに自動停止処理を登録
  8. ProfilerRecorderを初期化（metricsが指定されている場合はそれらのみ、未指定なら全メトリクス）
  9. セッション情報を静的フィールドに保存
  10. レスポンスJSON生成（sessionId, startedAt, isRecording, outputPath）

**検証**:
- T008の`profiler_start` contract testが合格する（GREEN）
- Unity EditorでProfiler.enabledがtrueになる
- セッションIDがGUID形式で生成される

**依存関係**: T001, T012

---

### T014 Unity ProfilerHandler.Stop実装

**ファイル**: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ProfilerHandler.cs`

**タスク**:
- `Stop()`メソッドを実装:
  1. プロファイリング未実行の場合 → E_NOT_RECORDINGエラーを返却
  2. sessionIdが指定されている場合、現在のセッションIDと一致するか確認（不一致なら→ E_INVALID_SESSION）
  3. ProfilerDriver.enabledをfalseに設定
  4. recordToFile=trueの場合、ProfilerDriver.SaveProfile(outputPath)で.dataファイル保存
  5. recordToFile=falseの場合、ProfilerRecorderから全メトリクス値を取得してJSON化
  6. 記録時間計算（duration = DateTime.UtcNow - startedAt）
  7. フレーム数カウント取得（ProfilerDriver.lastFrameIndex）
  8. EditorApplication.updateから自動停止処理を解除
  9. ProfilerRecorderをすべてDispose
  10. セッション情報をクリア
  11. レスポンスJSON生成（sessionId, outputPath, duration, frameCount, metrics）

**検証**:
- T009の`profiler_stop` contract testが合格する（GREEN）
- .dataファイルが`.unity/capture/`配下に保存される
- Unity Profiler Windowでファイルが正常に開ける

**依存関係**: T013

---

### T015 Unity ProfilerHandler.GetStatus実装

**ファイル**: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ProfilerHandler.cs`

**タスク**:
- `GetStatus()`メソッドを実装:
  1. 現在のセッション情報を読み取る
  2. isRecording=trueの場合:
     - sessionId、startedAtを返却
     - elapsedSec計算（DateTime.UtcNow - startedAt）
     - maxDurationSec>0の場合、remainingSec計算（maxDurationSec - elapsedSec）
  3. isRecording=falseの場合:
     - sessionId=null, startedAt=null, elapsedSec=0, remainingSec=null
  4. レスポンスJSON生成（isRecording, sessionId, startedAt, elapsedSec, remainingSec）

**検証**:
- T010の`profiler_status` contract testが合格する（GREEN）
- 記録中/未記録で正しい値が返却される
- 経過時間が実時間と一致する

**依存関係**: T013

---

### T016 Unity ProfilerHandler.GetAvailableMetrics実装

**ファイル**: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ProfilerHandler.cs`

**タスク**:
- `GetAvailableMetrics()`メソッドを実装:
  1. listAvailable=trueの場合:
     - ProfilerRecorderHandle.GetAvailable()で全メトリクスを取得
     - ProfilerCategoryごとにメトリクスをグループ化
     - カテゴリオブジェクト生成（categories: {Memory: {name, metrics[]}, Rendering: {name, metrics[]}, ...}）
  2. listAvailable=falseの場合:
     - metricsが指定されている場合はそれらのみ、未指定なら現在記録中の全メトリクスを取得
     - ProfilerRecorder.GetSample(0).Valueで現在値を取得
     - メトリクス配列生成（metrics: [{category, name, value, unit}, ...]）
  3. 無効なメトリクス名が指定された場合 → E_INVALID_METRICSエラーを返却
  4. レスポンスJSON生成（categories または metrics）

**検証**:
- T011の`profiler_get_metrics` contract testが合格する（GREEN）
- 利用可能なメトリクス一覧が返却される（Memory、Rendering、CPU、GC等のカテゴリ）
- 現在のメトリクス値が正確に返却される

**依存関係**: T013

---

### T017 [P] Node ProfilerStartToolHandler実装

**ファイル**: `unity-cli/src/handlers/profiler/ProfilerStartToolHandler.js`

**タスク**:
- `handle(args)`メソッドを実装:
  1. Unityコマンド`profiler_start`を送信
  2. 引数にworkspaceRootを追加（既存のVideoCaptureHandlerと同様）
  3. args.mode, args.recordToFile, args.metrics, args.maxDurationSecをUnityに転送
  4. Unityからのレスポンスをそのまま返却（sessionId, startedAt, isRecording, outputPath）
  5. エラーレスポンス処理（E_ALREADY_RUNNING, E_INVALID_MODE, E_INVALID_METRICS）

**検証**:
- MCPクライアントから`profiler_start`ツールを呼び出すと、Unityでプロファイリングが開始される
- workspaceRootが正しく解決される（未受領時は親ディレクトリを遡って `.unity/` を探索）

**依存関係**: T002, T013 [P]

---

### T018 [P] Node ProfilerStopToolHandler実装

**ファイル**: `unity-cli/src/handlers/profiler/ProfilerStopToolHandler.js`

**タスク**:
- `handle(args)`メソッドを実装:
  1. Unityコマンド`profiler_stop`を送信
  2. 引数にworkspaceRootを追加
  3. args.sessionIdをUnityに転送（省略時は現在のセッション）
  4. Unityからのレスポンスをそのまま返却（sessionId, outputPath, duration, frameCount, metrics）
  5. エラーレスポンス処理（E_NOT_RECORDING, E_INVALID_SESSION, E_FILE_IO）

**検証**:
- MCPクライアントから`profiler_stop`ツールを呼び出すと、Unityでプロファイリングが停止される
- .dataファイルが正しく保存される

**依存関係**: T003, T014 [P]

---

### T019 [P] Node ProfilerStatusToolHandler実装

**ファイル**: `unity-cli/src/handlers/profiler/ProfilerStatusToolHandler.js`

**タスク**:
- `handle(args)`メソッドを実装:
  1. Unityコマンド`profiler_status`を送信
  2. Unityからのレスポンスをそのまま返却（isRecording, sessionId, startedAt, elapsedSec, remainingSec）

**検証**:
- MCPクライアントから`profiler_status`ツールを呼び出すと、現在の状態が返却される
- 記録中/未記録で正しい値が返る

**依存関係**: T004, T015 [P]

---

### T020 [P] Node ProfilerGetMetricsToolHandler実装

**ファイル**: `unity-cli/src/handlers/profiler/ProfilerGetMetricsToolHandler.js`

**タスク**:
- `handle(args)`メソッドを実装:
  1. Unityコマンド`profiler_get_metrics`を送信
  2. args.listAvailable, args.metricsをUnityに転送
  3. Unityからのレスポンスをそのまま返却（categories または metrics）
  4. エラーレスポンス処理（E_INVALID_METRICS）

**検証**:
- MCPクライアントから`profiler_get_metrics`ツールを呼び出すと、メトリクス一覧または現在値が返却される
- カテゴリ別のグループ化が正しく機能する

**依存関係**: T005, T016 [P]

---

### T021 すべてのContract Testsが合格することを確認（GREEN確認）

**タスク**:
- T008-T011のすべてのテストを再実行
- すべてのテストが合格することを確認（GREEN）
- カバレッジが80%以上であることを確認

**検証**:
- `npm run test:integration --workspace=unity-cli -- --profiler`で17個のテストすべてが合格
- `npm run test:coverage --workspace=unity-cli`でカバレッジレポート生成

**依存関係**: T017, T018, T019, T020

**⚠️ 重要**: このタスクが完了するまでPhase 3.4に進んではいけません

---

## Phase 3.4: Integration

---

### T022 EditorApplication.update定期処理実装

**ファイル**: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ProfilerHandler.cs`

**タスク**:
- EditorApplication.updateに自動停止処理を登録:
  1. maxDurationSec>0の場合、経過時間を監視
  2. 経過時間がmaxDurationSecを超えたら自動的にStop()を呼び出す
  3. ProfilerRecorderの定期サンプリング（必要に応じて）
  4. EditorApplication.updateからの登録解除処理を実装

**検証**:
- maxDurationSec=10で開始し、10秒後に自動停止することを確認
- `profiler_status`でremainingSecがカウントダウンされることを確認

**依存関係**: T013, T014

---

### T023 ProfilerRecorderによるメトリクス取得実装

**ファイル**: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ProfilerHandler.cs`

**タスク**:
- ProfilerRecorderを使用したリアルタイムメトリクス取得:
  1. Start()時にProfilerRecorder.StartNew()で各メトリクスのRecorderを作成
  2. GetAvailableMetrics()でProfilerRecorder.GetSample(0).Valueで現在値を取得
  3. Stop()時にすべてのRecorderをDispose
  4. メトリクスのカテゴリ分類実装（ProfilerCategory）

**検証**:
- `profiler_get_metrics`で正確なメトリクス値が返却される
- メモリリークが発生しない（Recorder適切にDispose）

**依存関係**: T016

---

### T024 ワークスペースルート解決機能統合

**ファイル**: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ProfilerHandler.cs`

**タスク**:
- 既存のWorkspaceRootResolver機能を統合:
  1. Node側から受け取ったworkspaceRootを優先使用
  2. 未受領時は親ディレクトリを遡って `.unity/` を探索してフォールバック解決
  3. outputPath生成時にワークスペースルート + `.unity/capture/`を使用

**検証**:
- .dataファイルが正しいワークスペースルートの`.unity/capture/`配下に保存される
- 既存のVideoCapture/Screenshotと同じ場所に保存される

**依存関係**: T013, T014

---

## Phase 3.5: Polish

---

### T025 [P] Unit Tests作成

**ファイル**: `unity-cli/tests/unit/handlers/profiler/*.test.js`

**タスク**:
- 各ToolHandlerのunit testを作成:
  1. ProfilerStartToolHandler.test.js: 引数変換、バリデーション
  2. ProfilerStopToolHandler.test.js: sessionId省略時のデフォルト動作
  3. ProfilerStatusToolHandler.test.js: レスポンス形式検証
  4. ProfilerGetMetricsToolHandler.test.js: メトリクス配列の検証
- モックUnity接続を使用してハンドラーロジックのみテスト

**検証**:
- `npm run test:unit --workspace=unity-cli`ですべてのunit testsが合格
- カバレッジ80%以上を維持

**依存関係**: T017, T018, T019, T020 [P]

---

### T026 エラーハンドリング強化

**ファイル**: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ProfilerHandler.cs`

**タスク**:
- 包括的なエラーハンドリング追加:
  1. ProfilerDriver.SaveProfile()失敗時 → E_FILE_IO
  2. 無効なメトリクス名指定時 → E_INVALID_METRICS（無効な名前のリスト付き）
  3. Unity Editorクラッシュ時の状態リセット処理
  4. ファイル書き込み権限エラー処理
  5. メモリ不足時の対処

**検証**:
- すべてのエラーケースで適切なエラーコードとメッセージが返却される
- Unity Editorクラッシュ後の再起動で状態がクリアされる

**依存関係**: T013, T014, T016

---

### T027 構造化ロギング追加

**ファイル**:
- `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ProfilerHandler.cs`
- `unity-cli/src/handlers/profiler/*.js`

**タスク**:
- 既存のロギングインフラを使用してログ追加:
  1. プロファイリング開始/停止時のINFOログ
  2. .dataファイル保存成功時のINFOログ
  3. エラー発生時のERRORログ（スタックトレース付き）
  4. メトリクス取得時のDEBUGログ（メトリクス名、値）

**検証**:
- Unity Console/Node consoleでログが正しく出力される
- ログレベルが適切（DEBUG/INFO/ERROR）

**依存関係**: T013, T014, T015, T016

---

### T028 [P] README.md更新

**ファイル**: `README.md`

**タスク**:
- Unity Profilerパフォーマンス計測機能のドキュメント追加:
  1. 機能概要セクション追加
  2. 4つのMCPツールの説明追加
  3. quickstart.mdへのリンク追加
  4. 既存のVideoCapture/Screenshot機能と同じフォーマットで記載

**検証**:
- `npm run lint:md --workspace=unity-cli`がエラーなく完了
- README.mdがGitHub上で正しくレンダリングされる

**依存関係**: T021 [P]

---

### T029 [P] quickstart.md検証実行

**ファイル**: `specs/SPEC-1591581c/quickstart.md`

**タスク**:
- quickstart.mdに記載されたすべての手順を手動実行:
  1. プロファイリング開始
  2. プロファイリング停止と.dataファイル保存
  3. Unity Profiler Windowでファイルを開く
  4. リアルタイムメトリクス取得
  5. プロファイリング状態確認
  6. エラーケースの検証

**検証**:
- すべての手順が正常に動作する
- Unity Profiler Windowで.dataファイルが正常に開ける

**依存関係**: T021 [P]

---

### T030 リファクタリング（重複削除）

**ファイル**:
- `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ProfilerHandler.cs`
- `unity-cli/src/handlers/profiler/*.js`

**タスク**:
- コードの重複を削除:
  1. セッション状態管理の共通化
  2. ワークスペースルート解決ロジックの共通化
  3. エラーレスポンス生成の共通化
  4. メトリクス名バリデーションの共通化

**検証**:
- すべてのテストが引き続き合格する
- コードカバレッジが維持される
- `npm run lint --workspace=unity-cli`がエラーなく完了

**依存関係**: T021

---

## 依存関係グラフ

```
Setup (T001-T007)
  ↓
Contract Tests (T008-T012) [RED確認必須]
  ↓
Core Implementation (T013-T021) [GREEN確認必須]
  ↓
Integration (T022-T024)
  ↓
Polish (T025-T030)
```

**詳細な依存関係**:
- T001 → T007
- T002, T003, T004, T005 → T006
- T006, T007 → T008, T009, T010, T011
- T008, T009, T010, T011 → T012
- T012 → T013, T014, T015, T016（T012完了まで実装禁止）
- T013 → T014, T015, T016, T022, T024
- T014 → T024
- T016 → T023
- T002, T013 → T017
- T003, T014 → T018
- T004, T015 → T019
- T005, T016 → T020
- T017, T018, T019, T020 → T021
- T021 → T022, T023, T024, T025, T026, T027, T028, T029, T030（T021完了まで統合/仕上げ禁止）

---

## 並列実行例

### Phase 3.1: Setup
```
# T002-T005 を並列実行（すべて異なるファイル）
Task: "Node ProfilerStartToolHandler.js作成（空実装）"
Task: "Node ProfilerStopToolHandler.js作成（空実装）"
Task: "Node ProfilerStatusToolHandler.js作成（空実装）"
Task: "Node ProfilerGetMetricsToolHandler.js作成（空実装）"
```

### Phase 3.2: Contract Tests
```
# T008-T011 を並列実行（すべて異なるファイル）
Task: "profiler_start contract test作成"
Task: "profiler_stop contract test作成"
Task: "profiler_status contract test作成"
Task: "profiler_get_metrics contract test作成"
```

### Phase 3.3: Core Implementation (Node側)
```
# T017-T020 を並列実行（すべて異なるファイル）
Task: "Node ProfilerStartToolHandler実装"
Task: "Node ProfilerStopToolHandler実装"
Task: "Node ProfilerStatusToolHandler実装"
Task: "Node ProfilerGetMetricsToolHandler実装"
```

### Phase 3.5: Polish
```
# T025, T028, T029 を並列実行（すべて異なるファイル）
Task: "Unit Tests作成"
Task: "README.md更新"
Task: "quickstart.md検証実行"
```

---

## 注意事項

### TDD厳守
- **絶対ルール**: T012（すべてのContract Testsが失敗）が完了するまでT013-T021（Core Implementation）を開始しない
- **絶対ルール**: T021（すべてのContract Testsが合格）が完了するまでT022-T030（Integration/Polish）を開始しない
- **コミット順序**: テストコミットが実装コミットより先

### 並列実行
- [P]マーク付きタスクは並列実行可能（異なるファイル、依存関係なし）
- 同じファイルを変更するタスクは順次実行必須

### 各タスク後のアクション
- 実装後、該当するテストを実行して確認
- コミットメッセージはConventional Commits形式
- `npm run lint --workspace=unity-cli`と`npm run test --workspace=unity-cli`を実行してエラーがないことを確認

### エラー対処
- テスト失敗時は実装をロールバックして再度RED-GREEN-Refactorサイクル
- Unity Editorクラッシュ時は状態リセット処理を実装

---

## 検証チェックリスト

**タスク完全性**:
- [x] すべてのcontracts（4つ）に対応するcontract testがある（T008-T011）
- [x] すべてのentities（ProfilerSession, ProfilerMetric）にデータモデルがある（data-model.md）
- [x] すべてのMCPツール（4つ）が実装されている（T013-T020）
- [x] すべてのテストが実装より先にある（T008-T012 → T013-T021）
- [x] 並列タスクは本当に独立している（[P]マークのタスクはすべて異なるファイル）
- [x] 各タスクは正確なファイルパスを指定している
- [x] 同じファイルを変更する[P]タスクがない

**TDD検証**:
- [ ] T012完了時点ですべてのContract Testsが失敗している（RED）
- [ ] T021完了時点ですべてのContract Testsが合格している（GREEN）
- [ ] カバレッジが80%以上である

**統合検証**:
- [ ] .dataファイルがUnity Profiler Windowで正常に開ける
- [ ] リアルタイムメトリクスが正確に取得できる
- [ ] 既存のVideoCapture/Screenshot機能と同じ操作感で使用できる

---

**総タスク数**: 30タスク
**並列実行可能タスク数**: 13タスク（[P]マーク付き）
**推定実装時間**: Phase 3.1-3.2: 2-3時間、Phase 3.3: 4-6時間、Phase 3.4-3.5: 2-3時間

---

*TDD厳守 - 憲章 v1.0.0 に基づく - `/docs/constitution.md` 参照*
