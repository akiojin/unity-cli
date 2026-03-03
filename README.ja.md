# unity-cli

[English](README.md) | [中文](README.zh.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Italiano](README.it.md) | [Español](README.es.md)

`unity-cli` は、Claude Code から Unity Editor を直接 TCP で操作するための Rust 製 CLI です。
[`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server) の後継として、Node.js + MCP からネイティブバイナリ中心の構成に再設計しました。

## 特徴

- Claude Code から、用途別スキルと typed コマンドで Unity を操作できます。
- シーン、アセット、コード、テスト、UI、Editor を含む `101` 個の Unity Tool API を利用できます。
- 単一バイナリで高速起動、低オーバーヘッドです。

## 仕組み

```text
Claude Code
  -> Skills (必要時に読み込み)
  -> unity-cli
  -> Unity Editor (TCP bridge)
```

`read`、`search`、`find_symbol`、`find_refs` など一部のコード系ツールは Unity 接続なしでローカル実行できます。

## はじめ方

### 推奨: Claude Code プラグイン

Claude Code Marketplace から `unity-cli` プラグインをインストールします:

```bash
/plugin marketplace add akiojin/unity-cli
```

`cargo` が利用可能な環境では、プラグインセットアップ時に `unity-cli` を自動インストールまたは更新できます。

### Codex スキル

Codex でこのリポジトリを利用する場合、`.codex/skills/` にスキルのシンボリックリンクが配置済みです。
リポジトリをクローンするだけで追加セットアップは不要です。

### 手動インストール

```bash
cargo install unity-cli
```

Unity 側 UPM パッケージ URL:

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

接続確認:

```bash
unity-cli system ping
```

## スキル (13)

| カテゴリ | スキル |
| --- | --- |
| 導入 | `unity-cli-usage` |
| シーンとオブジェクト | `unity-scene-create`, `unity-scene-inspect`, `unity-gameobject-edit`, `unity-prefab-workflow` |
| アセット | `unity-asset-management`, `unity-addressables` |
| コード | `unity-csharp-navigate`, `unity-csharp-edit` |
| 実行時とテスト | `unity-playmode-testing`, `unity-input-system`, `unity-ui-automation` |
| エディター | `unity-editor-tools` |

## クイック例

```bash
# 接続確認
unity-cli system ping

# シーン作成
unity-cli scene create MainScene

# raw ツール呼び出しで GameObject 作成
unity-cli raw create_gameobject --json '{"name":"Player"}'

# C# コード検索（ローカルツール）
unity-cli tool call search --json '{"pattern":"PlayerController"}'

# EditMode テスト実行
unity-cli tool call run_tests --json '{"mode":"editmode"}'
```

## 設定

| 変数 | 説明 | 既定値 |
| --- | --- | --- |
| `UNITY_PROJECT_ROOT` | `Assets/` と `Packages/` を含むディレクトリ | 自動検出 |
| `UNITY_CLI_HOST` | Unity Editor ホスト | `localhost` |
| `UNITY_CLI_PORT` | Unity Editor ポート | `6400` |
| `UNITY_CLI_TIMEOUT_MS` | コマンドタイムアウト (ms) | `30000` |
| `UNITY_CLI_LSP_MODE` | LSP モード (`off` / `auto` / `required`) | `off` |
| `UNITY_CLI_TOOLS_ROOT` | ダウンロード済みツールのルートディレクトリ | OS 既定 |

旧 MCP 接頭辞の環境変数は非対応です。`UNITY_CLI_*` のみ使用してください。

## ドキュメント

- コマンドとツールの一覧: [docs/tools.md](docs/tools.md)
- 開発フローと CI: [docs/development.md](docs/development.md)
- 貢献ガイド: [CONTRIBUTING.md](CONTRIBUTING.md)
- リリース手順: [RELEASE.md](RELEASE.md)
- 帰属表示テンプレート: [ATTRIBUTION.md](ATTRIBUTION.md)

## ライセンス

MIT。再配布時の帰属表示テンプレートは [ATTRIBUTION.md](ATTRIBUTION.md) を参照してください。
