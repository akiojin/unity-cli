# Issue #324 — 全ツール E2E と daemon 応答

## 変更と再現

- カタログ146件を、実行対象122件と理由付き除外24件に照合する。
  `scripts/e2e-all-tools-exclusions.json` の除外は PASS に数えない。
- E2E の待機処理は、通信失敗や状態のない応答を準備完了と扱わない。
  Play Mode 終了後に asset refresh とコンパイル終了確認を行う。
- `quit_editor` は成功応答を送信し終わってから Editor の終了を予約する。
  修正前のテストでは、応答送信前に `delayCall` が登録されて失敗した。
- unityd の接続を再利用するときも、各要求のタイムアウトを反映する。
  20ms の接続を再利用した2秒要求が、100ms 遅延応答を待てないことを再現した。
- LSP のツール応答待ちを10秒から180秒へ変更する。11秒遅延応答の
  回帰テストで、修正前の macOS EAGAIN と修正後の成功を確認した。
- symbol/reference 検索の `scope` を LSP に渡し、対象フォルダーだけ走査する。
  `assets` 指定でも PackageCache 全体を毎回解析していた処理を修正した。
- 検証中に見つけた VFX の Unity 6 ID 互換修正は PR #335 に分離してマージ済み。

## ローカル検証

2026-09-30、macOS 実機、描画可能な隔離 Editor プロジェクトで検証。
ログと NUnit XML は `/tmp/issue324-evidence/` に保存した。

| 検証 | 結果 |
| --- | --- |
| `cargo fmt --all -- --check` | PASS |
| `cargo clippy --all-targets -- -D warnings` | PASS |
| `cargo test --all-targets -- --test-threads=1` | 460 unit + 10 integration PASS |
| `cargo run -- skills lint --severity error` | 16 skills、違反0 |
| `dotnet test lsp/Server.Tests.csproj` | 50 PASS |
| Unity 6000.6 HostConnection / ObjectIdentity | 14 PASS |
| Unity 6000.4 ObjectIdentity | 3 PASS |
| Unity 6000.6.3f1 全ツール E2E | 122/122対象ツール、133呼び出し PASS、失敗0 |
| Unity 6000.6.3f1 LSP 性能 | 6ケース × 5回、全閾値 PASS |
| Unity 6000.4.11f1 全ツール E2E | 122/122対象ツール、133呼び出し PASS、失敗0 |
| Unity 6000.4.11f1 LSP 性能 | 6ケース × 5回、全閾値 PASS |

6000.6 の最終ログ: `/tmp/unity-cli-e2e-all-tools-20260930-001707.log`。
6000.4 の最終ログ: `/tmp/unity-cli-e2e-all-tools-20260930-001849.log`。
初回測定には並行コンパイル中の閾値超過、ソース更新に伴う再ロード中の失敗、
接続プールのタイムアウト継承による失敗があった。最終実行は修正済み CLI と
daemon を使い、両 Editor の再起動・コンパイル終了後に順番に実行した。

追加した検証項目:

- `all_tools_e2e_covers_catalog_or_documents_exclusion`
- `all_tools_e2e_does_not_treat_disconnection_as_ready`（エラー応答拒否・正常応答受理）
- `tool_response_can_arrive_after_ten_second_transport_timeout`
- `pooled_connection_uses_each_requests_timeout`
- `SymbolQueries_RespectAssetsScopeBeforeScanningPackages`（symbol / references）
- `EnumerateUnityCsFiles_SelectsRequestedRoots`（6 scope ケース）
- `QuitEditor_DoesNotScheduleExitBeforeResponseIsSent`

## 実機 E2E の実行方法

この checkout の CLI と LSP をビルドし、隔離した tools root に配置する。
各バージョンの Editor を専用プロジェクトとポートで起動し、
`get_compilation_state` の `isCompiling=false` / `isUpdating=false` を確認する。
各 Editor に対して以下を実行する（6性能ケース、各5回の測定を含む）。

```bash
UNITY_CLI_TOOLS_ROOT=/tmp/issue324-tools UNITY_CLI_NO_AUTO_UPDATE=1 \
  UNITY_PROJECT_ROOT=/tmp/issue324-6000.6.3f1 \
  scripts/e2e-all-tools.sh --unity-cli "$PWD/target/debug/unity-cli" --port 6498
UNITY_CLI_TOOLS_ROOT=/tmp/issue324-tools UNITY_CLI_NO_AUTO_UPDATE=1 \
  UNITY_PROJECT_ROOT=/tmp/issue324-6000.4.11f1 \
  scripts/e2e-all-tools.sh --unity-cli "$PWD/target/debug/unity-cli" --port 6499
```

Windows 実機検証（AC-6）は Issue の指定どおり保留。実施済みとは扱わない。
User Verification Result: n/a (autonomous)。Agent Visual Check: n/a (no UI surface)。
