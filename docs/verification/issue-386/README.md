# Issue #386: Windows 実機検証と残る検証条件

対象: [Issue #386](https://github.com/akiojin/unity-cli/issues/386)。計測日時は各 JSON の UTC 時刻を参照。
Windows 11 x64 / Unity 6000.4.4f1 / CLI・Bridge 0.18.1 を使用した。
CLI は base `338f426119b61123567b74028e7e8fe3bd99caa5` に本変更を加えた開発ビルドで、
公開 Release 0.18.1 のバイナリも別に導入して接続を確認した。
[環境・ソースファイル・バイナリの SHA256](environment.json) で区別できる。
MCP profile 修正後の [最終ソース・バイナリ SHA256](environment-final.json) は別に収録した。
先に測定した Windows 操作にはその測定時点のバイナリ SHA256 を残している。

**Overall: FAIL — Issue 全体は未完了。PR は未作成。**
Windows の成功を Linux、通常の Domain Reload、winget 導入や原資料の全シナリオへ拡張しない。

## 受け入れ基準の実測状況

| AC | 結果 | 証跡・残る条件 |
| --- | --- | --- |
| 1: Windows lockfile / PID / 自動発見 | PASS | 実 Editor が `%USERPROFILE%\.unity-cli\editors` に発行した [lockfile（認証情報除去）](lockfiles.json)、`tasklist`、[自動発見](fresh-setup-summary.json)。 |
| 2: 実 setup / Bridge 導入・接続 | PASS | 空の隔離プロジェクトで `setup --launch-editor --wait-secs 420` を実行。40.549秒、exit 0、`projectMatches: true`。[結果](setup.stdout.json)、[実行時間と ping](fresh-setup-summary.json)。dry-run ではない。 |
| 3: 証跡の Issue / PR 記録 | PASS | 本資料と [Issue コメント](https://github.com/akiojin/unity-cli/issues/386#issuecomment-5970471700) に成功・失敗・未検証条件を記録。全件ゲート未通過のため PR 未作成。 |
| 4: Windows / Linux OS フォールバック | Windows PASS / Linux 未検証 | 実 Editor の `EditorUtility.DisplayDialog` でメインスレッドを止め、PowerShell + GDI の OS キャプチャが成功。Linux の実 Editor と Wayland/X11 キャプチャツールは本環境で揃わない。 |
| 5: スタッフ原資料の全シナリオ・比較表 | 部分 | 公開済み23操作は既存比較と同じ設定で23/23 PASS。[一覧](staff-final-operations.json)。原資料 `pm-scratch/staff-report.md` は未取得。公式 CLI の Windows 比較・原資料との網羅性確認は未実施。 |
| 6: 公開 install.ps1 / winget / ping | PowerShell PASS / winget 未検証 | 公開 installer で Release 0.18.1 を隔離 tools root へ導入し、実 Editor に ping 成功。winget source に `akiojin.unity-cli` がなく、repository に `WINGET_TOKEN` もない。 |

## 実機で発見して修正した問題

1. Windows では `Command::spawn` が CLI の標準出力ハンドルを Editor に継承させ、
   呼び出し元の `Command::output` が Editor 終了まで待つ。
   setup、editor open、test の起動を既存 `spawn_detached` に統一した。
   `setup_launch_returns_output_while_editor_is_still_running` は修正前に
   `setup output waited for the Editor to exit` で失敗し、修正後に成功した。
   実 Editor の新規 setup も40.549秒で戻り、Editor は起動したまま接続できた。
2. Windows の `canonicalize` が返す `\\?\` / `\\?\UNC\` を Unity の起動引数に渡すと、
   この Editor では正しく起動できなかった。Unity に渡す project / log パスを通常形式へ変換した。
   `editor_launch_uses_paths_supported_by_unity` は local / UNC の両方で修正前に失敗し、修正後に成功した。
   CLI の自動 headless 起動による実 Unity テストと GUI 起動の両方でも確認した。
3. Windows の `Path.GetDirectoryName` は `\` を返す。
   シーンの作成・保存は `/` で分割して AssetDatabase のフォルダを作るため失敗していた。
   AssetDatabase に渡す前に区切り文字を `/` に揃えた。
   深いフォルダへの作成・保存の回帰テストを追加し、実 Unity で RED → GREEN を確認した。

Windows の clippy で検出された既存の Unix 専用テストの未使用 import / helper には
`cfg(unix)` を付けた。LSP の動作変更はない。

4. 全件検証で MCP configure の Windows profile 解決も確認した。
   `dirs::home_dir` の Windows shell API はプロセスの `USERPROFILE` 上書きを参照しないため、
   Windsurf の設定テストが実ユーザープロファイルへ書き込んでいた。
   Windows では明示した `USERPROFILE` を優先し、未設定なら従来の解決方法に戻すよう修正した。
   テストは dry-run の出力先と、指定先で更新された `command` を確認するよう強化した。
   `windsurf: preview must target the isolated configuration directory` の RED を確認し、
   修正後は MCP / auth warning / tool list の11件が成功した。
   今回生成された実プロファイルの設定ファイルは、内容・SHA256・作成時刻で検証実行の生成物と
   確認して削除した。[削除前のメタデータと削除結果](profile-audit.json)。

## Windows OS キャプチャ

実 Editor PID 44232 に同期 `EditorUtility.DisplayDialog("Issue386FallbackProbe", ...)` を開き、
その PID 所有のモーダルウィンドウが存在することを確認してから実行した。

```text
unity-cli --project-path <isolated-project> --timeout-ms 1000 raw capture_screenshot --json '{}'
unity-cli --project-path <isolated-project> --timeout-ms 1000 raw capture_screenshot --json '{"osFallback":false}'
```

前者は exit 0、`fallback: "os"`、PNG 1512 × 949。
後者は exit 6、`TIMEOUT`。[実行記録と PNG SHA256](modal-summary.json)、
[成功レスポンス](modal-os-fallback.stdout.json)、[無効化時のエラー](modal-fallback-disabled.stdout.json)。
PNG 自体はローカルの隔離プロジェクトに保持し、この証跡には寸法と SHA256 を収録した。
終了時は対象 PID の検証用ダイアログだけを閉じた。

## 公開23操作

`scripts/bench-editor-ops.py::operations()` の23操作を使用した。
シーンとアセットは隔離プロジェクトの `Assets/Scenes/Generated/E2E/Performance` に作成した。
これは各操作1回の動作確認であり、100サンプルの速度比較や前面・背面のベンチマークではない。

最初の試走は Input System の restart ダイアログでアセット3操作が停止した。
Editor を閉じた状態で setup により `activeInputHandler: 2` に変更し、再試走した。
初回 setup 用の最小プロジェクトにも、初回 ping 後に同じ Input System の警告が開いた。
初回接続の成功はその時点の結果であり、設定ファイルのない最小プロジェクトを
継続して無人運用できることまでは主張しない。検証終了時に警告を確認して閉じた。
その通常 Domain Reload 有効の試走では21/23が成功し、Play 後の状態ポーリングに
`OPERATION_FAILED: early eof`、直後の Stop に `UNAUTHORIZED` が発生した。
後続の Stop により検証用 Play Mode は終了した。[最初の結果](first-operations.json)、[通常設定の結果](operations.json)。

[既存比較資料](../../comparison.md) と同じ `DisableDomainReload` / `PlayUnfocused` にすると、
23/23が成功した。[設定変更と以前の値](staff-play-settings.stdout.json)、
[全呼び出しの終了コード・時間](staff-final-summary.json)、[操作別結果](staff-final-operations.json)。
通常 Domain Reload 中の接続エラーを修正済みとは扱わない。
ローカル C# 読取は embedded package の `Packages/com.akiojin.unity-cli-bridge/Editor/Core/UnityCliBridgeHost.cs` を使用した。

## 配布経路

公開スクリプト `https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1` を
Windows PowerShell 5 の `irm ... | iex` で実行した。
`UNITY_CLI_TOOLS_ROOT` を隔離先に設定し、`UNITY_CLI_SKIP_PATH_UPDATE=1` でユーザーの PATH 更新を省いた。
Windows PowerShell 5 の module path はプロセス内でその `$PSHOME\Modules` に設定した。
Release 0.18.1 の Windows バイナリを SHA256SUMS 検証後に導入し、
[version](release-version.json) と [実 Editor ping](fresh-setup-summary.json) が成功した。
[公開 Release のアセット](release.json) と [バイナリの SHA256](environment.json) を収録している。

[winget show](winget.json) は「入力条件に一致するパッケージが見つかりませんでした」。
[repository secret の名前一覧](repository-secret-names.json) に `WINGET_TOKEN` はない。
トークン値は読み取っていない。winget での install / ping、初回公開提出とトークン設定は未実施。

## Verification Report

Mode: full。Launch mode: interactive (`launch_route: manual`)。
Baseline: `origin/develop` / HEAD `338f426119b61123567b74028e7e8fe3bd99caa5`。
Changed surfaces: interactive CLI、Editor 外部連携、C# シーン作成・保存、テスト条件、docs。
Detected runners: Cargo、Unity Test Framework。Playwright と LSP の dotnet test は対象外。

直接実行の結果:

| コマンド | 結果 |
| --- | --- |
| `cargo build --bin unity-cli` | PASS（Windows native） |
| `cargo clippy --all-targets -- -D warnings` | PASS（Windows native、Unix 専用 import の条件修正後） |
| `cargo test --test setup_dry_run -- --test-threads=1` | PASS、2件 |
| `cargo test editor_launch_uses_paths_supported_by_unity -- --test-threads=1` | PASS、1件 |
| `unity-cli test --mode editmode --filter SceneHandlerTests ...` | PASS、11件。[JUnit GREEN](green-results.xml)。修正前は11件中3件失敗。[JUnit RED](red-results.xml)。 |
| `cargo test --all-targets --no-fail-fast -- --test-threads=1` | FAIL。最終修正で **605 PASS / 1 FAIL**。失敗は `json_envelope::skills_show_and_lint_use_envelopes` のみ。[結果](final-checks.json)、[全件ログ](cargo-all-final.txt)。 |
| `unity-cli skills lint --severity error` | FAIL。checkout の skill リンク46件が通常ファイルになっている。 |

全件ゲートの失敗は `core.symlinks=false` の Windows checkout が Git の symlink を
リンク先文字列の通常ファイルとして展開したことによる R20 / R21 エラー。
Git blob と内容一致を確認した上で実リンクへの復元を試したが、`WinError 1314`（権限不足）で失敗し、
リンク変更は行われなかった。lint の抑制や Developer Mode / セキュリティ設定の変更は行っていない。

正式な `verify.plan` / `verify.run` には fmt、clippy、全 targets の単一スレッド test
（`--no-fail-fast` で全 suite の結果を収集）、skills lint、実 Unity の SceneHandlerTests を登録した。
正式 run は [300秒待機](verify-run.json)、[1500秒待機](verify-run-second.json) の両方で
他プロジェクトの host-wide lease により `deferred`。コマンドは正式 run からは未実行で、
Verification Run Record は未生成。lease 保有者には介入していない。
上表の最終 Cargo 結果は通常の直接実行で、正式記録と区別する。

Test Inventory: [Cargo 全606件のテスト名・suite・結果](cargo-test-inventory.json)。
Unity の実行テスト名は JUnit に全11件を収録。
追加 Rust テストは `app::editor::tests::editor_launch_uses_paths_supported_by_unity` と
`setup_launch_returns_output_while_editor_is_still_running`。
最終バイナリの自動 headless 起動でも SceneHandlerTests 11/11 が成功した。[最終実行ログ](unity-final.txt)。
`mcp::configure_all_clients_preserves_other_servers_and_dry_run` は出力先と更新内容を確認するテストに強化した。

User Verification Result: pending。自動実行の成功をユーザー確認済みとは扱わない。
自動ゲートが失敗しているため、追加のユーザー確認を Ready 判定に使わない。
Agent Visual Check: n/a（CLI と Editor 連携を検証。Web/TUI の表示変更なし）。

## 再開条件

1. Windows checkout の正規 skill リンクを作成できる実行環境で、全件 test / lint を再実行する。
2. 原資料 `pm-scratch/staff-report.md` の所在を確認し、公開23操作との差分を検証する。
3. Linux の実 Unity Editor と Wayland / X11 のキャプチャ環境で AC-4 を実行する。
4. winget 初回公開の担当と `WINGET_TOKEN` の設定を確定し、公開 source から install / ping を検証する。
5. 共通検証枠を確保し、正式な自動ゲートと必要なユーザー確認が通ってから Ready PR を作成する。
