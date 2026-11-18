# unity-cli

[English](README.md) | [中文](README.zh.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Italiano](README.it.md) | [Español](README.es.md)

`unity-cli` は、Claude Code から Unity Editor を直接 TCP で操作するための Rust 製 CLI です。
[`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server) の後継として、Node.js + MCP からネイティブバイナリ中心の構成に再設計しました。

## 特徴

- Claude Code から、用途別スキルと typed コマンドで Unity を操作できます。
- シーン、アセット、コード、テスト、UI、Editor を含む 143 個の runtime Unity Tool API と 11 個の Reference Cache ツールを利用できます。
- Unity 公式 C# リファレンス（[UnityCsReference](https://github.com/Unity-Technologies/UnityCsReference)）を `unity-cli reference fetch` でローカルキャッシュし、`unity-csharp-reference` スキルから読み取り専用で参照できます。
- 単一バイナリで高速起動、低オーバーヘッドです。

## v0.13 以降の機能

[実行可能なツール例](docs/tools.md#v013v015-executable-examples) には次が含まれます。

- Domain Reload をまたぐ PlayMode テスト結果。キーボード / ゲームパッドのホールド、マウス / タッチのシミュレーション、入力シーケンス、InputAction 通知の確認。
- MP4、WebM、PNG フレームシーケンスの録画と、メソッドのホットリロードプレビュー。
- Timeline のアセット / トラック / クリップ編集、Lighting / NavMesh / NavMeshSurface / Occlusion のベイクジョブ、ステータスをポーリングするスタンドアロン Player ビルド。

実行時の入力、キャプチャ、ホットリロードは `unity-playmode-testing`、Timeline は `unity-asset-management`、ビルドとベイクは `unity-editor-tools` から始めてください。パッケージとシーンの前提は各例に記載しています。

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
Windows では 6000.3.28f1、6000.6.8f1、Unity 7.0 Alpha 7000.0.0a16 で、2026-11-10 時点の更新ソースに対するベースラインと全ツール確認が通っています。
7000.0.0a16 の Editor は CoreCLR で動作します。正確なアルファビルド、パッケージ、検証範囲はリンク先の Windows 結果を参照してください。

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
managed ディレクトリをユーザーの `PATH` に追加します。`UNITY_CLI_VERSION=v0.19.1` で
リリースを固定できます。導入後は CLI がバックグラウンドで自動更新を確認し、
更新も同じチェックサムで検証されます。

`~/.local/bin` が PATH に含まれていない場合は、シェルプロファイル
（例: `~/.zshrc` や `~/.bashrc`）に次を追加してください:

```bash
export PATH="$HOME/.local/bin:$PATH"
```

自動更新を止めるには `UNITY_CLI_NO_AUTO_UPDATE=1` を設定してください。

Apple silicon 向け CLI は、同梱する ONNX Runtime に合わせて macOS 13.4 以降を
必要とします。Intel 向け CLI のビルド対象は macOS 10.13 以降です。reference 埋め込みには
対応する ONNX Runtime の導入も必要です。Apple silicon でソースからビルドする場合は
Xcode Command Line Tools を導入し、`MACOSX_DEPLOYMENT_TARGET=13.4`（またはそれ以降）を
設定してください。CoreML の OS バージョン判定に必要な compiler-rt は、選択中の
Xcode toolchain からリンクします。

#### パッケージマネージャ

```bash
brew install akiojin/tap/unity-cli     # macOS / Linux（Homebrew）
```

Homebrew で導入した場合は `brew upgrade` で更新します。
Windows の winget パッケージは未公開です（#537）。公開完了までは、上記の
PowerShell クイックインストールを使ってください。
Intel Mac の reference 埋め込み（`unity-cli reference embed-build` / `embed-search`）は ONNX Runtime を動的に読み込みます。
Homebrew formula は `onnxruntime` を依存として導入します。それ以外の導入方法では
`brew install onnxruntime` を実行するか、`ORT_DYLIB_PATH` を設定してください。

#### Releases / cargo install

crates.io に公開された crate をインストールできます。リリースワークフローが `cargo publish` で公開します。

```bash
cargo install unity-cli
```

最新バイナリは [GitHub
Releases](https://github.com/akiojin/unity-cli/releases) から取得できます。
ローカル clone からインストールする場合:

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

## スキル (23)

ワークフローガイドとして書いたスキルです。一覧は [docs/skills.md](docs/skills.md) と、プラグイン同梱ディレクトリ `.claude-plugin/plugins/unity-cli/skills/` に一致します。

| カテゴリ | スキル |
| --- | --- |
| 導入 | `unity-project-bootstrap`, `unity-cli-usage` |
| シーンとオブジェクト | `unity-scene-create`, `unity-scene-inspect`, `unity-gameobject-edit`, `unity-prefab-workflow` |
| アセット | `unity-2d-sprite-tilemap`, `unity-asset-management`, `unity-audio-setup`, `unity-addressables` |
| コード | `unity-csharp-navigate`, `unity-csharp-edit`, `unity-csharp-reference` |
| 実行時とテスト | `unity-playmode-testing`, `unity-input-system`, `unity-ui-toolkit-build`, `unity-localization`, `unity-ui-automation`, `unity-development-loop`, `unity-vfx-graph` |
| エディター | `unity-editor-tools`, `unity-package-management`, `unity-urp-setup` |

`gh-skills-sync` は `dev-skills/` にある開発専用ヘルパーです。プラグインには同梱しません。

## クイック例

ダイアログなしで、ローカルの `.unitypackage` またはダウンロード済みの Asset Store パッケージをインポートし、コンパイルと Domain Reload を待ちます。

```bash
unity-cli package import /absolute/path/Small.unitypackage --output json
unity-cli package cache-list --output json
unity-cli package cache-import "Small Package" --output json
# My Assets を開き、Download は手動で押し、その後のインポートだけ自動化する:
unity-cli package asset-store-import 12345 --download-timeout-ms 600000 --output json
```

キャッシュの場所は `--cache-path` または `UNITY_CLI_ASSET_STORE_CACHE_PATH` で指定します。
選択 ID、タイムアウト、復旧は [package import commands](docs/tools.md#unitypackage-and-asset-store-cache) を参照してください。`package asset-store-open [name-or-id]` と `package asset-store-wait <name-or-id>` は表示と待機を分けて実行します。ダウンロード、ログイン、所有確認は Unity 上の手動操作のままで、CLI は Download を押したり Unity アカウントトークンを読んだりしません。

```bash
# 接続確認
unity-cli system ping

# シーン作成
unity-cli scene create MainScene

# raw ツール呼び出しで GameObject 作成
unity-cli raw create_gameobject --json '{"name":"Player"}'

# C# コード検索（ローカルツール）
unity-cli tool call search --json '{"pattern":"PlayerController"}'

# ツールスキーマの確認
unity-cli tool schema create_scene --output json

# 変更系ツール呼び出しのドライラン（副作用なし）
unity-cli --dry-run tool call create_scene --json '{"sceneName":"PreviewScene"}'

# EditMode テスト実行
unity-cli tool call run_tests --json '{"mode":"editmode"}'
```

## GWT Spec ワークフロー

機能仕様は `gwt-spec` ラベルの付いた GitHub Issue で管理します。
`Spec`、`Plan`、`Tasks`、`TDD` の正本は Issue 本文です。

## 貢献

外部からの貢献は `develop` ブランチを対象にしてください。PR を開く前に
[CONTRIBUTING.md](CONTRIBUTING.md) を読み、ブランチ方針、必須 CI、スキル文書の変更を確認してください。

## 設定

| 変数 | 説明 | 既定値 |
| --- | --- | --- |
| `UNITY_PROJECT_ROOT` | `Assets/` と `Packages/` を含むディレクトリ | 自動検出 |
| `UNITY_CLI_HOST` | Unity Editor ホスト | `127.0.0.1` |
| `UNITY_CLI_PORT` | Unity Editor ポート | `6400` |
| `UNITY_CLI_AUTH_TOKEN_FILE` | リモート接続やマウントした資格情報向けの、`authToken` を含む Editor lockfile JSON | *(未設定)* |
| `UNITY_CLI_ALLOW_UNAUTHENTICATED` | 一時的な移行用 opt-out。`1` のみ有効 | *(未設定)* |
| `UNITY_CLI_TIMEOUT_MS` | コマンドタイムアウト (ms) | `30000` |
| `UNITY_CLI_LSP_MODE` | LSP モード (`off` / `auto` / `required`)。`auto` はシンボル検索で C# LSP を優先し、使えないときはローカルインデックスに戻る（`backend: "index"`, `fallbackReason`） | `auto` |
| `UNITY_CLI_LSP_AUTO_TIMEOUT_MS` | `auto` のシンボル検索が LSP を待つ上限 (ms)。超えるとローカルインデックスに戻る | `10000` |
| `UNITY_CLI_TOOLS_ROOT` | ダウンロード済みツールのルートディレクトリ | OS 既定 |
| `UNITY_CLI_UNITYD_PORT` | Windows の unityd ループバック TCP ポート（クライアントと daemon） | `6422` |
| `UNITY_CLI_LSPD_PORT` | Windows の LSP daemon ループバック TCP ポート（クライアントと daemon） | `6421` |
| `UNITY_CLI_NO_AUTO_UPDATE` | バックグラウンド自動更新を無効にする（`1` で無効） | *(未設定)* |

旧 MCP 接頭辞の環境変数は非対応です。`UNITY_CLI_*` のみ使用してください。
Windows の daemon ポートは 1 から 65535 の整数です。未設定に戻すと既定値になります。daemon と CLI クライアントに同じ値を渡してください。
認証、Docker、WSL2 は [設定ガイド](docs/configuration.md) を参照してください。

## ドキュメント

- コマンドとツールの一覧: [docs/tools.md](docs/tools.md)
- スキル契約と一覧: [docs/skills.md](docs/skills.md)
- 設定ガイド: [docs/configuration.md](docs/configuration.md)
- アーキテクチャ: [docs/architecture.md](docs/architecture.md)
- ホットリロード: [docs/hot-reload.md](docs/hot-reload.md)
- 移行メモ: [docs/migration-notes.md](docs/migration-notes.md)
- MCP 薄いアダプタとクライアント設定: [docs/mcp.md](docs/mcp.md)
- 公式 Unity CLI との比較（機能・レイテンシ計測・併用方法）: [docs/comparison.md](docs/comparison.md)
- 開発フローと CI: [docs/development.md](docs/development.md)
- Asset Store ダウンロードの実現性と制約: [調査](docs/asset-store-download-research.md)
- 貢献ガイド: [CONTRIBUTING.md](CONTRIBUTING.md)
- リリース手順: [RELEASE.md](RELEASE.md)
- 帰属表示テンプレート: [ATTRIBUTION.md](ATTRIBUTION.md)

## ライセンス

MIT。再配布時の帰属表示テンプレートは [ATTRIBUTION.md](ATTRIBUTION.md) を参照してください。
