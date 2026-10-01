# Issue #441 — CLI JSON envelope と終了コード

2026-10-02 JST、macOS 26.5 / Apple Silicon。初回の Rust 検証対象は `aa726e7`。
Editor はすべて隔離プロジェクトで起動し、検証が所有するプロセスだけを終了した。

## develop 統合後の再検証

`origin/develop` の `03545ea`（#447 の認証、#448 のライフサイクル）を
`9c6a065` で統合した。以下は初回検証とは別に実行した結果で、
詳細は [post-merge.json](issue-441/post-merge.json) に記録する。

- Rust 623 tests PASS、fmt / clippy PASS。Python 55 tests（既存の 1 skip）、
  .NET 53 tests PASS、skills lint 23 skills / 0 violations。
- 両 Editor で smoke、Domain Reload、all-tools、screenshot が PASS。
  AC-3/4 の実 Bridge 応答、認証の EditMode テスト、daemon の認証、
  各 5 cycles / 16 checks の再起動検証も PASS。自動検出は 19/19 checks PASS。
- 直接 TCP を使用する E2E は、所有する Editor の private lockfile から認証情報を取得する。
  検証結果には token を保存せず、Editor 検出ログも token を除外する。
  自動検出 E2E の daemon も専用 tools root で起動・終了し、
  既存 daemon が別の認証 registry を参照する状態を避けた。
- 認証必須化後、`instances list` / `set-active` のヘルスチェックだけが token を
  送信せず、稼働中 Editor を停止中と判定する問題を再現した。
  通常の通信と同じ token resolver を使用する修正と、一覧・選択の回帰テストを追加した。
- 認証 E2E の eval 結果参照を `data.value` に更新した。
  screenshot の一時的な通信失敗では既存リトライが動くよう、
  `CalledProcessError` を維持した。
- opt-out の検証は専用 fixture に限定し、次 minor の廃止警告も確認した。
  その他の統合後検証は認証を有効にしたまま実行した。

## 受け入れ基準

| AC | 検証 | 結果 |
| --- | --- | --- |
| 1 | 未使用ポートへの ping | `EDITOR_UNREACHABLE`、exit 7 |
| 2 | 不正 JSON / clap 引数 / batch 引数 | `INVALID_ARGUMENT`、exit 2 |
| 3 | 実 Editor の EditMode テストを完了まで poll | 失敗を含む場合 8、全成功 0（両バージョン） |
| 4 | 実 Bridge の `get_scene_bake_status` に存在しない jobId | wire と同じ `JOB_NOT_FOUND`、exit 6（両バージョン） |
| 5 | モック Bridge、直接 TCP と daemon の両経路 | `UNAUTHORIZED`、exit 3 |
| 6 | 下記の既存 JSON consumer E2E | PASS |
| 7 | docs/tools.md、unity-cli-usage、全 Unity skills | envelope / 分岐ガイド更新、lint 23 skills / 0 violations |

実 Editor の応答・終了コードは [acceptance.json](issue-441/acceptance.json)、
実行コマンドと各 suite の結果は [compatibility.json](issue-441/compatibility.json) に保存した。
後者の `source_report` / `source_log` は実行ホストの詳細ログを指す。

## 検証範囲

以下は develop 統合前の全 consumer 検証記録。

- Rust: 607 tests PASS（うちプロセス単位の新規 JSON 契約テスト 15 件）。
  `cargo fmt --all -- --check`、`cargo clippy --all-targets -- -D warnings` PASS。
- Python: 52 tests、既存の 1 skip、失敗 0。.NET: 53 tests PASS。
- Unity 6000.3.25f1 / 2022.3.62f3 の両方で input、timeline、VFX、eval、hot-reload
  （外部プラグイン未導入時の応答）、Domain Reload、bake、macOS Player build、video formats、
  animation curves、Input Actions 永続化、reference、all-tools、screenshot、smoke、
  test-results、skills-install が PASS。
- all-tools: 各バージョン 134 呼び出し、失敗 0。LSP 性能検証も PASS。
  122 種類のツールを呼び出した。専用 fixture が必要なツールなどは既存の skip 条件を維持した。
- Prefab: 各 45 checks。Audio: 各 16 NUnit tests と作成・再読込。
  uGUI: 両バージョンで依存宣言あり / なしの構成を検証。
- unityd: 各 9 checks。Editor 再起動: 各 5 cycles / 16 checks。
  複数 Editor 検出: 19 checks。
- skills-install: 新しい Claude Code セッションからインストール済み skill を使用し、
  隔離 Editor でシーンの作成・保存・再取得を確認。
- screenshot: Edit / Play、UI 合成、カメラのみ、リサイズ、base64、フォーカス不足エラーを確認。
  `ScreenshotHandlerTests` は各 5/5、最終 Console error count は 0。
- Windows ビルドはモジュール不足を既存 harness が `UNSUPPORTED` と判定した。
  Windows/Linux は Issue の裁定どおり #386 の範囲。

## 検証中に見つけた差分と再実行

- Domain Reload / screenshot の暗黙の text 出力を明示的な `--output json` に変更した。
- video の入力エラー、screenshot のフォーカス不足、LSP 性能検証の `backend` / `version`
  参照を新しい envelope に対応させ、各失敗を実 Editor で再検証して PASS を確認した。
- 初回の Unity 6 eval soak は約 66 MiB で既存の 64 MiB 上限を超えた。
  Editor 再起動後は 13,635,584 bytes、1000 回の評価を含む 18 checks が PASS。
  上限や Unity 側のコードは変更していない。
- GUI 起動時のインポート、通知ダイアログ、テスト開始時の一時的失敗があった。
  所有する Editor を再起動して再検証し、最終の screenshot / smoke / test-results は PASS。
  all-tools は通常の batch host で再実行した。

User Verification Result: n/a (autonomous)。
Agent Visual Check: n/a (CLI 出力契約の変更)。上記 GUI E2E は既存 consumer の回帰検証。

## 再実行

AC-3/4 は `python3 scripts/e2e-json-envelope.py` で両 Editor の隔離検証を実行できる。
既存 suite は `scripts/e2e-matrix.py` と各 `scripts/e2e-*.sh/py` を使用する。
前提ツールと起動方法は [Local Unity E2E](../development.md#local-unity-e2e) を参照。

共通 API は `crate::core::failure`（`crate::failure` からも再 export）と
`crate::app::output::envelope`。#443 へ `aa726e7` を先行共有済み。
