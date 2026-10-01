# Issue #444 — MCP stdio adapter

2026-10-02 JST、macOS / Apple Silicon。MCP Inspector 2.9.0 を使用し、
隔離した Unity プロジェクトで実行した。検証ごとに CLI 実行ファイルをコピーして
固定し、SHA-256 を `summary.json` に記録している。

## 統合後の検証

対象ソース: `895b9e0` に本 PR の `src/mcp/mod.rs` 動的探索接続を加えた状態。
Issue #442 の `2a84ee8` と develop の #445 を統合済み。
両実機検証のバイナリ SHA-256 は
`4020ba87eb34f22e05bd89ebe53d351aaaf235611240572dc3effa4cbfe334b3`。

| Unity | 結果 | 証跡 |
| --- | --- | --- |
| 6000.3.25f1 | 16/16 PASS、159 tools | [summary](issue-444/6000.3.25f1/summary.json) |
| 2022.3.62f3 | 16/16 PASS、159 tools | [summary](issue-444/2022.3.62f3/summary.json) |

各ディレクトリには Inspector の実行コマンドと未加工 stdout、CLI のツール名一覧、
Editor 起動前からの stdio 通信記録を保存している。Editor の lockfile、
認証トークン、実行バイナリは含めない。

- AC-1: Inspector `tools/list` と CLI `tool list` の名前集合が一致。
  生の MCP 応答は CLI の引数スキーマと厳密一致し、プロジェクトツールの公開・実行も成功。
- AC-2: Inspector `create_gameobject` で作成し、`get_hierarchy` で存在を確認。
- AC-3: MCP 起動時は Editor 不在。後から実 Editor を起動して変更通知を確認。
- AC-4: 5 クライアントの dry-run、既存設定保持、冪等性、symlink 拒否を
  `tests/mcp.rs` で検証。設定パスの Windows 分岐も単体テストで確認。
- AC-5: 認証ありの eval は成功。不一致トークンは `UNAUTHORIZED`、副作用なし。
- AC-6: `docs/mcp.md` と `unity-cli-usage` を更新。skills lint は 23 skills、0 violations。
- AC-7: [親 SPEC #160 への記録](https://github.com/akiojin/unity-cli/issues/160#issuecomment-5937044068)。

Rust は 663 tests PASS、行カバレッジ 92.82%、fmt / clippy PASS。
Python は 75 tests（既存 1 skip）、.NET は 53 tests PASS。
コマンドと Rust テスト一覧は [checks.json](issue-444/checks.json) に記録。
最終 PR にはこの証跡へのリンクを添付する。Windows/Linux 実機確認は #386 の範囲。

## 検証計画と再実行時の修正

変更面は CLI、stdio プロトコル、クライアント設定、スキル、ドキュメント。
Rust のプロセステストで JSON-RPC・設定ファイル・動的通知を検証し、Inspector と
実 Editor で transport / 認証まで確認した。既存 Python と LSP テストも通している。
ブラウザや描画 UI の変更はない。

初回の静的実装 `8ff54b1` は両 Editor 各 11 checks PASS。
動的統合後、検証スクリプトに以下の修正を加えて両版を再実行した。

- Inspector の JavaScript JSON パーサーが Int64 の上下限を丸めるため、
  Inspector の比較と生の stdio の厳密比較を分離。生の値は変更されていない。
- 初回アセットインポート中の `tool list` タイムアウトを再現し、接続通知を確認した後に
  コンパイル・インポート完了を待つよう修正。最終実行はすべて PASS。

User Verification Result: n/a (autonomous)。
Agent Visual Check: n/a (no UI surface)。

再実行は [MCP 検証手順](../mcp.md#verification) を参照。
