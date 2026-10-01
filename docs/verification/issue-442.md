# Issue #442 — プロジェクト定義のカスタムツール

macOS / Apple Silicon の隔離プロジェクトで検証する。既存の Editor と daemon は
使用せず、harness が所有する Editor だけを終了する。

## 結果

2026-10-02 JST、Unity 2022.3.62f3 / 6000.3.25f1 ともに
42 checks、EditMode 21 tests が PASS。
[2022 summary](issue-442/2022-summary.json) と
[6000 summary](issue-442/6000-summary.json) に検証対象のハッシュを記録した。
同じディレクトリに各バージョンの transcript と NUnit XML を保存している。

- Rust 全 639 tests PASS、行カバレッジ 92.75%（要求 90%）。
  `cargo llvm-cov --all-targets --summary-only --fail-under-lines 90 -- --test-threads=1`
- fmt / clippy PASS、skills lint 23 skills / 0 violations、markdownlint PASS。
- .NET 53 tests PASS、Python 55 tests（既存の 1 skip）、失敗 0。
- 公開型は `ddafc93`、動的 discovery 実装は `2a84ee8` で先行 push し、#444 に共有済み。
  最終検証はその後のオフライン回帰修正を含む。

## 再現コマンド

```bash
cargo build
python3 scripts/e2e-custom-tools.py --version 2022.3.62f3 \
  --cli target/debug/unity-cli --output /tmp/custom-tools-2022
python3 scripts/e2e-custom-tools.py --version 6000.3.25f1 \
  --cli target/debug/unity-cli --output /tmp/custom-tools-6000
```

出力先は未作成のディレクトリを指定する。`summary.json` は各検査の成否、
CLI / harness / Bridge ソースの SHA-256、実行日時、NUnit の結果を含む。
`transcript.json` は CLI と Bridge の応答を記録し、認証 token は含めない。

## 検証内容

| AC | 検証 |
| --- | --- |
| 1 | Editor 起動後に fixture を追加して再コンパイル。`tool list` と `tool schema` の schema / source を照合 |
| 2 | `raw spawn_light` で `Sun` を作り、hierarchy と Light component、main thread 実行を確認 |
| 3 | 必須引数欠落・型不一致・未知引数を CLI と直接 TCP で拒否。呼出し回数が増えないことを確認 |
| 4 | 意図的な例外が `CUSTOM_TOOL_FAILED` / exit 6 となり、直後の ping が成功 |
| 5 | EditMode テストで組み込み名との衝突拒否と Console warning を検査 |
| 6 | `docs/tools.md` と `unity-editor-tools` に実行例。JSON 移行と `--names-only` を docs / skills / CHANGELOG に反映 |
| 7 | 親 SPEC #152 に実装と検証をコメントで記録 |

同じ実行で `--names-only`、dry-run の副作用抑止、属性名変更後の古い登録の消滅も検査する。
Rust TCP テストは各 CLI 呼出し形式、batch の事前検証、応答喪失時の再実行禁止、
旧 Bridge のフォールバック、認証エラー、壊れた discovery 応答、数値範囲と null を検査する。
未起動プロジェクトの明示指定でも組み込みカタログを取得できる回帰テストを含む。

## 検証時に修正した点

- E2E の `get_gameobject_details` 引数を既存 API の `path` に修正し、
  `Sun` の `components[].type == Light` を明示的に検査する。
- Unity 2022 の初回 import 中は状態取得が一時的に idle になるため、
  所有する batch host の起動ログと連続する idle 応答を待ってから検証を開始する。
  カスタムツール呼出し自体を再試行して成功扱いにはしない。
- CLI の未起動プロジェクト指定で discovery が `EDITOR_NOT_FOUND` となる回帰を
  RED で確認し、組み込みカタログのオフライン取得を維持した。

User Verification Result: n/a (autonomous)

Agent Visual Check: n/a (no UI surface)
