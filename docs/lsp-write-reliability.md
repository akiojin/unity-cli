# C# LSP 書き込みの信頼性測定（#490）

直接編集を禁止できる根拠は、現時点では得られていません。BOM なしの
ファイルへの BOM 追加を Windows の実 CLI / 実 LSP で検出しています。
全 OS の測定結果は `LSP Write Reliability` CI の JSON / Markdown artifact に
保存します。これは禁止を導入する変更ではなく、オーナー判断のための測定です。

## 初期測定と修正が必要な不具合

2026-10-13 の Windows / checkout LSP / `warm_indexed` 初期測定は
19 シナリオを各 3 回、計 57 試行です。期待バイト列と一致したのは 6 試行
（10.53%）。BOM あり CRLF の更新と検証のみが一致しました。
全試行の LSP p50 は 260.755 ms、p95 は 293.267 ms です。
これは厳密なバイト一致率であり、処理が実行された率や意味的に正しいコードの率ではありません。

| 不具合 | 実測 | 禁止前に必要な修正 |
| --- | --- | --- |
| BOM なしのファイルへ BOM を追加 | BOM なしの変更シナリオすべてで EF BB BF を追加 | 既存の BOM / 改行を保存し、新規ファイルの規約を定める |
| `replace_symbol_body` が末尾コメントを消す | 日本語コメントと改行が 3/3 消失し、次のメンバーと連結 | 置換前 Body の末尾 trivia を保持する |
| `create_class` の ScriptableObject 作成が using を欠く | `using UnityEngine` が 3/3 欠落 | 非修飾 ScriptableObject が名前解決できるテンプレートにする |

生応答とバイト差分は [Windows 初期測定の証跡](lsp-write-reliability-windows.json) に保存しています。
製品修正は個別の不具合 Issue として PM に登録を依頼しています。
3 OS / 全起動条件の結論は CI artifact の完成したマトリクスを参照してください。

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
デーモン停止・初回ダウンロードは各シナリオ 1 試行を別枠で記録します。
`--cold-repetitions` / `--download-repetitions` で増やせます。

既知の不具合は `tests/fixtures/lsp-write-baseline.json` にシナリオ単位で明記します。
これは合格率を補正する仕組みではありません。既知の失敗はレポート上も失敗です。
CI は baseline に含まれない失敗を終了コード 1 で拒否します。既知のテキスト差分も
正規化した内容の SHA256 を照合し、別の誤編集を隠しません。修正による失敗の解消は
許可します。baseline の変更は再現証跡と不具合 Issue を伴うレビュー対象です。
無関係のファイルへの BOM 追加も regression として拒否します。
再測定を絞る場合は `--scenarios create_plain` のように指定できます。絞った実行は
完全なマトリクスの代わりにならず、レポートの欠けたセル数に反映します。

Launch mode: autonomous

User Verification Result: n/a (autonomous)

Agent Visual Check: n/a (no UI surface)
