# Issue #379 — 2D Sprite / Tilemap スキル検証

## 変更と受入条件の監査

`unity-2d-sprite-tilemap` を追加し、Sprite 取り込み、Sprite Atlas、Tile / RuleTile、
Grid / Tilemap、Pixel Perfect Camera、保存・再読込・Play・画像検証を既存ツールで結ぶ。
CLI / bridge の実装変更はない。単独操作は既存スキルに委譲する。

再開時に全 AC を監査した。AC-1 / AC-3 の未コミット実装は存在したが検証未完了、
AC-2 の両 Editor 証跡と AC-4 の PR レビューは未達だった。AC-5 は
[親 SPEC の要件・設計・タスク記録](https://github.com/akiojin/unity-cli/issues/160#issuecomment-5905094372)
で達成済み。linked PR はなかったため、Issue を閉じず既存変更を継続した。

## 検証計画

- Changed surfaces: skill-asset / docs / routing fixtures。
- Acceptance Surface: Unity の描画結果。ブラウザではなく実 Unity Editor で検証する。
- Skill Contract、配布、routing を検査し、Rust の既存回帰テストも実行する。
- C# 製品コード、LSP、Web UI の変更はないため、dotnet / Playwright は対象外。
- User Verification Result: n/a (autonomous)

## 静的・回帰検証

- `cargo run -- skills lint --severity error`: 20 skills、0 violations。
- `cargo fmt --all -- --check`: PASS。
- `cargo clippy --all-targets -- -D warnings`: PASS。
- `cargo test --all-targets -- --test-threads=1`: 582 passed。
  [全テスト名と結果](issue-379/rust-tests.txt)。配布テスト8件を含む。
- `python3 -m unittest discover -s tests/scripts -p test_skill_routing.py`: 8 passed。
- Markdown lint: PASS。

## Routing

追加ケース SPR379-01〜09 は正例3件と、asset / scene / capture / console / package
単独操作への境界6件。旧スキル集合では top-1 が6/9、追加後は9/9。

Claude Code にスキル・ツール一覧とプロンプトだけを渡し、正解を与えず測定した。
既存 `run-codex-routing.py` と同じ順序でキーワード判定を適用し、該当しないケースは
Claude の予測を使用した（55件 / 141件）。判定規則や正解は変更していない。

| 測定 | 件数 | Top-1 | Top-2 | Tool | Payload |
| --- | ---: | ---: | ---: | ---: | ---: |
| 追加ケース | 9 | 100% | 100% | 100% | 100% |
| 全ケース・既存キーワード判定込み | 196 | 99.49% | 99.49% | 98.47% | 96.43% |
| 全ケース・LLM 単独 | 196 | 99.49% | 99.49% | 95.41% | 92.86% |

既存ランナー方式は全閾値を PASS。LLM 単独では既存ケースの引数予測が95%閾値に届かず、
この列を PASS とは扱わない。LLM の結果は測定値であり、将来の応答を保証しない。
[予測](issue-379/routing/predictions.jsonl)、[LLM 生予測](issue-379/routing/llm-predictions.jsonl)、
[全件スコア](issue-379/routing/summary.json)、[追加ケース](issue-379/routing/cases.jsonl)。

## 実 Editor のシナリオ

macOS / Apple Silicon 上で Claude Code の Skill ツールからスキルを起動した。
隔離プロジェクト・専用ポートを使用し、他の Editor は変更していない。
CLI / bridge は 0.16.0。Built-in、16 PPU、320×180 の最小構成を選択した。

1. 16×16 PNG を Single / Point / 16 PPU / mipmaps 無効で取り込む。
2. Atlas と Tile を作り、Grid / Tilemap に配置する。
3. RuleTile の右隣 `This` 条件を作り、match / nonmatch の Sprite を確認する。
4. PixelPerfectCamera を設定し、保存後に別シーンを経由して再読込する。
5. Play、状態・Console・コンパイル結果、1280×720 PNG を確認する。
6. Stop 後の永続化状態を再確認し、この検証用 Editor だけを終了する。

Unity 2022.3.62f3 は port 6580、Pixel Perfect 5.1.1 / Tilemap Extras 3.1.3。
Unity 6000.3.25f1 は port 6579、Pixel Perfect 5.1.1 / Tilemap Extras 6.0.3。

| Editor | 最終版再検証 | ピクセル検査 | 証跡 |
| --- | --- | --- | --- |
| 2022.3.62f3 | 保存・再読込・Play・Stop 成功、エラー0 | 6セル、4倍整数拡大、不一致0 | [結果](issue-379/2022.3.62f3/result.json) / [画像](issue-379/2022.3.62f3/screenshots/game.png) |
| 6000.3.25f1 | 各状態70/70項目成功、エラー0 | 5セル、4倍整数拡大、不一致0 | [結果](issue-379/6000.3.25f1/result.json) / [画像](issue-379/6000.3.25f1/screenshots/game.png) |

Agent Visual Check: pass — 両画像を確認。Tile / RuleTile の配置と異なる Sprite、
硬いピクセル境界を確認し、欠落・継ぎ目・マゼンタ描画なし。
Web UI のテーマ切替ではなく Unity の静的シーン描画を検証した。

[最終スキルのハッシュ](issue-379/skill-hashes.json) と各 `result.json` の
`skillUnderTest.sha256` を照合する。各バージョンの証跡には Skill 呼び出し、
コマンド、実測結果、PNG、保存したシーン・Tile・Atlas とメタデータを含む。
元の `raw/<name>` 出力は配布用の `tool-results.json` にファイル名をキーとして集約した。
`python3 docs/verification/issue-379/verify_evidence.py` は最終スキル3ファイルと
保存証跡102ファイルのハッシュ、Skill 呼び出し、成功結果、PNG を検証する。
これは既存実機証跡の整合性検査であり、Editor の再実行ではない。

## 実行で見つかった問題と制約

- 最初の実機実行で、例の `action:set` と `packingSettings.enableRotation` が拒否された。
  CLI スキーマと handler を照合し、`modify` / `allowRotation` に修正した。
  最終版の例をパスだけ変えて両 Editor で再実行し成功した。
- Atlas は保存・packables・設定の検証。Sprite Packer が Disabled のため、
  ランタイムで Atlas を使用したとは主張しない。
- Unity 6 の初回実行で、Claude が手順外に追加した `SpriteAtlasUtility.PackAtlases`
  が Editor の native crash を起こした。保存済みプロジェクトを復旧し、任意の強制パックを
  除いて必須シナリオを検証した。CLI / bridge に回避実装は追加していない。
- `captureMode:game` / `osFallback:false` は、この bridge では Main Camera の
  RenderTexture を返す。PNG はタイル描画と整数拡大の証跡であり、最終 Game-view
  backbuffer、UI、post-processing の証明ではない。既存の [#422](https://github.com/akiojin/unity-cli/issues/422)
  をスキルから参照し、実際の取得元を必ず記録する。

## D6 / 独自著作

公式 `unity-agent-plugin/skills/` のテキスト・コードは参照・コピーしていない。
手順はこのリポジトリの CLI スキーマ / handler、既存スキルの責務境界、Unity API と
実 Editor の測定から独自に構成した。runtime-checklist は当リポジトリの既存形式を使用する。
PR #433 の再開時に Codex が正本3ファイル、既存スキルとの共有部分、CLI の
`tool_catalog.rs` と実行証跡をレビューした。runtime-checklist は当リポジトリの
既存素材で、recipe は CLI 固有の制約と実測による修正を反映している。
公式 plugin の素材の取り込みや派生物を示す差分はなく、独自著作の説明と整合する。
これはリポジトリの来歴に基づくエージェントレビューであり、人間の確認ではない。

## PR #433 の競合解消

develop の URP スキル追加と、asset / playmode / scene スキルの `siblings` で競合した。
各一覧に `unity-2d-sprite-tilemap` と `unity-urp-setup` の両方を残した。
2D 正本3ファイルと実機証跡は変更していないため、保存済み実機検証の対象は同一。
統合後に fmt / clippy、Rust 588件、Skill Contract 21スキル・違反0件、
routing 評価器10件、Markdown lint、102証跡の整合性検査が成功した。
保存予測204件の再採点は全閾値PASS（top1/top2 99.51%、tool 98.53%、payload 96.57%）。
この再採点は、統合後の新しい LLM 推論とは区別する。
正式検証記録と配送結果は PR #433 に記録する。
