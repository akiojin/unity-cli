# C# LSP 書き込みの信頼性測定（#490）

## 結論: 現時点では直接編集を禁止できない（DO NOT ENFORCE）

3 OS × 4 開始条件 × 19 シナリオの全セルを各 3 試行、計 684 試行測定しました。
期待バイト列と一致したのは各 OS とも 228 試行中 24 試行（10.5%）です。
目安（全シナリオ・全 OS で成功率 99% 以上、誤編集・エンコーディング/改行の破壊 0 件）を
どの OS・どの開始条件でも満たしていません。オーナーの最終判断のための測定であり、
この測定は禁止を導入しません。

根拠は次のとおりです。

1. **BOM の付加（#498）**: BOM なしのファイルを扱う 17 シナリオすべてで、3 OS・4 条件・全試行に
   UTF-8 BOM（EF BB BF）が付きました（各 OS 204 件）。直接編集では起きない差分が
   ほぼ全ファイルに入ります。一致したのは BOM あり CRLF の更新と、ファイルを変更しない
   構文検証の 2 シナリオだけです。
2. **コメントの消失（#499）**: `replace_symbol_body` が末尾コメントと改行を消し、次のメンバーと
   連結していました。checkout の LSP では #503 で修正済みで、`warm_indexed` / `unindexed`
   では 3 OS とも再発していません。ただし利用者が実際に取得する公開版 LSP `0.18.1`
   （`daemon_stopped` / `first_download`）では 3 OS とも 3/3 で再現します。
   次のリリースまで、エージェントの実環境ではコードを壊す誤編集が残ります。
3. **ScriptableObject の `using` 欠落（#500）**: #502 で修正済みです。この測定では
   全 OS・全条件で再発していません（残る差分は BOM のみ）。
4. **コールド起動の遅さと失敗（#501 / #504）**: デーモン停止中の最初の呼び出しは
   macOS / Linux で p50 約 3〜4 秒、Windows で p50 約 13 秒でした。先行の run
   38372615105 では Windows が約 43 秒でした。macOS のローカル実行では、自動起動の
   5 秒待ちを超えて編集が行われない試行が 1 件ありました（#501、PR #505 で修正済み）。

禁止を再検討できる条件は、#498 の修正、#499 の修正を含む LSP の公開、#504 の改善です。
checkout の LSP を使う 2 条件では BOM 以外の失敗がないため、#498 を直せば全シナリオが一致する見込みです。
その後に週次の多試行測定（各 20 試行、コールド各 5 試行）で 99% 以上を確認してください。
少数試行での 100% は将来の 99% を保証しません。

## 測定データの出典

