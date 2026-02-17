# 実装計画: Rust版 unity-cli 置換・UPM統合・TDD整備

**要件ID**: `SPEC-83d9d7ee` | **日付**: 2026-02-17 | **仕様**: [spec.md](./spec.md)  
**入力**: `/specs/SPEC-83d9d7ee/spec.md` の機能仕様

## 概要

Node.js ベースの実行導線を Rust 製 `unity-cli` に置換し、Unity TCP 直結の CLI を提供する。合わせて `unity-cli` リポジトリ内に UPM パッケージ（`UnityCliBridge`）と LSP 実装を同梱し、Cargo 配布導線と TDD を整備する。

## 技術コンテキスト

**言語/バージョン**: Rust 1.91+  
**主要依存関係**: `tokio`, `clap`, `serde`, `serde_json`, `anyhow`, `tracing`  
**ストレージ**: ローカル JSON (`instances.json`)  
**テスト**: `cargo test`  
**対象プラットフォーム**: macOS / Linux / Windows 上の Rust CLI  
**プロジェクトタイプ**: single (new `unity-cli` crate inside workspace)  
**パフォーマンス目標**: script/index 系ローカル処理で既存実装以上（P50/P95）を維持し、LSP失敗時はRust実装へフォールバック  
**制約**: Unity TCP 既存プロトコル互換（length+JSON）を保持  
**スケール/スコープ**: まずコアサブコマンド + raw fallback を提供

## 憲章チェック

- シンプルさ: 新規実装は `unity-cli` ディレクトリへ分離し、既存 Node 本体は改変しない方針で複雑度を抑制。  
- TDD: パーサー/通信/インスタンス切替に対するテストを先に定義し、実装整合を確認。  
- 可観測性: `--output json` と明確なエラーメッセージでデバッグ可能性を維持。  
- ドキュメント: 旧READMEへ移行ガイドを追記し、運用導線を明示。

## プロジェクト構造

```text
specs/SPEC-83d9d7ee/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── cli-commands.md
└── tasks.md

unity-cli/
├── Cargo.toml
├── src/
│   ├── main.rs
│   ├── cli.rs
│   ├── config.rs
│   ├── transport.rs
│   ├── instances.rs
│   ├── lsp.rs
│   ├── local_tools.rs
│   └── tool_catalog.rs
├── lsp/
│   ├── Program.cs
│   ├── Server.csproj
│   └── Server.Tests.csproj
├── UnityCliBridge/
│   └── Packages/unity-cli-bridge/
├── README.md
└── RELEASE.md

scripts/
└── export-unity-cli-subtree.sh

.github/workflows/
└── unity-cli-release.yml
```

## Phase 0: リサーチ

- Unity TCP 応答のフレーム有無差分を吸収する方針を確定。  
- 既存環境変数互換（`UNITY_MCP_*`）の優先順位を決定。  
- ローカルインスタンス管理の保存先（OS標準config）を決定。

## Phase 1: 設計

- CLI インターフェース（サブコマンドと引数）を定義。  
- RuntimeConfig / UnityClient / InstanceRegistry の責務分離を確定。  
- エラー出力契約（text/json）を定義。  
- スキル導線（Codex/Claude）を `unity-cli-usage` として統一。

## Phase 2: TDD実装

- RED: 失敗するテスト（パース検証・通信失敗/成功・インスタンス到達性）を追加。  
- GREEN: 最小実装でテストを通す。  
- REFACTOR: 設定解決/通信処理/インスタンス管理を分離して可読化。

## Phase 3: UPM/LSP/Cargo 導線

- Unity 側実装を `unity-cli/UnityCliBridge/Packages/unity-cli-bridge` へ移行。
- Unity 側公開名を `UnityCliBridge` に統一（`UnityCliBridge` 名称を排除）。
- LSP 実装を `unity-cli/lsp` へ同梱し、Rust 側の `UNITY_CLI_LSP_MODE` 連携を追加。
- `Cargo.toml` メタデータを crates.io 公開に対応させ、`cargo install` 手順を明文化。

## Phase 4: 移行ドキュメント

- README / README.ja に `unity-cli` 置換方針とコマンド対応表を追加。  
- Claude plugin marketplace と `unity-cli` スキルを登録。  
- Codex スキル `unity-cli-usage` を追加。
- 専用repo切り出し用に subtree export スクリプトと release workflow を整備。

## 実装完了の判定

- `cargo test --manifest-path unity-cli/Cargo.toml` が成功。  
- CLI ヘルプが期待サブコマンドを表示。  
- script/index ローカルツール（`build_index`, `update_index`, `get_symbols`, `find_symbol`, `find_refs`）が実行できる。  
- Unity UPM パッケージが `akiojin/unity-cli` 配下の Git URL で導入できる。  
- Cargo install 導線（`cargo install unity-cli`）が README と Cargo metadata に反映される。  
- spec/plan/tasks が完成し、追加機能時の作業手順が決まっている。
