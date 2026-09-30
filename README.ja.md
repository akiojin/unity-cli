# unity-cli

[English](README.md) | [中文](README.zh.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Italiano](README.it.md) | [Español](README.es.md)

`unity-cli` は、Claude Code から Unity Editor を直接 TCP で操作するための Rust 製 CLI です。
[`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server) の後継として、Node.js + MCP からネイティブバイナリ中心の構成に再設計しました。

## 特徴

- Claude Code から、用途別スキルと typed コマンドで Unity を操作できます。
- シーン、アセット、コード、テスト、UI、Editor を含む `101` 個の Unity Tool API を利用できます。
- Unity 公式 C# リファレンス（[UnityCsReference](https://github.com/Unity-Technologies/UnityCsReference)）を `unity-cli reference fetch` でローカルキャッシュし、`unity-csharp-reference` スキルから読み取り専用で参照できます。
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

### Editor 互換性

Bridge の最小バージョンは Unity 2022.3 です。macOS 検証対象は
2022.3.62f3、6000.0.84f1、6000.3.25f1、6000.4.11f1、6000.5.3f1、
6000.6.3f1、6000.7.0a2、6000.7.0b2 です（2026-09-30 受け入れ E2E: 全版 PASS）。
[対応表の検証日・結果](docs/editor-compatibility.md)を確認してください。
6000.7 の Editor は Mono で動作し、CoreCLR Player ビルドはこの検証の対象外です。

### 推奨: Claude Code プラグイン

Claude Code Marketplace から `unity-cli` プラグインをインストールします:

```bash
/plugin marketplace add akiojin/unity-cli
```

プラグインが提供するのはスキルで、バイナリと Unity ブリッジは必要時に導入されます。
Unity 操作が必要なのに `unity-cli` が未導入の場合、`unity-cli-usage` スキルに従って
エージェントがインストールスクリプト（`scripts/install.sh`）を実行し、Unity プロジェクトで
`unity-cli setup` を実行します（`Packages/manifest.json` を変更する前にユーザーへ明示します）。

### Codex スキル

Codex でこのリポジトリを利用する場合、`.codex/skills/` にスキルのシンボリックリンクが配置済みです。
リポジトリをクローンするだけで追加セットアップは不要です。

### 手動インストール

最新バイナリは [GitHub
Releases](https://github.com/akiojin/unity-cli/releases) から取得できます。
Cargo を使う場合は、ローカル clone からインストールしてください:

```bash
git clone https://github.com/akiojin/unity-cli.git
cd unity-cli
cargo install --path .
```

プロジェクトのセットアップを 1 コマンドで行う（binary 検証 → bridge 導入 → Editor 接続確認）:

```bash
cd /path/to/YourUnityProject
unity-cli --output json setup --launch-editor
```

`setup` は `Packages/manifest.json` に OpenUPM の scoped registry と
`com.akiojin.unity-cli-bridge`（CLI と同一バージョン）を追加し、必要なら Unity Hub の
Editor を起動してブリッジの応答を待ち、結果を 1 つの JSON で返します。
`unity-cli bridge install | upgrade | status` で個別にも管理でき、`install` は冪等です。
CLI とブリッジのバージョンが異なる場合は `setup` / `system ping` が `versionCheck` で報告します。
旧 Input Manager のみのプロジェクトでは、Editor が閉じていれば `install` が
`ProjectSettings/ProjectSettings.asset` の `activeInputHandler` を Both に設定し、
Input System 依存の導入時に Editor をブロックする再起動ダイアログを回避します。

Unity 側ブリッジパッケージを手動で導入する場合（いずれかを選択）:

**OpenUPM**:

```bash
openupm add com.akiojin.unity-cli-bridge
```

**Git URL**（Unity Package Manager）:

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

接続確認:

```bash
unity-cli system ping
unity-cli doctor --output json   # ping 失敗時: SAFE_MODE / BRIDGE_NOT_INSTALLED / PORT_IN_USE など
```

managed バイナリの確認と更新:

```bash
unity-cli cli doctor
unity-cli cli install
```

`unityd` と `lspd` は daemon 起動時に managed `unity-cli` / C# LSP バイナリを自動更新します。
managed copy は `UNITY_CLI_TOOLS_ROOT`（未指定時は OS 既定の tools ディレクトリ）配下に配置され、確認プロンプトなしで更新されます。

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
| `UNITY_CLI_HOST` | Unity Editor ホスト | `127.0.0.1` |
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
