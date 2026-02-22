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

## 品質ゲート

変更前後で以下を満たすこと:

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
