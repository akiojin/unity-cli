# CLAUDE.md

`unity-cli` リポジトリ向けの開発ガイドです。

## プロジェクト概要

`unity-cli` は [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server) の後継プロジェクトです。
Node.js + MCP プロトコルベースの旧実装を Rust + TCP 直接通信に置き換え、ネイティブ CLI として再設計しました。
旧リポジトリ (`unity-mcp-server`) への機能追加は行いません。

## スキルアーキテクチャ

Unity 操作は **Claude Code / Codex Skill** として提供する。
スキルはオンデマンドで読み込まれ、LLM コンテキストを肥大化させない。
内部的には `unity-cli` コマンド（型付きサブコマンド or `raw`）を呼び出す。
すべての `unity-*` skill は **Skill Contract v1** ([SPEC #160](https://github.com/akiojin/unity-cli/issues/160)) に準拠する。

- スキル正本: `.claude-plugin/plugins/unity-cli/skills/unity-*`
- Claude Code プラグイン: `.claude-plugin/plugins/unity-cli/plugin.json` + `.claude-plugin/marketplace.json`
- Claude Code テスト登録: `.claude/skills/`（正本へのシンボリックリンク）
- Codex プラグイン: `.codex-plugin/plugin.json` + `.agents/plugins/marketplace.json`
- Codex skills root: `.agents/skills/`（正本へのシンボリックリンク、CWD から repo root まで走査される公式パス）
- 開発専用 skill: `dev-skills/`（配布対象外、`gh-skills-sync` など）
- 静的検証: `unity-cli skills lint`（22 ルール、CI ゲート）
- 契約・運用ガイド: `docs/skills.md` / `.claude-plugin/plugins/unity-cli/CONTRIBUTING.md`
- zip 配布はこのリポジトリでは提供しない
- 旧 MCP 由来のスキル名/互換エイリアスは提供しない

## 基本方針

- 実装は `unity-cli`（Rust CLI）を中心に行う
- Unity 側実装は `UnityCliBridge/Packages/unity-cli-bridge` を更新する
- C# のシンボル編集・検索は `lsp/` 前提で設計する
- Node ベースの `unity-mcp-server` 実装は保守対象外

## Claude Code運用ワークフロー

### 1. 新規機能・大きな変更は Spec を先に更新する

- 新規機能・大きな変更は `gwt-spec` ラベル付き GitHub Issue の `Spec` / `Plan` / `Tasks` / `TDD` を先に更新する

### 2. サブエージェントは必要なときだけ使う

- サブエージェントは、独立して並列化できる大きな調査（広範な複数ファイル調査など）に限って使う。起動ごとにコンテキストの再構築と報告の再読が発生し、コストと時間が増えるため
- 数回のツール呼び出しで終わる作業や、自分の作業の検証・レビューには使わない

### 3. 完了の判定

- 動作証明なしで「完了」としない
- `gwt-spec` ラベル付き Issue を進めている間は、完了判定の一次情報を Issue 本文の `Tasks` / 受け入れ基準に置く
- 実装やローカルメモだけが進んでいても、Issue 本文の `Tasks` が未更新なら「完了」と言わない
- 完了報告前に、Issue 本文・PR本文・ローカル差分・作業ツリー状態が一致していることを確認する
- ローカル検証用の一時生成物（例: `.cache/` や generated asset）が残る場合は、ignore または cleanup のどちらかを先に行う

### 4. 依頼された範囲を、根本原因に対して実装する

- 依頼された範囲で実装し、範囲の拡大や不要な抽象化はしない。より良い方針があると判断した場合は一文で伝えたうえで、依頼どおりに進める
- 対症療法ではなく根本原因を直す

### 5. バグ修正は自律的に進める

- バグ報告を受けたら、まず再現・原因特定・修正・検証まで一気通貫で進める
- ログ、エラー、失敗テスト、CI結果を一次情報として扱う

### 6. タスク管理と改善ループ

- 非自明タスクは `tasks/todo.md` にチェック可能な項目で進捗管理する
- ユーザー修正を受けたら、`tasks/lessons.md` に再発防止ルールを追記する
- セッション開始時に `tasks/lessons.md` の直近パターンを見直してから着手する

## LLM向けローカルUnity E2E / シーン運用ルール

- ローカルで Unity E2E を実行・更新する前に、`docs/development.md` の `Local Unity E2E` / `ローカル Unity E2E` セクションを参照する
- ローカル E2E で生成するシーンは `UnityCliBridge/Assets/Scenes/Generated/E2E/` 配下を使用する
- 上記生成シーンは `.gitignore` 対象のため、E2E 実行結果としてコミットしない
- ルート直下 (`UnityCliBridge/Assets/Scenes/`) の固定シーンは `SampleScene` のみとする
- UI 検証シーンが必要な場合は `Tools/Unity CLI/UI Tests/*` で `UnityCliBridge/Assets/Scenes/Generated/UI/` に生成する

## 品質ゲート

変更前後で、影響範囲に応じて以下を実行して通すこと:

```bash
cargo fmt --all -- --check
cargo clippy --all-targets -- -D warnings
cargo test --all-targets -- --test-threads=1
cargo run -- skills lint --severity error
dotnet test lsp/Server.Tests.csproj
```

## TDD

1. RED: 失敗するテストを先に作る
2. GREEN: 最小実装で通す
3. REFACTOR: 既存テストを維持したまま整理

## Spec-Driven Development

新規機能・大きな変更は GitHub Issue-first で管理する:

- `gwt-spec` ラベル付き Issue の本文（`Spec` / `Plan` / `Tasks` / `TDD`）
- SPEC ID は `SPEC-xxxxxxxx` ではなく Issue 番号を使う
- ローカル spec ディレクトリは作成しない
- 実装完了の宣言は、Issue 本文の `Tasks` が実態どおりに更新され、受け入れ基準が満たされてから行う

## リリース

- リリース実行: `./scripts/publish.sh <major|minor|patch>`
- タグ: `vX.Y.Z`
- GitHub Actions: `.github/workflows/release.yml`
- crates.io 公開: `scripts/publish.sh` が `cargo publish` を実行

## 主要ディレクトリ

- `src/`: Rust CLI
- `lsp/`: C# LSP
- `UnityCliBridge/Packages/unity-cli-bridge/`: Unity UPM package
- `docs/`: 運用ドキュメント
- `tests/fixtures/`: 評価・検証用の固定入力
