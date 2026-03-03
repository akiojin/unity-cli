# CLAUDE.md

`unity-cli` リポジトリ向けの開発ガイドです。

## プロジェクト概要

`unity-cli` は [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server) の後継プロジェクトです。
Node.js + MCP プロトコルベースの旧実装を Rust + TCP 直接通信に置き換え、ネイティブ CLI として再設計しました。
旧リポジトリ (`unity-mcp-server`) への機能追加は行いません。

## スキルアーキテクチャ

旧 `unity-mcp-server` の 108 個の MCP ツールを **Claude Code Skill** に変換。
スキルはオンデマンドで読み込まれ、LLM コンテキストを肥大化させない。
内部的には `unity-cli` コマンド（型付きサブコマンド or `raw`）を呼び出す。

- スキル定義: `.claude-plugin/plugins/unity-cli/skills/`
- プラグインマニフェスト: `.claude-plugin/plugins/unity-cli/plugin.json`
- Claude Code 正式配布: Marketplace プラグイン（`.claude-plugin/marketplace.json`）
- このリポジトリ内の Claude テスト登録: `.claude/skills/`（正本へのシンボリックリンク）
- Codex 運用: `.codex/skills/`（正本へのシンボリックリンク）
- zip 配布はこのリポジトリでは提供しない
- 旧MCP由来のスキル名/互換エイリアスは提供しない

## 基本方針

- 実装は `unity-cli`（Rust CLI）を中心に行う
- Unity 側実装は `UnityCliBridge/Packages/unity-cli-bridge` を更新する
- C# のシンボル編集・検索は `lsp/` 前提で設計する
- Node ベースの `unity-mcp-server` 実装は保守対象外

## Claude Code運用ワークフロー

### 1. Plan Mode を既定にする

- 3ステップ以上、または設計判断を含む作業は Plan Mode で開始する
- 実装だけでなく、検証・ロールバック方針も計画に含める
- 途中で前提が崩れたら実装を止めて再計画する
- 新規機能・大きな変更は Spec（`spec.md` / `plan.md` / `tasks.md`）を先に更新する

### 2. サブエージェントを意図的に使う

- 調査、ログ解析、差分比較、長時間テストはサブエージェントへ委譲する
- 1サブエージェントにつき1タスクを原則とする
- メインスレッドは意思決定と統合に集中し、コンテキスト汚染を防ぐ

### 3. 完了前に必ず検証する

- 動作証明なしで「完了」としない
- テスト実行、ログ確認、必要時の `main` 比較を行う
- 「スタッフエンジニアがレビューで承認できるか」を自己チェックする

### 4. エレガントさを追求する（過剰設計しない）

- 非自明な変更は「よりシンプルで堅牢な解があるか」を一度見直す
- ハック的修正は避け、根本原因に対する実装を優先する
- 小さく明白な修正では速度を優先し、不要な抽象化を入れない

### 5. バグ修正は自律的に進める

- バグ報告を受けたら、まず再現・原因特定・修正・検証まで一気通貫で進める
- ログ、エラー、失敗テスト、CI結果を一次情報として扱う
- 追加指示待ちで停滞せず、必要な作業を能動的に実行する

### 6. タスク管理と改善ループ

- 非自明タスクは `tasks/todo.md` にチェック可能な項目で進捗管理する
- ユーザー修正を受けたら、`tasks/lessons.md` に再発防止ルールを追記する
- セッション開始時に `tasks/lessons.md` の直近パターンを見直してから着手する

## LLM向けE2E実行ルール

- LLM が E2E を実行・更新する前に、必ず `docs/development.md` の `E2E Tests` / `E2E テスト` セクションを参照する
- E2E で生成するシーンは `UnityCliBridge/Assets/Scenes/Generated/E2E/` 配下を使用する
- 上記生成シーンは `.gitignore` 対象のため、E2E実行結果としてコミットしない
- ルート直下 (`UnityCliBridge/Assets/Scenes/`) の固定シーンは `SampleScene` のみとする
- UI 検証シーンが必要な場合は `Tools/Unity CLI/UI Tests/*` で `UnityCliBridge/Assets/Scenes/Generated/UI/` に生成する

## 品質ゲート

変更前後で、影響範囲に応じて以下を実行して通すこと:

```bash
cargo fmt --all -- --check
cargo clippy --all-targets -- -D warnings
cargo test --all-targets
dotnet test lsp/Server.Tests.csproj
```

## TDD

1. RED: 失敗するテストを先に作る
2. GREEN: 最小実装で通す
3. REFACTOR: 既存テストを維持したまま整理

## Spec-Driven Development

新規機能・大きな変更は次を更新:

- `specs/SPEC-xxxxxxxx/spec.md`
- `specs/SPEC-xxxxxxxx/plan.md`
- `specs/SPEC-xxxxxxxx/tasks.md`

## リリース

- バージョン同期: `node scripts/release/update-versions.mjs <X.Y.Z>`
- タグ: `vX.Y.Z`
- GitHub Actions: `.github/workflows/unity-cli-release.yml`
- crates.io 公開: `cargo publish`

## 主要ディレクトリ

- `src/`: Rust CLI
- `lsp/`: C# LSP
- `UnityCliBridge/Packages/unity-cli-bridge/`: Unity UPM package
- `docs/`: 運用ドキュメント
- `.specify/`, `specs/`: 仕様/TDD運用
