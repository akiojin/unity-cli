# Issue #565 の実装検証

担当: サト（Codex）。開始時の `origin/develop`、HEAD、remote head は
`b99d60bd6f3270d36dc9156958a615bb087277f9` で一致していた。
既存 PR・Issue コメントはなかったため、この base に不足する半自動取得を実装した。

## Acceptance coverage

| 条件 | 実装・証跡 | 配信前の判定 |
| --- | --- | --- |
| AC-1 | 公開 My Assets メニューと `Window.Open`。ID は選択要求、解決不能な名前は My Assets 表示のまま JSON で `unresolved` を返す | 実装済み、実アセットの選択確認は AC-6 に残る |
| AC-2 | Editor 上の exact ID/name 検索、完全アーカイブ検査、指定期限、`ASSET_STORE_DOWNLOAD_TIMEOUT` / `waiting_download` | Rust / EditMode / E2E |
| AC-3 | `acquire` → 読み取り probe → 既存 import/status。各段階は独立コマンド。`importedAssets` を返す | Rust / 実 Editor E2E |
| AC-4 | `check_contract.py` と下記コードレビュー。Download・内部 API・reflection・入力イベント・アカウント token 読み取りなし | 自動チェック + コードレビュー |
| AC-5 | Rust CLI/schema/runner、Bridge handler、両側テスト、README/tools/配布スキルを更新 | lint / 回帰 |
| AC-6 | 実際の無料アセットで人が Download を押す証跡は取得していない | 未証明。合成キャッシュの E2E を代用しない |

受け入れ条件は配信まで `origin/develop` に存在しない。PR マージ後も AC-6 の
証跡を自動で完了扱いにせず、Issue は未完了の条件を残す。

## 公開 API の境界

`UnityPackageHandler.OpenAssetStore` は公開
`EditorApplication.ExecuteMenuItem("Window/Package Management/My Assets")`
で表示メニューを実行し、公開 `UnityEditor.PackageManager.UI.Window.Open`
で選択を要求する。Download のメニュー・ボタン・イベントは呼ばない。

