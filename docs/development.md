# Development Guide

English | [日本語](#日本語)

This document covers internal development workflow for `unity-cli`.

## Core Stack

- CLI runtime: Rust (`src/`)
- Unity bridge package: `UnityCliBridge/Packages/unity-cli-bridge`
- Unity test project: `UnityCliBridge`
- C# LSP: `lsp/`
- Spec workflow: `.specify/` + `specs/`

## Prerequisites

| Tool | Version | Purpose |
| ------ | --------- | -------- |
| Rust toolchain (stable) | latest | CLI build and test |
| .NET SDK | 9.0 | LSP server build and test |
| Unity Editor | 2022.3+ | E2E tests (requires live connection) |

### Installation

```bash
# Rust
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh

# .NET SDK 9
# https://dotnet.microsoft.com/download/dotnet/9.0
```

### Docker

Rust and .NET SDK 9 are included in the development Docker image.

```bash
# Build image
docker build -t unity-cli-dev .

# Run all tests (default)
docker run --rm unity-cli-dev

# Run individual tests
docker run --rm unity-cli-dev cargo test
docker run --rm unity-cli-dev dotnet test lsp/Server.Tests.csproj
```

## Local Commands

```bash
# Rust
cargo fmt
cargo clippy --all-targets -- -D warnings
cargo test --all-targets

# C# LSP
dotnet test lsp/Server.Tests.csproj

# Unity (EditMode tests)
unity -batchmode -nographics -projectPath UnityCliBridge -runTests -testPlatform editmode -testResults test-results/editmode.xml -quit
```

### Pre-push Hook

```bash
chmod +x .husky/pre-push
git config core.hooksPath .husky
```

`git push` will automatically run `cargo test` and `dotnet test`.

## TDD Flow

1. Write failing tests (RED)
2. Implement minimum change (GREEN)
3. Refactor while tests stay green

Keep test-first commit order whenever possible.

## E2E Tests

E2E tests require a running Unity Editor with TCP server active.

### Preparation

1. Open the Unity project in Unity Editor
2. Ensure the UnityCliBridge package is installed
3. Enter Play mode and verify the TCP server is running

### Execution

```bash
# Build
cargo build --release

# Default (127.0.0.1:8080)
scripts/e2e-test.sh

# Custom host and port
scripts/e2e-test.sh --host 192.168.1.10 --port 9090
```

### Test Scenarios

| Scenario | Command | Verification |
| ---------- | --------- | ------------- |
| system ping | `unity-cli system ping` | TCP connectivity check |
| raw create_scene | `unity-cli raw create_scene --json '{"sceneName":"E2ETest"}'` | Scene creation |
| tool list | `unity-cli tool list` | Tool listing |

Logs on failure are saved to `/tmp/unity-cli-e2e-*.log`.

## CI Overview

CI is defined in `.github/workflows/test.yml`.

| Job | Trigger | Description |
| ----- | --------- | ------------- |
| Rust Tests (required) | push / PR | `cargo test` |
| LSP Tests (required) | push / PR | `dotnet test lsp/Server.Tests.csproj` |
| Unity E2E Tests | manual (`workflow_dispatch`) | E2E test script |

Rust Tests and LSP Tests are required checks for PR merges.
E2E Tests are manual-only and require a runner with Unity Editor.

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

## Documentation Consistency Checks

Periodically verify that specs and docs match the current implementation.

### Check Targets

| Directory / File | Contents |
| ------------------ | ---------- |
| `specs/` | Design specs (architecture, migration notes) |
| `docs/` | Development guides, configuration reference |
| `README.md` | Project overview |
| `UnityCliBridge/Packages/unity-cli-bridge/docs/` | UPM package docs |

### Check Procedure

1. **Legacy name residuals**: Search for unintentional `MCP` or old project name references.

   ```bash
   grep -rn "unity-mcp-server\|UNITY_MCP\|UnityMcpBridge" specs/ docs/ README.md \
     | grep -v migration-notes.md \
     | grep -v configuration.md
   ```

   - `specs/migration-notes.md` and `docs/configuration.md` intentionally contain old names for migration/deprecation documentation.

2. **Environment variable consistency**: Compare `docs/configuration.md` variables with `src/config.rs`.

   ```bash
   grep -oE 'UNITY_CLI_[A-Z_]+' src/config.rs | sort -u
   grep -oE 'UNITY_CLI_[A-Z_]+' docs/configuration.md | sort -u
   ```

3. **Source file structure**: Verify `specs/architecture.md` file list matches actual sources.

   ```bash
   ls src/*.rs
   ```

4. **Command list**: Check `README.md` Command Overview against `src/cli.rs` subcommand definitions.

### When to Check

- Before merging PRs that add features or change design
- After changes to environment variables or command structure
- As a release checklist item

---

## 日本語

このドキュメントは `unity-cli` の内部開発フローをまとめたものです。

## コア構成

- CLI本体: Rust (`src/`)
- Unity連携パッケージ: `UnityCliBridge/Packages/unity-cli-bridge`
- Unityテストプロジェクト: `UnityCliBridge`
- C# LSP: `lsp/`
- Specワークフロー: `.specify/` + `specs/`

## 前提条件

| ツール | バージョン | 用途 |
| -------- | ----------- | ------ |
| Rust toolchain (stable) | latest | CLI 本体のビルド・テスト |
| .NET SDK | 9.0 | LSP サーバーのビルド・テスト |
| Unity Editor | 2022.3+ | E2E テスト (実機接続が必要) |

### インストール

```bash
# Rust
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh

# .NET SDK 9
# https://dotnet.microsoft.com/download/dotnet/9.0 からダウンロード
```

### Docker を使う場合

Rust と .NET SDK 9 が同梱された開発用 Docker イメージを利用できます。

```bash
# イメージをビルド
docker build -t unity-cli-dev .

# 全テストを実行 (デフォルト)
docker run --rm unity-cli-dev

# 個別テスト
docker run --rm unity-cli-dev cargo test
docker run --rm unity-cli-dev dotnet test lsp/Server.Tests.csproj
```

## ローカル実行コマンド

```bash
# Rust
cargo fmt
cargo clippy --all-targets -- -D warnings
cargo test --all-targets

# C# LSP
dotnet test lsp/Server.Tests.csproj

# Unity（EditModeテスト）
unity -batchmode -nographics -projectPath UnityCliBridge -runTests -testPlatform editmode -testResults test-results/editmode.xml -quit
```

### プッシュ前フック

```bash
chmod +x .husky/pre-push
git config core.hooksPath .husky
```

`git push` 時に自動で `cargo test` と `dotnet test` が実行されます。

## TDDフロー

1. 失敗するテストを先に作成（RED）
2. 最小実装で通す（GREEN）
3. テストを維持したまま整理（REFACTOR）

## E2E テスト

E2E テストは Unity Editor が起動している環境で実行します。

### 準備

1. Unity Editor でプロジェクトを開く
2. UnityCliBridge パッケージが導入されていることを確認する
3. Play モードに入り、TCP サーバーが起動していることを確認する

### 実行

```bash
# ビルド
cargo build --release

# デフォルト (127.0.0.1:8080)
scripts/e2e-test.sh

# ホスト・ポートを指定
scripts/e2e-test.sh --host 192.168.1.10 --port 9090
```

### テストシナリオ

| シナリオ | コマンド | 確認内容 |
| ---------- | --------- | --------- |
| system ping | `unity-cli system ping` | Unity Editor との疎通確認 |
| raw create_scene | `unity-cli raw create_scene --json '{"sceneName":"E2ETest"}'` | シーン作成の実行確認 |
| tool list | `unity-cli tool list` | ツール一覧の取得確認 |

失敗時はログが `/tmp/unity-cli-e2e-*.log` に保存されます。

## CI の概要

CI は `.github/workflows/test.yml` で定義されています。

| ジョブ | トリガー | 内容 |
| -------- | --------- | ------ |
| Rust Tests (required) | push / PR | `cargo test` |
| LSP Tests (required) | push / PR | `dotnet test lsp/Server.Tests.csproj` |
| Unity E2E Tests | 手動 (`workflow_dispatch`) | E2E テストスクリプトの実行 |

Rust Tests と LSP Tests は PR マージの必須チェックです。
E2E Tests は手動トリガーのみで、Unity Editor が起動しているランナーが必要です。

## リリースフロー

1. `node scripts/release/update-versions.mjs <X.Y.Z>` でバージョン同期
2. `vX.Y.Z` タグ作成
3. `.github/workflows/unity-cli-release.yml` でバイナリ公開
4. `cargo publish` で crates.io 公開

詳細は `RELEASE.md` を参照してください。

## ドキュメント整合チェック

仕様書・ドキュメントが現行の実装と矛盾していないことを定期的に確認します。

### チェック対象

| ディレクトリ / ファイル | 内容 |
| ------------------------ | ------ |
| `specs/` | 設計仕様書（アーキテクチャ、移行記録） |
| `docs/` | 開発ガイド、設定リファレンス |
| `README.md` | プロジェクト概要 |
| `UnityCliBridge/Packages/unity-cli-bridge/docs/` | UPM パッケージドキュメント |

### チェック手順

1. **旧名称の残留確認**: 以下のコマンドで `MCP` や旧プロジェクト名の残留をチェックします。

   ```bash
   grep -rn "unity-mcp-server\|UNITY_MCP\|UnityMcpBridge" specs/ docs/ README.md \
     | grep -v migration-notes.md \
     | grep -v configuration.md
   ```

2. **環境変数の整合確認**: `docs/configuration.md` の変数一覧と `src/config.rs` の実装を比較します。

   ```bash
   grep -oE 'UNITY_CLI_[A-Z_]+' src/config.rs | sort -u
   grep -oE 'UNITY_CLI_[A-Z_]+' docs/configuration.md | sort -u
   ```

3. **ソースファイル構成の確認**: `specs/architecture.md` のソースファイル一覧と実際のファイルを比較します。

   ```bash
   ls src/*.rs
   ```

4. **コマンド一覧の確認**: `README.md` の Command Overview セクションが `src/cli.rs` のサブコマンド定義と一致しているか確認します。

### チェックのタイミング

- 新機能追加・設計変更を含む PR をマージする前
- 環境変数やコマンド体系に変更があった場合
- リリース前のチェックリストの一項目として
