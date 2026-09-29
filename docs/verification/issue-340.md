# Issue #340 — 現存 Unity Editor 全版の互換性マトリクス

## 変更と再現

- `scripts/e2e-matrix.sh`（`scripts/e2e-matrix.py`）を追加した。Editor ごとに隔離プロジェクトを
  新規作成し、その Editor に同梱された Package Manager カタログから VFX / URP のバージョンを選ぶ。
  既存の input / Timeline / VFX / eval / hot-reload / reload / all-tools の各 E2E を順に実行する。
- Unity 2022.3 は一時停止中に `UnitySynchronizationContext` を回さない。コマンドキューの完了は
  `EditorApplication.update` からポーリングし、送信側は `ConfigureAwait(false)` にした。
- 6000.7.0a2 に同梱の Test Framework は、Domain Reload なしで Play Mode に入ると、読み込み済み
  テストアセンブリのキャッシュを無効化せずにクリアする。このため 0 件のまま成功扱いになる。
  この alpha に限り実行前にキャッシュを無効化し、フックが無い場合は
  `TEST_RUNNER_DOMAIN_RELOAD_REQUIRED` で明示的に失敗させる。
- VFX Graph のバージョン差分（AC-3）:
  - 解決できない reflection は `VFX_API_UNSUPPORTED` を返す（従来は `INTERNAL_ERROR`）。
  - VFX 17.6 以降は `GetOrCreateGraph` が `GetGraph` / `CreateGraph` に分かれた。VFX 14 は
    `WriteAssetWithSubAssets` が無い。どちらの差分も吸収した。
  - VFX 14 の SDF ベイクは Edit Mode では `VFX_SDF_EDIT_MODE_UNSUPPORTED` を返し、Play Mode で実行する。
  - VFX 17.6 以降、Shader Graph 出力の `shaderGraph` を `set_context_setting` で設定すると、
    直前の再インポート後の最初の 1 回が成功応答のまま失われていた。再インポートで
    `[SerializeReference]` の shading が作り直される一方、非シリアライズの trait 記述キャッシュが
    古いインスタンスを指していたため。書き込み前に `MarkCacheAsDirty` でキャッシュを再構築し、
    書き込み後に値を読み戻して、一致しなければ `VFX_API_UNSUPPORTED` を返す。
    6000.6.3f1 で、`add_context` の直後の設定が 2 アセットとも失われることを再現した。修正後の
    `ApplySetContextSetting_AssignsShaderGraphAssetToComposedOutput` は 6000.6 / 6000.7a2 / 6000.7b2 で PASS。
  - sticky-note の `colorTheme`（VFX 17.4 で追加）のガードが、ノート操作全体と group / auto-layout を
    拒否していた（VFX 17.0 では group 系の 3 件が失敗）。明示的な `colorTheme` 指定時だけ
    `VFX_API_UNSUPPORTED` を返し、グラフを変更しないように修正した。
  - VFX 14 の `convert_to_property` は、パラメーター記述子の `modelType` が `VFXParameter` を
    返すため `Single` に一致しなかった。`model.type` でも照合するよう修正した。

## ローカル検証

2026-09-30、macOS（Apple Silicon）実機。描画を有効にした隔離 Editor プロジェクトで検証した。

| 検証 | 結果 |
| --- | --- |
| `cargo fmt --all -- --check` | PASS |
| `cargo clippy --all-targets -- -D warnings` | PASS |
| `cargo test --all-targets -- --test-threads=1` | 460 unit + 10 integration PASS |
| `cargo run -- skills lint --severity error` | 16 skills、違反 0 |
| `dotnet test lsp/Server.Tests.csproj` | 50 PASS |
| `python3 -m unittest tests/scripts/test_e2e_matrix.py` | 6 PASS |
| 8 Editor 完全マトリクス（最終ソース） | 8/8 PASS（6000.7.0a2 は再実行で PASS） |

完全マトリクスの結果（Editor ごとに、全スイート PASS）:

