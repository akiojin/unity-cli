# Issue #549: Windows lspd の切断耐性と再起動診断

## develop の監査

2026-11-11 に fetch した `origin/develop` と作業開始時の HEAD はともに
`8da51f0af3db340d1e2782aa8946c4da7e7d250a` で、未提供のソースはなかった。
PR #551 (`69d1a0d`) に含まれる実装と証跡を AC ごとに確認した。

| AC | develop にある証拠 | 本変更で補う範囲 |
| --- | --- | --- |
| AC-1 | 接続処理エラーを隔離する実装、不正 JSON の回帰テスト、[Windows 10054 単発証跡](../issue-544/lspd-native-reset.json)。実切断の再実行可能な回帰は不足 | 実 TCP RST / 10054 と同一 listener の次 ping を固定 |
| AC-2 | 初回監査では status / doctor の理由・復旧手順が不足 | PR #556 でポート別 bind 診断と復旧後の消去を提供済み |
| AC-3 | [matrix.json](../issue-544/matrix.json) は Windows 7000.0.0a16 → 6000.3.28f1 → 6000.6.8f1 の連続実行が PASS。同一 `UNITY_CLI_LSPD_PORT=58284` を使用 | 既存の実機証跡を参照 |
| AC-4 | Unix は UnixListener を使用 | bind 診断と実 RST テストは Windows に限定。通常経路は全 OS で従来の `handle_stream` を渡す |

PR #556 の独立レビューで AC-1 の不足を再評価した。不正 JSON による一般的なエラー隔離と
単発の実機証跡を、実切断の継続回帰と同等とした初回判断を訂正する。
AC-2/3/4 は独立レビューでも pass となった。AC-1 の追加回帰は PR #557 で
develop に提供され、修正後の head に対する独立判定を別途確認する。

AC-3 の各 Editor の性能結果は
[7000.0.0a16](../issue-544/7000.0.0a16/lsp-performance.json)、
[6000.3.28f1](../issue-544/6000.3.28f1/lsp-performance.json)、
[6000.6.8f1](../issue-544/6000.6.8f1/lsp-performance.json) にある。
いずれも 6 ケース、各 5 回の計測、`failed_cases: 0`。
本変更の実機結果として再計上せず、develop に提供済みの AC 証拠として扱う。

## 方針

PM default ruling (override available): AC-2 が許容する診断による復旧を選ぶ。
既存のリスナーを共有する設定を追加せず、bind 失敗理由・endpoint・OS エラー・復旧手順を
tools root の `run/lspd/bind-error-<port>.json` に保存する。
`lspd status` と `lsp doctor` は同じ `startupError` を表示する。
成功した bind で当該ポートの診断を消去する。欠落・壊れた診断はコマンドを失敗させない。

AC-1 の follow-up では接続ハンドラを受け取る内部ループを用意し、通常経路には従来の
`handle_stream` を渡す。テストは accept 完了を channel で待ち、`SO_LINGER=0` で実 RST を送る。
実際の `handle_stream` のエラーから `ConnectionReset` / OS 10054 を観測した後、
同じ listener の次の ping、stop、ループの正常終了を検証する。待ち時間による同期は使わない。

## 検証

- RED: `cargo test --bin unity-cli bind_failure_is_diagnosable_and_cleared_after_successful_restart -- --test-threads=1`
  は修正前に endpoint がエラーに含まれないため失敗。記録: [bind-red.txt](bind-red.txt)。
- GREEN: `cargo test --bin unity-cli lsp::daemon::tests -- --test-threads=1`
  で bind 競合、診断表示、再 bind 後の消去、既存の接続隔離を確認。
  記録: [bind-green.txt](bind-green.txt)。
- 実 CLI: `cargo test --test lspd_bind_diagnostics -- --test-threads=1`
  で `lspd serve` の実 bind 競合後に `status` / `doctor` の 10048 と復旧手順を確認し、
  成功した再起動後に両コマンドから診断が消えることを確認。
  記録: [cli-diagnostics.txt](cli-diagnostics.txt)。
- AC-1 RED: 接続エラーを listener から伝播する既知の旧挙動を局所 mutation で再現し、
  `cargo test --bin unity-cli listener_survives_a_real_tcp_reset_and_answers_the_next_ping -- --test-threads=1 --nocapture`
  を実行。10054 の観測後に次の ping が失敗することを確認した。
  mutation は直後に除去し、コミットには含めない。記録: [rst-red.txt](rst-red.txt)。
- AC-1 GREEN: 既存のエラー隔離に戻し、
  `cargo test --bin unity-cli lsp::daemon::tests -- --test-threads=1 --nocapture`
  で 18 件 PASS。実 RST / 10054、次 ping、stop 成功を確認した。
  記録: [rst-green.txt](rst-green.txt)。
- 最終コミットの全品質ゲートは `verify.plan` / `verify.run` で実行し、結果を PR に記録する。
  C# 製品コードと Unity package は変更していない。

## 再開時の develop 再検証

2026-11-11 の再開時に Issue 本文・Tasks・Board を再読し、fetch 後の HEAD と
`origin/develop` がともに `36fcd1bed5307d10fb9acf17aa1d4ccd615c5cdc` であることを確認した。
未提供のコミット・製品ソースはなく、PR #556 と PR #557 はマージ済みだった。
PR #557 の最終 head `e798d3815dd6440640b071918ddffe5ba5f74a41` は base の祖先で、
その後 `src/lsp/daemon.rs` と `tests/lspd_bind_diagnostics.rs` に変更はない。

現行 develop の `verify.run` `vrr-2add450bbcf14006ad78ce2818cb70d9` は
次の 2 コマンド、計 19 テストが PASS した。

| AC | コマンド・証跡 | 確認内容 |
| --- | --- | --- |
| AC-1 | `cargo test --bin unity-cli lsp::daemon::tests -- --test-threads=1 --nocapture` | 18 件 PASS。実 RST による ConnectionReset / 10054 と、同一 listener の次 ping・stop・正常終了 |
| AC-2 | `cargo test --test lspd_bind_diagnostics -- --test-threads=1` | 1 件 PASS。10048、status / doctor の復旧手順、再起動後の診断消去 |
| AC-3 | 上記の 3 Editor の既提供性能 JSON | 各 6 ケース、各 5 計測、`failed_cases: 0`。今回の再実行結果とは扱わない |
| AC-4 | Windows に限定した差分と PR #557 の成功済み OS CI | UnixListener 経路を維持。PR #557 は 20 チェック成功・失敗ゼロ |

この再開で製品コードや回帰テストを重複変更せず、監査記録を更新する。
独立レビューは修正を含む head を対象とし、PR #556 / `fac0f70` の古い fail を
修正後の判定と混同しない。Issue の AC-1 更新と完了確定には、新しい独立判定と
`origin/develop` への提供を確認する。

修正後の PR #557 / `e798d38` は、[PM 手配の独立受け入れレビュー](https://github.com/akiojin/unity-cli/issues/549#issuecomment-6795332427)
で AC-1 PASS となった。実 RST 回帰、旧挙動の RED、必須 CI と Windows Tests の実行を
確認し、PR #556 / `fac0f70` に対する以前の fail を更新している。

User Verification Result: n/a (autonomous)

Agent Visual Check: n/a (no UI surface)
