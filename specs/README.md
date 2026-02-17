# unity-cli 仕様書

本ディレクトリは `unity-cli`（Rust ベースの Unity エディタ自動化 CLI）の設計仕様書をまとめたものです。

## ドキュメント一覧

| ファイル | 内容 |
|----------|------|
| [architecture.md](./architecture.md) | 現行アーキテクチャの概要・コンポーネント構成 |
| [migration-notes.md](./migration-notes.md) | 旧 `unity-mcp-server`（Node.js）からの移行記録 |

## 経緯

本プロジェクトは当初 `unity-mcp-server` という名称で Node.js + MCP（Model Context Protocol）を採用していましたが、パフォーマンスと運用性の観点から Rust へ移行し、プロトコルも直接 TCP 通信に変更しました。

旧リポジトリに存在した仕様書は棚卸しを行い、現行の方針に整合するよう更新または本ディレクトリへ統合しています。旧仕様からの差分については [migration-notes.md](./migration-notes.md) を参照してください。

## 仕様書の方針

- 言語: 日本語（コード例を除く）
- 対象読者: 開発チームメンバーおよびコントリビューター
- 更新: 設計変更時に該当ドキュメントを随時更新する
