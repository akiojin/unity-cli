# Development Guide

English | [日本語](#日本語)

This document covers internal development workflow for `unity-cli`.

## Core Stack

- CLI runtime: Rust (`src/`)
- Unity bridge package: `UnityCliBridge/Packages/unity-cli-bridge`
- Unity test project: `UnityCliBridge/TestProject`
- C# LSP: `lsp/`
- Spec workflow: `.specify/` + `specs/`

## Local Commands

```bash
# Rust
cargo fmt
cargo clippy --all-targets -- -D warnings
cargo test --all-targets

# C# LSP
dotnet test lsp/Server.Tests.csproj

# Unity (EditMode tests)
unity -batchmode -nographics -projectPath UnityCliBridge/TestProject -runTests -testPlatform editmode -testResults test-results/editmode.xml -quit
```

## TDD Flow

1. Write failing tests (RED)
2. Implement minimum change (GREEN)
3. Refactor while tests stay green

Keep test-first commit order whenever possible.

## Release Flow

1. Update versions: `node scripts/release/update-versions.mjs <X.Y.Z>`
2. Tag: `vX.Y.Z`
3. Push tag and run `.github/workflows/unity-cli-release.yml`
4. Publish crate: `cargo publish`

Detailed steps: `RELEASE.md`.

## Spec Kit

- Source of truth: `docs/constitution.md`
- Mirror for Spec Kit: `.specify/memory/constitution.md`
- Spec generation:
  - `/speckit.specify`
  - `/speckit.plan`
  - `/speckit.tasks`

---

## 日本語

このドキュメントは `unity-cli` の内部開発フローをまとめたものです。

## コア構成

- CLI本体: Rust (`src/`)
- Unity連携パッケージ: `UnityCliBridge/Packages/unity-cli-bridge`
- Unityテストプロジェクト: `UnityCliBridge/TestProject`
- C# LSP: `lsp/`
- Specワークフロー: `.specify/` + `specs/`

## ローカル実行コマンド

```bash
# Rust
cargo fmt
cargo clippy --all-targets -- -D warnings
cargo test --all-targets

# C# LSP
dotnet test lsp/Server.Tests.csproj

# Unity（EditModeテスト）
unity -batchmode -nographics -projectPath UnityCliBridge/TestProject -runTests -testPlatform editmode -testResults test-results/editmode.xml -quit
```

## TDDフロー

1. 失敗するテストを先に作成（RED）
2. 最小実装で通す（GREEN）
3. テストを維持したまま整理（REFACTOR）

## リリースフロー

1. `node scripts/release/update-versions.mjs <X.Y.Z>` でバージョン同期
2. `vX.Y.Z` タグ作成
3. `.github/workflows/unity-cli-release.yml` でバイナリ公開
4. `cargo publish` で crates.io 公開

詳細は `RELEASE.md` を参照してください。
