# Issue #560 調査証跡

調査日: 2026-11-11。担当: テツヤ（Codex）。
成果物: [Asset Store 新規ダウンロード方式の調査](../../asset-store-download-research.md)。

## Acceptance coverage

| 条件 | 証跡 |
| --- | --- |
| AC-1 | 対象 3 タグの固定 commit、内部ダウンロード経路、6000.x と 7000.0 の認証差分。`source-evidence.json` に 24 ファイルの SHA-256 と 84 観察項目の行番号 |
| AC-2 | 調査資料の公開 API・規約の節。Unity 公式資料の URL、確認日、改定日、条項番号、事実と適用上の推論を記載 |
| AC-3 | 調査資料の 4 案比較。認証・規約・バージョン差と破壊的変更リスクを明記 |
| AC-4 | 調査資料の非対応理由と再検討条件。実装 Issue の起票は非対応推奨のため対象外 |

## Verification Report

Mode: full (docs/research surface)
Launch mode: autonomous
Baseline: `origin/develop` = `2568fe575184d6261150e6ea66cbc96d17376a28` at initial fetch
Changed surfaces: documentation, research provenance checker, Work event shards
Acceptance Surface: non-user-facing (research only; no product behavior change)

### Plan

Detected runners: Cargo, dotnet, Unity Test Framework, markdownlint, Python.
Selected commands:

- `python docs/verification/issue-560/check_sources.py` — 公開 GitHub から固定ソースを再取得し、記録した観察項目と SHA-256 を照合する。
- `"C:/Program Files/Git/bin/bash.exe" -lc "npx --yes markdownlint-cli@0.50.0 README.md docs/asset-store-download-research.md docs/verification/issue-560/README.md --config .markdownlint.json --ignore-path .markdownlintignore"` — 変更 Markdown の整形を確認する。Windows の canonical runner では npm の shim を直接起動せず Bash を使用する。
- `git diff --check origin/develop` — 差分の whitespace を確認する。

Skipped: Rust / LSP / Unity 実機テストは製品ソースの変更がないため対象外。
UI / headed E2E も画面変更・UI 動作に関する AC がないため対象外。
調査成果物なので RED/GREEN は新規機能の回帰テストに適用せず、一次資料の再確認で検証する。

### Test Inventory

- 6000.3.28f1: 8 pinned files / 29 source observations
- 6000.6.8f1: 8 pinned files / 29 source observations
- 7000.0.0a16: 8 pinned files / 26 source observations
- source-evidence.json: 24 ファイル全件のハッシュ・観察行が再取得結果と一致すること
- Markdown lint と whitespace: 上記のコマンド対象

この checker が証明するのは固定参照ソースの内容であり、ダウンロードの成功、
全 Editor ビルドの互換性、利用規約上の許可、オーナーの最終判断ではない。
規約・公開 API 資料は調査日に公式ページを確認しており、将来の再調査では再読が必要。
実行結果と commit に結び付く canonical record は `verify.run` と PR 本文に記録する。

User Verification Result: n/a (autonomous)
Agent Visual Check: n/a (no UI surface)

### Reproduce

```bash
python docs/verification/issue-560/check_sources.py
```

Python 標準ライブラリと HTTPS 接続のみを使用し、Unity のログインや Editor 起動は不要。
接続先は公開 GitHub の固定参照ソースに限り、Asset Store の認証・商品・配信 API を呼ばない。
`--record` は証跡を意図的に更新する場合だけ使う。通常の検証では保存済み証跡を変更しない。
