# 移行記録: unity-mcp-server から unity-cli へ

本ドキュメントは旧プロジェクト `unity-mcp-server`（Node.js）から現行の `unity-cli`（Rust）への移行で変更された事項を記録するものです。

## 移行の動機

- **パフォーマンス**: Node.js ランタイムのオーバーヘッドを排除し、起動速度とメモリ効率を改善
- **配布の簡素化**: npm に代わり `cargo install` による単一バイナリ配布
- **プロトコルの簡素化**: MCP (JSON-RPC over stdio) を廃止し、直接 TCP 通信に変更

## 主な変更点

### ランタイム

| 項目 | 旧 (unity-mcp-server) | 新 (unity-cli) |
|------|----------------------|----------------|
| 言語 | TypeScript / Node.js | Rust |
| エントリーポイント | `src/index.ts` | `src/main.rs` |
| パッケージマネージャ | npm | Cargo |
| 配布 | npm install | cargo install |

### 通信プロトコル

| 項目 | 旧 | 新 |
|------|----|----|
| プロトコル | MCP (Model Context Protocol) | 直接 TCP |
| トランスポート | JSON-RPC over stdio | JSON over TCP |
| 接続方式 | ホストアプリが stdio でサーバを起動 | CLI が TCP で Unity Editor に接続 |

旧 MCP 方式では、AI ホスト（Claude Desktop 等）が stdio 経由で MCP サーバを起動し、JSON-RPC メッセージをやり取りしていました。新方式では CLI が直接 Unity Editor の TCP サーバに接続するため、中間レイヤーが不要になりました。

### 環境変数

| 旧変数名 | 新変数名 | 備考 |
|----------|---------|------|
| `UNITY_CLI_HOST` / `UNITY_CLI_HOST` | `UNITY_CLI_HOST` | ホスト名 |
| `UNITY_CLI_PORT` | `UNITY_CLI_PORT` | ポート番号 |
| `UNITY_CLI_TIMEOUT_MS` | `UNITY_CLI_TIMEOUT_MS` | タイムアウト |

**互換性ポリシー**: 旧 `UNITY_CLI_*` 環境変数は移行用エイリアスとしてサポートされています。新しい `UNITY_CLI_*` が設定されていない場合に旧変数が参照されます。新規運用では `UNITY_CLI_*` を使用してください。

### パッケージ名・名前空間

| 項目 | 旧 | 新 |
|------|----|----|
| リポジトリ名 | unity-mcp-server | unity-cli |
| UPM パッケージ名 | com.akiojin.unity-mcp-bridge | com.akiojin.unity-cli-bridge |
| C# 名前空間 | Legacy Bridge namespace | UnityCliBridge |
| npm パッケージ | unity-mcp-server | ― (廃止) |
| crates.io | ― | unity-cli |

### Unity 側 (UPM パッケージ)

Unity 側の UPM パッケージは引き続き同一リポジトリ内で管理されています。主な変更:

- **名前空間の変更**: Legacy Bridge namespace → `UnityCliBridge`
- **パッケージ名の変更**: `com.akiojin.unity-mcp-bridge` → `com.akiojin.unity-cli-bridge`
- **通信方式**: 変更なし（TCP サーバとして動作する点は同一）
- **コマンドハンドラ**: 既存の 108 ツール群はそのまま引き継ぎ

### LSP (C# 静的解析)

- 旧リポジトリでは Node.js 側に組み込まれていた静的解析機能を、独立した C# LSP サーバとして再実装
- `lsp/` ディレクトリに配置
- .NET 9 を使用

### CI/CD

| 項目 | 旧 | 新 |
|------|----|----|
| テスト | npm test | cargo test + dotnet test |
| リリース | npm publish | cargo publish + GitHub Release |
| ワークフロー | ― | `.github/workflows/unity-cli-release.yml` |

## 削除された機能

- **MCP サーバモード**: stdio 経由の JSON-RPC サーバ機能は廃止
- **npm 関連ファイル**: `package.json`, `tsconfig.json`, `node_modules` 等は削除

## 移行時の注意事項

1. **環境変数の更新**: `UNITY_CLI_*` を `UNITY_CLI_*` に順次更新してください。新規設定は `UNITY_CLI_*` のみを使用してください。
2. **UPM パッケージ URL の変更**: Unity Package Manager の Git URL をリポジトリ名の変更に合わせて更新してください。
3. **スクリプトの更新**: 旧 `unity-mcp-server` コマンドを使用しているスクリプトは `unity-cli` に置き換えてください。

## 関連ドキュメント

- [ベースライン方針と差分棚卸し](SPEC-83d9d7ee/baseline-policy.md): 移行差分の網羅的な棚卸しとベースライン方針
