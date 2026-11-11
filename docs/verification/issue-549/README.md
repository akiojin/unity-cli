# Issue #549: Windows lspd の切断耐性と再起動診断

## develop の監査

2026-11-11 に fetch した `origin/develop` と作業開始時の HEAD はともに
`8da51f0af3db340d1e2782aa8946c4da7e7d250a` で、未提供のソースはなかった。
PR #551 (`69d1a0d`) に含まれる実装と証跡を AC ごとに確認した。

| AC | develop にある証拠 | 本変更で補う範囲 |
| --- | --- | --- |
| AC-1 | `serve_listener` が接続処理エラーを隔離する回帰テストと [Windows 10054 実機証跡](../issue-544/lspd-native-reset.json) | 既存の接続隔離を維持 |
| AC-2 | bind は OS エラーを返すが、status / doctor には理由・復旧手順が出ない | Windows のポート別 bind 診断と復旧後の消去 |
| AC-3 | [matrix.json](../issue-544/matrix.json) は Windows 7000.0.0a16 → 6000.3.28f1 → 6000.6.8f1 の連続実行が PASS。同一 `UNITY_CLI_LSPD_PORT=58284` を使用 | 既存の実機証跡を参照 |
| AC-4 | Unix は UnixListener を使用 | 追加する製品コードをすべて `cfg(not(unix))` で限定 |

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
- 最終コミットの全品質ゲートは `verify.plan` / `verify.run` で実行し、結果を PR に記録する。
  C# 製品コードと Unity package は変更していない。

User Verification Result: n/a (autonomous)

Agent Visual Check: n/a (no UI surface)