Unity 6000.3.28f1 の公開ソースで、
[Window.Open](https://github.com/Unity-Technologies/UnityCsReference/blob/efa8fffa1235e83ebc71a52b49887135e2aa5602/Modules/PackageManagerUI/Editor/UI/PackageManagerWindow.cs)
は結果を返さず、対象がロードされているかは公開された結果から観測できない。
[My Assets 表示メニュー](https://github.com/Unity-Technologies/UnityCsReference/blob/efa8fffa1235e83ebc71a52b49887135e2aa5602/Editor/Mono/AssetStore/AssetStoreWindow.cs)
は Package Manager のページを開く。製品コードからこれらの内部実装は直接参照しない。

PM default ruling (override available): 表示専用の公開メニュー API を使い、
未解決の名前に架空の product ID を割り当てない。`selectionConfirmed:false`
を常に明示し、API への要求と選択完了を区別する。Download 済みの完全な一致は
再利用する。強制更新・購入・認証・ダウンロード自動化は実装しない。

コードレビュー範囲: `UnityPackageHandler.cs` の追加処理と共有 validator、
`src/app/package.rs` の orchestration、CLI/schema/runner の配線。
Bridge キャッシュ検索と既存 import は再利用し、通信先は既存の選択済み Bridge のみ。
Unity アカウントの token、内部 Package Manager 型、reflection、クリック等の入力送信、
独自 HTTP ダウンロードは追加していない。Bridge 自体の既存認証は維持する。

## Verification Report

Mode: full
Launch mode: autonomous
Changed surfaces: Interactive CLI, external binding (public Unity display API),
Bridge cache/import logic, docs, skill asset.
Acceptance Surface: CLI commands/JSON and existing native Editor integration.
HTML、UI レイアウト、テーマ、独自コントロールの変更はない。
ネイティブウィンドウを開く動作は画面付き実 Editor の E2E で検証する。

### Plan

```text
cargo fmt --all -- --check
cargo clippy --all-targets -- -D warnings
cargo test --all-targets -- --test-threads=1
cargo build --bin unity-cli
cargo run -- skills lint --severity error
python docs/verification/issue-565/check_contract.py
python scripts/e2e-package-import.py --editor "C:/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Unity.exe" --cli target/debug/unity-cli.exe --asset-store --port 6595 --output .unity/issue-565
```

変更 Markdown は markdownlint-cli 0.50.0 とリポジトリ設定で検査する。
`git diff --check origin/develop` も行う。LSP ソース・契約の変更がないため
`dotnet test` は対象外。ブラウザー UI は存在しないため Playwright は対象外。
最終 push 後にこの行列を `verify.plan` / `verify.run` で実行し、生成された
検証レコードと CI 結果を PR に記録する。

### Executed

最終コミット前の実行結果:

- Rust 全対象テスト、追加の acquisition テスト 8 件: PASS。
- fmt / clippy / skills lint（23 skills、0 violations）: PASS。
- contract checker（安全境界、typed CLI 4 ケース、raw schema 3 ケース）: PASS。
- 変更 Markdown の lint と差分 whitespace: PASS。
- Windows / Unity 6000.3.28f1: EditMode 15 ケースと画面付き E2E: PASS。
  [agent-check.json](agent-check.json) に全チェック名と対象ソースの SHA-256 を保存。

### Test Inventory

Rust:

- `asset_store_flow_opens_once_waits_then_reuses_import_and_status`
- `asset_store_wait_alone_never_opens_or_imports`
- `asset_store_wait_reconnects_after_read_disconnect`
- `asset_store_open_failure_is_not_retried_and_never_imports`
- `asset_store_rejects_missing_path_before_submitting_import`
- `asset_store_timeout_reports_manual_download_wait_without_import`
- `asset_store_rejects_ambiguous_package_and_invalid_deadline`
- `asset_store_download_timeout_is_bounded`
- 既存の package CLI、cache override、import/reload/auth/status 回帰テスト。

EditMode (`UnityPackageHandlerTests`、15 ケース):

- 不明/未完了/別名キャッシュ、曖昧名、完全な実 export の検出と非インポート。
- 実 export の最初の 20 バイトで止めた gzip の待機と import 拒否。
- batch mode の My Assets 拒否と、選択確認を偽装しない JSON。
- 既存のキャッシュ metadata、missing/corrupt/unsafe import、実 import のパスと idle。

E2E は Windows / Unity 6000.3.28f1 の隔離プロジェクトを使う。
My Assets を公開 API で開き、合成キャッシュの遅延到着、途中ファイル、期限切れ、
独立 wait、一括 import、実アセット内容、既存の local/cache import、script reload を
検証する。実際の Asset Store サーバーからの取得や人の Download 操作は行わない。
生成したシーン/アセット・ログ・NUnit XML/JSON は ignored な `.unity/issue-565/`
に保存する。

RED: Rust の `download_and_wait` 未実装でコンパイル失敗。
実 Editor では途中ファイルの probe が応答せず、タイムアウト結果に cache state が
残らない失敗も再現した。validator 単独では修正前のコードで 20 バイトの実 export
を拒否できることを確認し、誤って追加した gzip 末尾検査は取り下げた。
20 バイトの実 export の回帰テストは残した。画面付き Editor の Warning
ダイアログで Input System の native backends が無効と確認したため、隔離テスト
プロジェクトの `activeInputHandler:1` を既存 runner と揃えた。

User Verification Result: n/a (autonomous)
Agent Visual Check: n/a (no authored UI surface; native display is covered by headed integration)

Owner acceptance: AC-6 の手動 Download 証跡は未取得。Issue を閉じる根拠にはしない。
Overall: PASS (automated implementation slice; full owner acceptance is pending AC-6)
