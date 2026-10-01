# Issue #371 — 公式Unity CLI比較の検証

2026-10-02 JST、Apple M5 Max / 128 GiB / macOS 26.5 arm64。
変更対象は比較ドキュメント、計測用Python、記録JSON、READMEリンク。
製品のRust / C# / LSPコードは変更していない。

Launch mode: autonomous

User Verification Result: n/a (autonomous)

Agent Visual Check: n/a (no UI surface)

## 実測と受け入れ基準

公開値は認証対応を含む `6ca7f4542aad491a427cb6147b947680fbcc531f` の
CLI / Bridge 0.17.0開発ビルドを使用した。同じ隔離Unity 6000.3.25f1プロジェクトへ
Pipeline 0.8.0-exp.1を導入し、公式CLI 1.0.0-beta.11と交互に計測した。
公式CLIは公開配布マニフェストのSHA-256を確認してから実行した。

| AC  | 実行・成果物                                                                                                   | 結果                                                                            |
| --- | -------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------- |
| 1   | プロセス方式3操作、各ツール100回、前面・背面。生データとp50/p95を比較ページに掲載                              | PASS                                                                            |
| 2   | 英日各11行の機能表に公式URL・リポジトリ内出典。機械検査で全セルの出典を確認                                    | PASS                                                                            |
| 3   | 認証付きBridge ping=`pong`、公式editor_status=`ready`、同一プロジェクト・Unity版。認証なしpingは`UNAUTHORIZED` | PASS                                                                            |
| 4   | README 7言語すべての比較ページリンク                                                                           | PASS                                                                            |
| 5   | 親SPEC #157へSpec / Plan / Tasksを記録                                                                         | [記録](https://github.com/akiojin/unity-cli/issues/157#issuecomment-5936971952) |
| 6   | 常駐unityd対NDJSON shell、23操作×各100回、前面・背面。Play/Stopは状態確認まで含む                              | PASS                                                                            |
| 7   | 両方式の結果・意味の違いと旧結果/スタッフ調査との差を説明。速くない操作も省略しない                            | PASS                                                                            |
| 8   | 計測スクリプトと隔離プロジェクト準備・再実行手順                                                               | PASS                                                                            |

各実行は3回のウォームアップ後に100サンプルを記録し、合計10,400サンプル。
フォーカス変化による破棄は0、失敗応答・daemon再起動・直接TCPへのfallbackは0。
4実行でCLI、公式CLI、計測スクリプトのSHA-256が同一であることも検証した。
実行時のバイナリと公開JSONのハッシュ照合は、最終統合ビルド前に実施した。

| 方式 / focus          | 開始load1 | 終了load1 | 記録サンプル |
| --------------------- | --------: | --------: | -----------: |
| process / frontmost   |      8.42 |      8.88 |          600 |
| process / background  |      7.58 |     14.56 |          600 |
| resident / frontmost  |      5.51 |      7.88 |        4,600 |
| resident / background |      6.25 |     10.92 |        4,600 |

PM指定の開始条件はload1 < 9、他プロジェクトのrustc / テスト / Unity Editorに
CPU 50%超がないこと。担当間の直列化を待ち、条件未達時には開始しなかった。
実行中の負荷変動を隠さず、各JSONに開始・終了のloadと上位CPUプロセス名を記録した。
高負荷の予備計測は公開値に含めていない。履歴として残す2026-09-30の値は
v0.15.3の別データであり、今回の現行値として扱わない。

結果と生データへのリンクは [比較ページ](../comparison.md#results) に集約した。
検証コマンド、75件のテスト名・結果、4つの公開実行の概要、統合後試走、認証確認は
[verification.json](issue-371/verification.json) に保存した。資格情報は保存しない。

## 自動検証と最新developとの互換性

- `cargo build --release --bin unity-cli`: 計測用6ca7f45と統合後4028248でPASS。
- `UNITY_CLI_NO_AUTO_UPDATE=1 python3 -m unittest discover -s tests/scripts -v`:
  75件、74 PASS、既存の任意性能テスト1件skip、失敗0。
  スキップは`UNITY_CLI_PERF_TEST_BINARY`を指定した場合だけ動く別の性能ゲート。
- 新規15件: NDJSONプロセス再利用、応答ID、失敗応答、タイムアウト、eval完了、
  material/readの実結果、23操作集合、同一C#ファイル、Play状態待機、
  環境・認証設定の隔離、同一プロジェクト/Unity版、新旧JSON形式。
- 新規5件: 全生サンプルとnearest-rank百分位、版/ハッシュ/開始負荷、表の全数値一致、
  全README・相対出典、英日全機能行の出典。
- 最新develop `4028248`（#447/#448/#450）統合後に再ビルドし、隔離Bridgeも更新。
  両方式×両focusを各1回再実行し、認証と新JSON envelopeで全操作PASS。
  この短い試走は公開性能値とは別に記録し、公開値のコミットを書き換えていない。
- 最初の統合後Python実行は再ビルドと並行したため、旧バイナリを読むテスト1件が失敗。
  ビルド完了後に全75件を再実行して解消。製品コードや判定条件の変更は不要だった。

- `pnpm exec markdownlint docs/comparison.md docs/verification/issue-371.md README*.md --config .markdownlint.json`: PASS。
- `pnpm exec prettier --check docs/comparison.md docs/verification/issue-371.md docs/verification/issue-371/verification.json docs/benchmarks/official-*.json`: PASS。
- `git diff --check`: PASS。

公開表とJSONの整合性は最終整形後にも再検証した。
Rust/C#製品コードやLSPの変更はないため、別担当が実施した製品全体の回帰件数を
この変更で新たに実行した検証としては計上しない。

## 限界

1台・1プロジェクト・1バージョンの実測で、全操作の優位性を主張しない。
例として前面の常駐Play完了待ちはunity-cli 552.16ms、公式266.35ms（p50）。
ファイル読取の通信経路、返却項目、画像取得、状態確認フラグには実装差がある。
旧スタッフ調査の約550msの原因は、切り分けたトレースがないため断定しない。
ディレクトリ掲載申請はオーナー判断の別スコープ。