| Editor | VFX | VFX E2E | All-tools |
| --- | --- | --- | --- |
| 2022.3.62f3 | 14.0.12 | 73/73 | 121/121 ツール、133 呼び出し |
| 6000.0.84f1 | 17.0.4 | 64/64 | 121/121、133 |
| 6000.3.25f1 | 17.3.0 | 64/64 | 121/121、133 |
| 6000.4.11f1 | 17.4.0 | 64/64 | 121/121、133 |
| 6000.5.3f1 | 17.5.0 | 64/64 | 121/121、133 |
| 6000.6.3f1 | 17.6.0 | 65/65 | 121/121、133 |
| 6000.7.0a2 | 17.7.0 | 65/65 | 121/121、133 |
| 6000.7.0b2 | 17.7.0 | 64/64 | 121/121、133 |

`--skip-quit` のため `quit_editor` は呼ばない（#324 の 122 件との差）。各 Editor の実行時ランタイムは、
`eval_csharp` で `Mono.Runtime` 型の存在を確認し、Mono であることを実測した。

6000.7.0a2 は、Bridge とは無関係な Unity 側の理由で 2 回失敗した。1 回目は組み込みモジュールの
インポート中に Mono が `implement type compare for 0!` で abort した（Bridge のコンパイル前）。
2 回目は、2 グループ並行実行中に SDF ベイク後の Console に Unity 内部エラー
`deleting an allocation that is older than its permitted lifetime of 4 frames` が出て、
VFX の clean-console 検査が 64/65 になった。単独で再実行した結果は全スイート PASS。
失敗した実行は PASS に数えていない。

## VfxGraphHandlerTests（EditMode NUnit）

`VfxGraphHandlerTests` と `UnityCliBridgeHostConnectionTests` を、各 Editor の最終ソースで実行した。

| Editor | 結果 | 残りの失敗 |
| --- | --- | --- |
| 2022.3.62f3 | 185/223 | 38（テンプレート名・記述子名・Custom HLSL 不在などの fixture 前提） |
| 6000.0.84f1 | 216/222 | 6（明示 `colorTheme`、GPU Event 不在、テンプレート指定 API 不在など） |
| 6000.3.25f1 | 222/222 | 0 |
| 6000.4.11f1 | 222/222 | 0 |
| 6000.5.3f1 | 220/222 | 2（`01_Minimal_System` などの旧テンプレート名） |
| 6000.6.3f1 | 218/222 | 4（旧テンプレート名、テスト内の `GetOrCreateGraph` 直接呼び出し、削除された設定） |
| 6000.7.0a2 | 218/222 | 4（6000.6 と同じ） |
| 6000.7.0b2 | 218/222 | 4（6000.6 と同じ） |

修正前は 2022.3 = 46、6000.0 = 14、6000.6 / 6000.7 = 5 件の失敗だった。残りの失敗は、実行時に
`vfx_*` を呼ぶと仕様どおり動作するか、安定したエラーコード（`VFX_API_UNSUPPORTED` /
`INVALID_ARGUMENT` と利用可能な名前の一覧）を返すことを、実 Editor への呼び出しで確認した。
失敗の原因は、VFX 17.4 に固定したテンプレート名・記述子名・ノード数・UI schema の期待値にある。
この fixture のバージョン対応は Issue #344 で扱う（PM 裁定 2026-09-30）。
`UnityCliBridgeHostConnectionTests` は全 Editor で PASS。

追加・更新した検証項目:

- `MissingReflectionMethod_ReturnsStableUnsupportedCode`
- `LegacyPackage_MissingAuthoringApis_ReturnQuietUnsupportedErrors`（VFX 14: 明示 `colorTheme` は拒否、ノート自体は作成可能）
- `tests/scripts/test_e2e_matrix.py`（カタログに従うパッケージ選択、VFX 欠落の検出、準備判定、
  失敗・部分実行を PASS にしない判定、タイムアウト時の停止、fixture の隔離）

## 対象外

- Windows 実機検証は AC-6 のとおり保留。
- CoreCLR Player のビルド・実行は対象外（AC-8）。6000.7 の Editor は Mono で検証済み。
