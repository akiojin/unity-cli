# CLAUDE.md

`unity-cli` リポジトリ向けの開発ガイドです。

## プロジェクト概要

`unity-cli` は [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server) の後継プロジェクトです。
Node.js + MCP プロトコルベースの旧実装を Rust + TCP 直接通信に置き換え、ネイティブ CLI として再設計しました。
旧リポジトリ (`unity-mcp-server`) への機能追加は行いません。

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
