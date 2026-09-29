# Issue #352 — Unity 2022.3 で async Task テストを実行できるようにする

## 変更

- Unity 2022.3 に同梱の Test Framework 1.1.33 は、戻り値が `Task` のテストを
  `Method has non-void return value` で失敗させる。該当テストを `[UnityTest]` + `IEnumerator` に置き換えた。
  - `TaskTestUtility.Await`（`Tests/Editor/Helpers`）: async の本体を Editor フレームを進めながら待ち、
    完了後に元の例外（Assert 失敗 / Ignore）をそのまま投げ直す。タイムアウトは 30 秒。
  - 対象: `BridgeCommandRouterTests`（2）、`HotReloadHandlerTests`（1）、`PlayerBuildHandlerTests`（3）、
    `UIInteractionHandlerTests`（3）、`UnityCliBridgeIntegrationTests`（5）。
  - `AnimationCurveHandlerTests` は #344（PR #351）と同じ内容（同期化）をそのまま取り込んだ。
- 再発防止: `TestMethodReturnTypeTests.NoTestMethodReturnsTask` が、テストアセンブリ内に `Task` を返す
  `[Test]` / `[TestCase]` / `[UnityTest]` が無いことを検査する（どの Editor でも失敗として検出できる）。
- `UnityCliBridgeIntegrationTests` は、これまで常に skip されていた（`-runTests` プロセスではリスナーを
  起動しない設計のため）。さらに、改行区切りの送信と旧レスポンス形式（`success` / `data`）を前提にしていた。
  - テスト用フック `UnityCliBridge.StartOnEphemeralLoopbackPortForTesting()`（internal）を追加した。
    ループバックの空きポートでリスナーを起動する。既存の起動処理は、スキップ判定と起動本体に分けただけで、
    挙動は変わらない。フィクスチャ終了時は `Restart()` で設定どおりの状態に戻す。
  - テストは実際のプロトコル（4 バイト big-endian の長さ + JSON）と、現在の応答形式
    （`id` / `status` / `result` / `code`）で検証する。
- 製品コードの修正（テストで見つけたもの）: 受信した JSON が不正なとき（`JSON_ERROR`）と、コマンド形式が
  不正なとき（`PARSE_ERROR`）の応答が、`ErrorResult(message, code, null)` のオーバーロード解決で
  `ErrorResult(id, message, code, details)` に束縛されていた。そのため `id` にメッセージ、`error` にコード、
  `code` に null が入っていた。`(object)null` を渡して、`(message, code, details)` を使うようにした。
- `TestExecutionHandlerCollectorTests`（PM 指示で範囲に追加）: 2022.3 に同梱の Test Framework 1.1.33 の
  `TestAdaptor` は、ルート直下のテストを assembly 階層とみなして `platform` プロパティを読む。テストが組み立てる
  NUnit ツリーにはこのプロパティが無く、`NullReferenceException` で 6 件失敗していた（`origin/develop` でも同じ）。
  テスト側で、ルート直下のテストに `platform` が無ければ設定するようにした（テストのみの変更）。

## 検証

2026-09-30、macOS（Apple Silicon）実機。`scripts/e2e-matrix.py` の `prepare` で Editor ごとに隔離プロジェクトを作り、
次のコマンドで実行した。

```bash
"$UNITY" -batchmode -projectPath "$PROJECT" -runTests -testPlatform EditMode \
  -testFilter "UnityCliBridge.Tests.Editor.Core.BridgeCommandRouterTests;UnityCliBridge.Tests.Integration.UnityCliBridgeIntegrationTests;UnityCliBridge.Tests.Editor.Handlers.HotReloadHandlerTests;UnityCliBridge.Tests.PlayerBuildHandlerTests;UnityCliBridge.Tests.UIInteractionHandlerTests;UnityCliBridge.Tests.AnimationCurveHandlerTests;UnityCliBridge.Tests.Helpers.TestMethodReturnTypeTests;UnityCliBridge.Tests.TestExecutionHandlerCollectorTests" \
  -testResults results.xml -logFile editor.log
```

| Editor | 修正前 | 最終ソース |
| --- | --- | --- |
| 2022.3.62f3 | 18/59 PASS（40 件失敗: `Method has non-void return value` とガードテスト、1 件 skip: Integration）。Collector は `origin/develop` で 1/7 PASS（6 件失敗） | 66/66 PASS（skipped 0） |
| 6000.4.11f1 | — | 66/66 PASS（skipped 0） |

`UnityCliBridge.Tests` アセンブリ全体（Collector 修正前のソース）（`-assemblyNames UnityCliBridge.Tests`）の結果:

- 6000.4.11f1: 540 PASS、失敗 0。skip 2 件（package-absent プロジェクトでだけ動く既存のテスト）。
- 2022.3.62f3: 497 PASS、44 件失敗、2 件 skip（上と同じ）。失敗の内訳は、`VfxGraphHandlerTests` の 38 件（#344 / PR #351 の範囲）と、
  `TestExecutionHandlerCollectorTests` の 6 件（`origin/develop` でも失敗していた既存の問題。上の修正で解消し、66/66 の run に含めて確認した）。
