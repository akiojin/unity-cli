# Issue #390 — Play Mode のメソッド差し替えを macOS の Editor で実証して有効にする

## 原因

`hot_reload apply` が一度も成功しなかった原因は、Editor のアーキテクチャではなかった。

- FastScriptReload 1.8.0 のコンパイラは、候補の型に自前の static フィールド
  （`__Patched_NewFieldNameToInitialValueFn` など）を追加する。
- アダプタは FSR のローダーを「フィールド数チェック有効」で呼んでいた。FSR はこの追加フィールドを
  「フィールドが増えた」と判定し、その型の detour をすべて飛ばしていた。
- その結果、x64 でも ARM64 でも `FSR did not install every requested detour.`
  （`HOT_RELOAD_VERIFICATION_FAILED`）になっていた。ARM64 は事前に拒否していたため、この失敗は
  どの環境でも観測されていなかった。

## 変更

- アダプタ: 候補の型のフィールド名（`__Patched_` で始まる FSR 生成分を除く）が、コンパイル済みの型と
  一致することをパッチ前に検証する。一致しなければパッチせずに失敗する。そのうえで FSR 側の
  フィールド数チェックを無効にして呼ぶ。
- 対応ホスト: x64 Editor に加えて、Apple Silicon（ARM64）の macOS Editor を対応にした。
  FSR 同梱の Harmony（MonoMod.Core 1.3.3）は macOS ARM64 の detour に対応しており、実差し替えが通った。
  それ以外のホストは従来どおり `HOT_RELOAD_PLATFORM_UNSUPPORTED` で拒否し、x64 Editor を使う案内を返す。
- `scripts/e2e-hot-reload-batch-host.sh`: `UNITY_PATH` の Editor バージョンで fixture を作る
  （`ProjectVersion.txt` とパッケージのバージョンを Editor に合わせる）。`--require-arch` と `--artifacts` を追加。
- `scripts/e2e-hot-reload.py`: Editor プロセスのアーキテクチャを出力し、`--require-arch` で検証する。
  クリーンコンパイル待ちを 90 秒から 300 秒にした（Rosetta の 2022.3 は 90 秒を超える）。
- `scripts/e2e-matrix.py`: `--fsr-path` で `hot-reload-apply`、`--x64-editor-root` で
  `hot-reload-apply-x64` スイートを追加する。

## 実 Editor での結果（2026-09-30、macOS / Apple Silicon、コミット 0f1fe44）

```bash
python3 scripts/e2e-matrix.py \
  --editor /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  --editor /Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  --lsp-root ~/.unity/tools --suites hot-reload,hot-reload-apply,hot-reload-apply-x64 \
  --fsr-path <FastScriptReload 51140b7>/Assets \
  --x64-editor-root ~/Library/Caches/unity-cli/x64-editors
```

開始時刻 2026-09-30T08:08:41Z（最後の製品コード変更より後）。全体 PASS。

| Editor | プロセス | スイート | 結果 |
| --- | --- | --- | --- |
| 6000.3.25f1 | X64（Rosetta 2） | `hot-reload-apply-x64` | 25 passed / 0 failed |
| 2022.3.62f3 | X64（Rosetta 2） | `hot-reload-apply-x64` | 25 passed / 0 failed |
| 6000.3.25f1 | Arm64 | `hot-reload-apply` | 24 passed / 0 failed |
| 2022.3.62f3 | Arm64 | `hot-reload-apply` | 24 passed / 0 failed |
| 6000.3.25f1 | Arm64 | `hot-reload`（パッケージ未導入の契約） | 8 passed / 0 failed |
| 2022.3.62f3 | Arm64 | `hot-reload`（パッケージ未導入の契約） | 8 passed / 0 failed |

x64 の 25 件は、24 件に「プロセスが X64 であること」の検査を加えたもの。

supported シナリオが確認する内容:

- Play 中に `Calculate` の式を `input * 2` から `input * 5` に変えると、Play を止めずに値が 6 から 15 になる。
- シーンのパスとハンドル、オブジェクト ID、位置、HP、score、static、NonSerialized の値が変わらない。
- ディスク上の `.cs` は変更されない。
- 古い revision、構文エラー、フィールドの型変更は、パッチ前に拒否され、`appliedRevision` は変わらない。
- 2 つのメソッドを変えて片方だけが実行された場合、検証待ちの間は `appliedRevision` が null で、
  期限後は `HOT_RELOAD_VERIFICATION_FAILED` と `recoveryRequired` になる。
- `recover` のあと、新しい Play はディスクの元のソース（値 6）で動く。

## 範囲外

- Windows / Linux の実機確認は Issue #386。
- FastScriptReload の自動導入（`unity-cli setup` からの案内・自動化）は行っていない。導入手順は
  `docs/hot-reload.md` と `unity-development-loop` スキルに記載した。