| 項目 | 値 |
| --- | --- |
| CI run | [38380911276](https://github.com/akiojin/unity-cli/actions/runs/38380911276)（`Test` workflow、PR #497） |
| 測定対象 | `b5a4059f`（develop の #502 / #503 を取り込み済み） |
| 取得日時 | 2026-10-13 10:05〜10:44 UTC（artifact 取得: 2026-10-13） |
| RID | `win-x64` / `osx-arm64` / `linux-x64`（GitHub-hosted `*-latest`） |
| 試行数 | 各セル 3 試行（コールド条件も 3 試行） |
| LSP | `warm_indexed` / `unindexed` は checkout、`daemon_stopped` / `first_download` は公開版 `0.18.1` |
| 回帰ゲート | 3 OS とも baseline 外の失敗 0 件 |

CI artifact（`results.json` の生応答と diff）は保存期間 30 日で失われます。
本文の表がこのレポートの一次資料です。Windows の初期測定の生データは
[lsp-write-reliability-windows.json](lsp-write-reliability-windows.json) に残しています。

## OS × 開始条件

一致率は期待バイト列と完全に一致した試行の割合です。処理が実行された率や、
意味的に正しいコードの率ではありません。p50 / p95 は失敗を含む全試行の値です。

| OS | 開始条件 | 一致 / 試行 | 一致率 | LSP p50 / p95 ms | 直接編集 p50 / p95 ms | 失敗の種類（件数） |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| Windows | `warm_indexed` | 6/57 | 10.5% | 273 / 297 | 0.50 / 1.23 | bom_added 51 |
| Windows | `daemon_stopped` | 6/57 | 10.5% | 12,929 / 13,338 | 0.56 / 1.27 | bom_added 51, text_changed 3 |
| Windows | `unindexed` | 6/57 | 10.5% | 512 / 560 | 0.62 / 1.21 | bom_added 51 |
| Windows | `first_download` | 6/57 | 10.5% | 13,453 / 14,443 | 0.55 / 1.32 | bom_added 51, text_changed 3 |
| Windows | **全条件** | 24/228 | 10.5% | 578 / 13,636 | 0.55 / 1.23 | bom_added 204, text_changed 6 |
| macOS | `warm_indexed` | 6/57 | 10.5% | 328 / 435 | 0.40 / 2.43 | bom_added 51 |
| macOS | `daemon_stopped` | 6/57 | 10.5% | 3,257 / 4,759 | 0.33 / 1.17 | bom_added 51, text_changed 3 |
| macOS | `unindexed` | 6/57 | 10.5% | 468 / 558 | 0.27 / 1.78 | bom_added 51 |
| macOS | `first_download` | 6/57 | 10.5% | 3,878 / 4,610 | 1.02 / 49.19 | bom_added 51, text_changed 3 |
| macOS | **全条件** | 24/228 | 10.5% | 569 / 4,231 | 0.37 / 19.87 | bom_added 204, text_changed 6 |
| Linux | `warm_indexed` | 6/57 | 10.5% | 242 / 259 | 0.07 / 0.10 | bom_added 51 |
| Linux | `daemon_stopped` | 6/57 | 10.5% | 3,631 / 3,778 | 0.06 / 0.10 | bom_added 51, text_changed 3 |
| Linux | `unindexed` | 6/57 | 10.5% | 354 / 391 | 0.06 / 0.09 | bom_added 51 |
| Linux | `first_download` | 6/57 | 10.5% | 4,045 / 4,281 | 0.07 / 0.11 | bom_added 51, text_changed 3 |
| Linux | **全条件** | 24/228 | 10.5% | 416 / 4,145 | 0.06 / 0.10 | bom_added 204, text_changed 6 |

## 禁止前に直すべき不具合

| Issue | 状態 | 実測 | 代表的な差分 |
| --- | --- | --- | --- |
| [#498](https://github.com/akiojin/unity-cli/issues/498) BOM の付加 | 未修正 | 3 OS × 4 条件で BOM なしの 17 シナリオ全試行 | 先頭に `EF BB BF` が付く |
| [#499](https://github.com/akiojin/unity-cli/issues/499) 末尾コメントの消失 | checkout で修正済み（PR #503）、公開版 `0.18.1` は未修正 | コールド 2 条件 × 3 OS で 3/3 | `public int Run(){return 2;} // 日本語の説明を保持` → `public int Run(){return 2;}public int Next(){return 3;} }` |
| [#500](https://github.com/akiojin/unity-cli/issues/500) ScriptableObject の `using` 欠落 | 修正済み（#502） | 再発 0 件 | （修正前）`using UnityEngine;` が出力されない |
| [#501](https://github.com/akiojin/unity-cli/issues/501) lspd 自動起動の 5 秒タイムアウト | 修正済み（PR #505、この測定の対象 `b5a4059f` には未反映） | macOS ローカル 19 試行中 1 件 | `lspd failed to start within timeout`、編集なし |
| [#504](https://github.com/akiojin/unity-cli/issues/504) Windows のコールド起動 | 修正中（PR #506） | Windows p50 約 13 秒（先行 run では約 43 秒） | 編集は成功するが待ち時間が長い |

測定ハーネス自体の不具合（macOS の大文字小文字を区別しないファイルシステムで
`Server` を自身へコピーして失敗）は、この PR で修正済みです。製品の不具合とは分けて扱います。

## 再現

.NET 10、Rust、Python 3.11 以上を使用します。Unity Editor は不要です。
リポジトリ直下から、対象 OS の RID に合わせて実行します。

```bash
cargo build --bin unity-cli
dotnet publish lsp/Server.csproj -c Release -r win-x64 --self-contained true -o .cache/lsp-reliability-publish
python scripts/lsp-write-reliability.py --unity-cli target/debug/unity-cli.exe --lsp-publish .cache/lsp-reliability-publish --out .cache/lsp-reliability --repetitions 20 --download-repetitions 1 --baseline tests/fixtures/lsp-write-baseline.json
```

macOS は `osx-arm64` / `osx-x64`、Linux は `linux-x64` / `linux-arm64` を
指定し、CLI パスから `.exe` を除きます。初回ダウンロードにはネットワーク接続が
必要です。既知の失敗も含めて収集するだけなら `--baseline` の代わりに
`--record-only` を指定します。観測した失敗は、どちらのモードでも失敗のまま保存します。

## シナリオと起動条件

全 10 ツールを 19 シナリオで測定します。

| 作業 | ツール / 入力 |
| --- | --- |
| MonoBehaviour / ScriptableObject / 通常クラスの新規作成 | `create_class` と `create_csharp_file` の各 3 種 |
| メソッド本体の置換 | `replace_symbol_body`、通常・namespace/partial/ジェネリクス/属性・日本語の末尾コメント |
| メンバー追加・削除 | `insert_before_symbol` / `insert_after_symbol` / `remove_symbol` |
| ファイル全体の更新 | `write_csharp_file` |
| 複数ファイルのリネーム | `rename_symbol`、別 namespace の同名型は変更しない |
| 日本語 / 絵文字 / UTF-8 / CRLF | BOM ありとなしの両方で `write_csharp_file` |
| 大きなファイル | 3,502 行、最後のフィールドを更新 |
| 構文エラーの修正・複数ファイル更新 | `apply_csharp_edits` |
| 構文検証のみ | `validate_text_edits`、ディスクが変更されないことも検証 |

| 条件 | 測定開始時の状態 |
| --- | --- |
| `warm_indexed` | 専用 lspd を起動し、そのプロジェクトで `build_index` を完了済み |
| `daemon_stopped` | LSP はインストール済み、lspd は停止。CLI の自動起動を含む |
| `unindexed` | lspd は起動済み、プロジェクトのインデックスは未構築 |
| `first_download` | LSP 未導入の専用 tools root。CLI が公開 LSP を実際に取得する |

各シナリオ・各試行で空の専用プロジェクトと tools root を使用します。
直接編集の oracle は独立した期待バイト列を書き込み、LSP 結果から生成しません。
大きな入力は `--params-file` を使い、Windows のコマンドライン長制限を避けます。
Windows の lspd はポート 6421 を共有するため、既存の利用者がいる場合は起動失敗として
記録し、他プロセスを停止しません。Unix は試行ごとの専用ソケットを使用します。

## 記録と判定

`results.json` に OS / RID / CLI SHA256 / LSP SHA256 と VERSION、試行数、
成功率、各試行の所要時間、失敗分類、CLI の生応答、期待結果との SHA256・バイト数・
unified diff を記録します。p50 / p95 は失敗を含む全試行の nearest-rank 値です。
直接編集はファイル書き込み時間、LSP は CLI 起動・パラメーター読込・LSP 呼び出しを含む
時間です。事前のデーモン起動・インデックス構築は `setup_ms` に分離します。

成功には CLI と payload の成功、`backend: lsp`、全 `.cs` ファイルの期待バイト列との
一致が必要です。文字列の見た目が同じでも BOM の追加・除去、改行変換を失敗に数えます。
`text_changed` は空白の差も含む厳密な差分で、意味的誤編集とは自動的に断定しません。
`lsp_startup_failure` / `timeout` / `download_failure` / `tool_rejected` / `wrong_backend` /
`other` と、`bom_added` / `bom_removed` / `newlines_changed` / `text_changed` /
`file_set_changed` を分け、複数の原因がある試行は各原因を記録します。

初回ダウンロードは公開済み LSP、インデックス構築済み/未構築は checkout の LSP を測ります。
デーモンの自動起動には LSP 自動更新があるため、この条件の実際の VERSION / SHA256 も
記録して区別します。Unity の AssetDatabase refresh、コンパイル、Editor
インポートの信頼性はこの測定の範囲外です。

目安は全シナリオ・全 OS で成功率 99% 以上、誤編集・エンコーディング/改行破壊 0 件です。
OS / 条件 / シナリオが欠ける結果は `INSUFFICIENT`、失敗がある完全なマトリクスは
`DO NOT ENFORCE` と報告します。少数試行での 100% は将来の 99% の保証にはなりません。
CI は PR / develop push の各 3 試行、週次と手動実行の既定は各 20 試行です。
PR / push は `Test` workflow から呼び出します。既存の必須
`LSP Tests (required)` は全 OS の測定完了を待ち、測定失敗時には明示的に失敗します。
依存 job の skip が成功扱いになる経路も `if: always()` と結果チェックで防ぎます。
デーモン停止・初回ダウンロードは PR / push で各シナリオ 1 試行、
週次・手動の既定は各 5 試行を別枠で記録します。このレポートの数値は、
コールド条件も一時的に各 3 試行とした PR の測定 run 1 回分です。Windows の
コールド起動は 1 試行あたり約 13〜46 秒かかるため、週次・手動の多試行を
完了できるよう測定 job の上限は 240 分です。
`--cold-repetitions` / `--download-repetitions` で増やせます。

既知の不具合は `tests/fixtures/lsp-write-baseline.json` にシナリオ単位で明記します。
これは合格率を補正する仕組みではありません。既知の失敗はレポート上も失敗です。
CI は baseline に含まれない失敗を終了コード 1 で拒否します。既知のテキスト差分も
正規化した内容の SHA256 を照合し、別の誤編集を隠しません。修正による失敗の解消は
許可します。baseline の変更は再現証跡と不具合 Issue を伴うレビュー対象です。
Issue #499 のコメント喪失修正は checkout 条件で回帰を拒否します。公開 LSP を使う
コールド条件では、実測した公開版 `0.18.1` の既知のテキスト SHA256 だけを
許容します。VERSION 不明・checkout・後続の公開版にはこの例外を適用しません。
無関係のファイルへの BOM 追加も regression として拒否します。
Issue #498 の BOM 修正後は checkout 条件の `bom_added` も regression として拒否します。
公開版 `0.18.1` の `bom_added` は、各シナリオの `published` 例外でのみ許容します。
再測定を絞る場合は `--scenarios create_plain` のように指定できます。絞った実行は
完全なマトリクスの代わりにならず、レポートの欠けたセル数に反映します。

## 付録: シナリオ × 開始条件 × OS

各セルは「期待バイト列と一致した試行 / 試行数」です。

| シナリオ | ツール | 開始条件 | Windows | macOS | Linux | 失敗の種類（3 OS 合計） |
| --- | --- | --- | ---: | ---: | ---: | --- |
| `create_class_monobehaviour` | `create_class` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_class_monobehaviour` | `create_class` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_class_monobehaviour` | `create_class` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_class_monobehaviour` | `create_class` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_class_scriptableobject` | `create_class` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_class_scriptableobject` | `create_class` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_class_scriptableobject` | `create_class` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_class_scriptableobject` | `create_class` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_class_plain` | `create_class` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_class_plain` | `create_class` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_class_plain` | `create_class` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_class_plain` | `create_class` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_plain` | `create_csharp_file` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_plain` | `create_csharp_file` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_plain` | `create_csharp_file` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_plain` | `create_csharp_file` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_monobehaviour` | `create_csharp_file` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_monobehaviour` | `create_csharp_file` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_monobehaviour` | `create_csharp_file` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_monobehaviour` | `create_csharp_file` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_scriptableobject` | `create_csharp_file` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_scriptableobject` | `create_csharp_file` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_scriptableobject` | `create_csharp_file` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `create_scriptableobject` | `create_csharp_file` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `replace_body` | `replace_symbol_body` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `replace_body` | `replace_symbol_body` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `replace_body` | `replace_symbol_body` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `replace_body` | `replace_symbol_body` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `write_file` | `write_csharp_file` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `write_file` | `write_csharp_file` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `write_file` | `write_csharp_file` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `write_file` | `write_csharp_file` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `insert_before` | `insert_before_symbol` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `insert_before` | `insert_before_symbol` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `insert_before` | `insert_before_symbol` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `insert_before` | `insert_before_symbol` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `insert_after` | `insert_after_symbol` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `insert_after` | `insert_after_symbol` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `insert_after` | `insert_after_symbol` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `insert_after` | `insert_after_symbol` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `remove_member` | `remove_symbol` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `remove_member` | `remove_symbol` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `remove_member` | `remove_symbol` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `remove_member` | `remove_symbol` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `namespace_partial_generic_attributes` | `replace_symbol_body` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `namespace_partial_generic_attributes` | `replace_symbol_body` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `namespace_partial_generic_attributes` | `replace_symbol_body` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `namespace_partial_generic_attributes` | `replace_symbol_body` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `replace_preserve_comment` | `replace_symbol_body` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `replace_preserve_comment` | `replace_symbol_body` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9, text_changed 9 |
| `replace_preserve_comment` | `replace_symbol_body` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `replace_preserve_comment` | `replace_symbol_body` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9, text_changed 9 |
| `rename_multifile` | `rename_symbol` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `rename_multifile` | `rename_symbol` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `rename_multifile` | `rename_symbol` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `rename_multifile` | `rename_symbol` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `unicode_no_bom_crlf` | `write_csharp_file` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `unicode_no_bom_crlf` | `write_csharp_file` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `unicode_no_bom_crlf` | `write_csharp_file` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `unicode_no_bom_crlf` | `write_csharp_file` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `unicode_bom_crlf` | `write_csharp_file` | `warm_indexed` | 3/3 | 3/3 | 3/3 | なし |
| `unicode_bom_crlf` | `write_csharp_file` | `daemon_stopped` | 3/3 | 3/3 | 3/3 | なし |
| `unicode_bom_crlf` | `write_csharp_file` | `unindexed` | 3/3 | 3/3 | 3/3 | なし |
| `unicode_bom_crlf` | `write_csharp_file` | `first_download` | 3/3 | 3/3 | 3/3 | なし |
| `large_3500_lines` | `write_csharp_file` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `large_3500_lines` | `write_csharp_file` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `large_3500_lines` | `write_csharp_file` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `large_3500_lines` | `write_csharp_file` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `repair_syntax_error` | `apply_csharp_edits` | `warm_indexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `repair_syntax_error` | `apply_csharp_edits` | `daemon_stopped` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `repair_syntax_error` | `apply_csharp_edits` | `unindexed` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `repair_syntax_error` | `apply_csharp_edits` | `first_download` | 0/3 | 0/3 | 0/3 | bom_added 9 |
| `validate_without_write` | `validate_text_edits` | `warm_indexed` | 3/3 | 3/3 | 3/3 | なし |
| `validate_without_write` | `validate_text_edits` | `daemon_stopped` | 3/3 | 3/3 | 3/3 | なし |
| `validate_without_write` | `validate_text_edits` | `unindexed` | 3/3 | 3/3 | 3/3 | なし |
| `validate_without_write` | `validate_text_edits` | `first_download` | 3/3 | 3/3 | 3/3 | なし |

Launch mode: autonomous

User Verification Result: n/a (autonomous)

Agent Visual Check: n/a (no UI surface)
