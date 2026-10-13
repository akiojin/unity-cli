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

### LLM エージェントでインストール・更新

Unity プロジェクトで開いた Claude Code や Codex などのコーディングエージェントに、
次のプロンプトをそのまま貼り付けてください。後から同じプロンプトを再実行すると、CLI と
Unity 側の bridge パッケージをまとめて更新できます:

```text
この Unity プロジェクトに unity-cli をインストールまたは更新し、Unity 側の bridge パッケージを
CLI と同じバージョンに揃えて、Unity Editor と接続してください。

1. OS（macOS / Linux / Windows）を判定してください。
2. `unity-cli` が PATH 上にない場合は、公式のクイックインストールスクリプトを実行してください:
   - macOS / Linux: curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
   - Windows（PowerShell）: irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
3. `unity-cli` がすでに PATH 上にある場合は、代わりに最新リリースへ更新してください。
   UNITY_CLI_NO_AUTO_UPDATE=1 または UNITY_CLI_VERSION が設定されている場合は、私が意図的に
   バージョンを固定しています。更新せず、私に伝えて確認を取ってください。それ以外の場合は、
   バイナリの実体の場所を（シンボリックリンクをたどって）確認し、導入方法に合わせて更新してください:
   - クイックインストール（~/.unity/tools 配下の managed コピー）: unity-cli cli install
   - Homebrew: brew upgrade akiojin/tap/unity-cli
   - winget: winget upgrade akiojin.unity-cli
   - それ以外（cargo install や手動ダウンロード）: 導入方法を私に伝え、変更前に確認を取ってください。
4. PATH から `unity-cli --version` が実行できることを確認してください。macOS / Linux で
   見つからない場合は、このシェルの PATH に ~/.local/bin を追加し、シェルプロファイルに
   追記する行を教えてください。Windows では、更新されたユーザー PATH を反映するため
   新しいシェルを開いてください。
5. プロジェクトを変更する前に、setup が Packages/manifest.json を編集すること
   （OpenUPM の scoped registry と、CLI と同じバージョンに固定した
   com.akiojin.unity-cli-bridge を追加すること）と、旧 Input Manager のみのプロジェクトでは
   ProjectSettings/ProjectSettings.asset の activeInputHandler を Both に設定することを
   説明し、私の確認を待ってください。
6. Unity プロジェクトのルートで次を実行してください: unity-cli --output json setup --launch-editor
   JSON が ok: false の場合は editor.hint に従ってください（初回インポートは数分かかる
   ことがあります。待ち続けるには setup を再実行します）。
7. 次のコマンドで bridge のバージョンを確認してください: unity-cli --output json bridge status
   - source が registry で、versionCheck.status（この結果または setup の結果）が mismatch の場合:
     Packages/manifest.json の com.akiojin.unity-cli-bridge の行を CLI のバージョンに変更することを
     私に伝え、確認を待ってください。その後 unity-cli bridge upgrade を実行し、
     Unity Editor にパッケージを再インポートさせてください。
   - source が custom または embedded（Git URL、ローカルパス、Packages/ 配下のコピー）の場合や、
     versionCheck.status が unknown の場合: 書き換えないでください。代わりに source と
     両方のバージョンを私に報告してください。
8. 次のコマンドで接続を確認してください: unity-cli system ping
   versionCheck.status が match（CLI と bridge が同じバージョン）であることを確認してください。
9. いずれかの手順が失敗した場合は、次を実行してください: unity-cli doctor --output json
   diagnosis コードを報告し、そのヒントに従ってから再試行してください。
```

**Claude Code プラグイン**: Claude Code Marketplace から `unity-cli` プラグインを
インストールすると、エージェントが Unity 操作用のスキルを使えるようになります:

```bash
/plugin marketplace add akiojin/unity-cli
```

プラグインが提供するのはスキルで、バイナリと Unity ブリッジは必要時に導入されます。
Unity 操作が必要なのに `unity-cli` が未導入の場合、`unity-cli-usage` スキルに従って
エージェントがクイックインストールスクリプトを実行し、Unity プロジェクトで
`unity-cli setup` を実行します（`Packages/manifest.json` を変更する前にユーザーへ明示します）。

**Codex スキル**: Codex でこのリポジトリを利用する場合、`.agents/skills/` に
スキルのシンボリックリンクが配置済みです（`.claude/skills/` からも参照できます）。
リポジトリをクローンするだけで追加セットアップは不要です。

### 手動でインストール

#### クイックインストール（推奨）

macOS（Apple silicon / Intel）と Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
```

Windows（PowerShell）:

```powershell
irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
```

どちらのインストーラもリリースバイナリを managed レイアウト
`~/.unity/tools/unity-cli/{rid}/` に取得し、導入前にリリースの `SHA256SUMS` で検証します。
ハッシュが一致しない場合は導入を中止し、既存のバイナリには触れません。
`install.sh` は `~/.local/bin/unity-cli` にシンボリックリンクを作り、`install.ps1` は
managed ディレクトリをユーザーの `PATH` に追加します。`UNITY_CLI_VERSION=v0.16.0` で
リリースを固定できます。導入後は CLI がバックグラウンドで自動更新を確認し、
更新も同じチェックサムで検証されます。

`~/.local/bin` が PATH に含まれていない場合は、シェルプロファイル
（例: `~/.zshrc` や `~/.bashrc`）に次を追加してください:

```bash
export PATH="$HOME/.local/bin:$PATH"
```

自動更新を止めるには `UNITY_CLI_NO_AUTO_UPDATE=1` を設定してください。

#### パッケージマネージャ

```bash
brew install akiojin/tap/unity-cli     # macOS / Linux（Homebrew）
winget install akiojin.unity-cli       # Windows（winget）
```

パッケージマネージャで導入した場合は `brew upgrade` / `winget upgrade` で更新します。
Intel Mac の reference 埋め込み（`unity-cli reference embed-build` / `embed-search`）は ONNX Runtime を動的に読み込みます。
Homebrew formula は `onnxruntime` を依存として導入します。それ以外の導入方法では
`brew install onnxruntime` を実行するか、`ORT_DYLIB_PATH` を設定してください。

#### Releases / cargo install

最新バイナリは [GitHub
Releases](https://github.com/akiojin/unity-cli/releases) から取得できます。
Cargo を使う場合は、ローカル clone からインストールしてください:

```bash
git clone https://github.com/akiojin/unity-cli.git
cd unity-cli
cargo install --path .
```

#### プロジェクトのセットアップ

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

#### 接続確認

```bash
unity-cli system ping
unity-cli doctor --output json   # ping 失敗時: SAFE_MODE / BRIDGE_NOT_INSTALLED / PORT_IN_USE など
```

#### managed バイナリの確認と更新

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
- 公式 Unity CLI との比較（機能・レイテンシ計測・併用方法）: [docs/comparison.md](docs/comparison.md)
- 開発フローと CI: [docs/development.md](docs/development.md)
- 貢献ガイド: [CONTRIBUTING.md](CONTRIBUTING.md)
- リリース手順: [RELEASE.md](RELEASE.md)
- 帰属表示テンプレート: [ATTRIBUTION.md](ATTRIBUTION.md)

## ライセンス

MIT。再配布時の帰属表示テンプレートは [ATTRIBUTION.md](ATTRIBUTION.md) を参照してください。
