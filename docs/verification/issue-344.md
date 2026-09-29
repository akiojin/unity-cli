# Issue #344 — VFX NUnit fixture のバージョン非依存化

## 変更

- `VfxGraphHandlerTests` は、Editor に解決された `com.unity.visualeffectgraph` のバージョンを
  `PackageInfo` から読み、次の値をパッケージごとに選ぶようにした。
  - ライブラリ名: VFX 17 の `|Set|_Color` / `Output Particle|Unlit|Quad` と、VFX 14 の
    `Set Color` / `Output Particle Quad` など（`LibName`）。
  - テンプレート名: そのパッケージの template 一覧から `01_Minimal_System` / `Minimal_System` /
    `SimpleParticleSystem` などを選ぶ（`MinimalTemplate` / `BurstTemplate`）。
  - グラフの取得: テスト内で `GetOrCreateGraph` を直接呼ばず、ハンドラーのバージョン対応
    アクセサーを使う（VFX 17.7 で API が分割されたため）。
- パッケージに存在しない機能は、`VFX_API_UNSUPPORTED` を返すことを検証する。
  - Custom HLSL（17.0 以降）、blackboard の custom attribute（17.0 以降）、sticky-note の
    `colorTheme`（17.4 以降）、`designate_template`（GraphView template descriptor）、
    `allowShaderExternalization`（17.6 で削除）、VFX 14 の `VFXErrorReporter`。
  - VFX 14 の Shader Graph、flipbook、strip 出力、block subgraph は、そのパッケージの手順で検証する。
  - GPU Event は 17.3 より前では experimental のため、テスト中だけ
    `displayExperimentalOperator` を有効にし、終了時に元に戻す。
- 製品コードの修正（テストで見つけたもの）:
  - VFX 14 で Custom HLSL を要求すると `INVALID_ARGUMENT` を返していた。記述子が無いことが
    原因なので、`VFX_API_UNSUPPORTED`（「VFX Graph 17.0 以降が必要」）を返すようにした。
  - `includeErrors` のエラー収集が使えない場合、エントリに `code` を付けるようにした。
  - `group_nodes` の note に `colorTheme` を指定すると、17.4 未満では拒否の前にグループを
    作っていた。グループを作る前に拒否するよう修正した。
- `AnimationCurveHandlerTests`（PM 指示で範囲に追加）: `async Task` のテストは、2022.3 に同梱の
  Test Framework 1.1.33 では実行できず 25 件が失敗していた。対象のルートは同期で完了するため、
  テストを同期メソッドにした。
- 6000.7 の CS0619（`InstanceIDToObject` / `GetInstanceID`）の修正は、hotfix PR #348 と同じ内容。

## 検証

2026-09-30、macOS（Apple Silicon）実機。Editor ごとに隔離プロジェクトを作り、その Editor の
パッケージカタログから VFX / URP のバージョンを選んだ（`scripts/e2e-matrix.py` の `prepare`）。

```bash
"$UNITY" -batchmode -projectPath "$PROJECT" -runTests -testPlatform EditMode \
  -testFilter "UnityCliBridge.Tests.VfxGraphHandlerTests;UnityCliBridge.Tests.AnimationCurveHandlerTests" \
  -testResults results.xml -logFile editor.log
```

| Editor | VFX Graph | 修正前（VFX） | 最終ソース（VFX + AnimationCurve） |
| --- | --- | --- | --- |
| 2022.3.62f3 | 14.0.12 | 173/211（38 件失敗） | 237/237 PASS |
| 6000.0.84f1 | 17.0.4 | 204/210（6 件失敗） | 236/236 PASS |
| 6000.4.11f1 | 17.4.0 | 210/210 | 236/236 PASS |
| 6000.7.0b2 | 17.7.0 | コンパイル失敗（CS0619）、CS0619 修正後 232/236 | 236/236 PASS |

skipped は全 Editor で 0。VFX_API_UNSUPPORTED を検証するテストも、実際にその応答を確認して PASS している。
