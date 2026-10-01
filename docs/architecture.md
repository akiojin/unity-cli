# アーキテクチャ概要

## 全体構成

`unity-cli` は以下の 3 つのコンポーネントで構成されています。

```
┌─────────────────────────────────────────────────────────────┐
│  ホストマシン                                                │
│                                                             │
│  ┌───────────────┐         TCP          ┌────────────────┐  │
│  │  unity-cli    │ ───────────────────→ │  Unity Editor  │  │
│  │  (Rust CLI)   │ ←─────────────────── │  + UPM Bridge  │  │
│  │  src/         │    JSON コマンド      │  UnityCliBridge│  │
│  └───────┬───────┘                      └────────────────┘  │
│          │                                                   │
│          │ プロセス起動                                       │
│          ▼                                                   │
│  ┌───────────────┐                                          │
│  │  C# LSP       │                                          │
│  │  (lsp/)       │                                          │
│  │  .NET 10      │                                          │
│  └───────────────┘                                          │
└─────────────────────────────────────────────────────────────┘
```

## コンポーネント詳細

### 1. Rust CLI (`src/`)

プロジェクトの中核となるコマンドラインツールです。

- **言語**: Rust (Edition 2021)
- **配布方法**: `cargo install unity-cli` (crates.io) またはソースビルド
- **主要クレート**: clap (CLI パーサ), tokio (非同期ランタイム), serde_json (JSON 処理)
- **役割**:
  - ユーザーからのコマンドを受け取り、Unity Editor へ TCP 経由で送信
  - 一部のツール（`read`, `search`, `list_packages` 等）はローカル実行
  - LSP プロセスの起動・管理

**主要ソースファイル**:

| ファイル | 責務 |
| -------- | ---- |
| `src/main.rs` | エントリーポイント |
| `src/cli.rs` | clap によるサブコマンド定義 |
| `src/core/config.rs` | 環境変数・設定管理 |
| `src/unity/transport.rs` | TCP 通信層 |
| `src/tooling/tool_catalog.rs` | 129 登録ツールのカタログ |
| `src/tooling/local_tools.rs` | ローカル実行ツール |
| `src/lsp/` | LSP プロセス管理 |
| `src/core/instances.rs` | 複数 Unity インスタンス管理 |

### 2. Unity Editor Bridge (`UnityCliBridge/Packages/unity-cli-bridge/`)

Unity Editor 側で TCP サーバとして動作し、CLI からのコマンドを処理する UPM パッケージです。

- **言語**: C#
- **配布方法**: UPM (Git URL)
- **名前空間**: `UnityCliBridge`
- **役割**:
  - TCP サーバとしてコマンドを受信
  - エディタ操作（シーン管理、アセット操作、コンポーネント操作等）を実行
  - 結果を JSON で返却

### 3. C# LSP (`lsp/`)

C# ソースコードの静的解析を行う Language Server Protocol 実装です。

- **言語**: C# (.NET 10)
- **役割**:
  - シンボル検索・参照解析
  - インデックスの構築と更新
- **起動制御**: `UNITY_CLI_LSP_MODE` 環境変数で制御 (`off` | `auto` | `required`)

## 通信プロトコル

### CLI → Unity Editor (TCP)

- **プロトコル**: TCP (デフォルト `127.0.0.1:6400`)
- **フォーマット**: 4-byte big-endian 長さヘッダ + UTF-8 JSON
- **認証**: Editor ごとの256-bitランダムトークン。CLI / unityd は
  `~/.unity-cli/editors/<pid>.json` の `authToken` を各リクエストに付ける。
  Bridge はメインスレッドのキューやバックグラウンド処理へ渡す前に検証し、
  欠落・不一致は `UNAUTHORIZED` で拒否する（`ping` / `eval_csharp` も対象）。
- **保存と更新**: lockfile は POSIX 0600。秘密を書き込む前に権限を設定し、
  heartbeat でもアトミックに置換する。ドメインリロードで新しいトークンを生成し、
  再接続時に読み直す。`instances list` や診断にはトークンを出力しない。
- **待受**: 初期値と設定読込失敗時は loopback。非 loopback 設定では起動時に
  Editor Console へ警告する。TCP は平文のため、リモート接続には信頼できる
  ネットワークまたは暗号化トンネルを使う。同じOSユーザーのファイル読取権限を
  持つプロセスからの隔離を提供するものではない。
- **移行期間**: 本リリースに限り、Editor 起動時の
  `UNITY_CLI_ALLOW_UNAUTHENTICATED=1` でトークン無しの旧クライアントを許容。
  CLI に同じ値を設定すると stderr に非推奨警告を出す。不一致トークンは常に拒否。
  次のマイナーリリースで opt-out を廃止し、認証を必須化する。

```
CLI 側                        Unity Editor 側
───────                      ──────────────
  │  TCP connect (port 6400)     │
  │ ──────────────────────────→ │
  │                              │
  │  JSON コマンド送信            │
  │ ──────────────────────────→ │
  │                              │
  │  JSON レスポンス受信          │
  │ ←────────────────────────── │
  │                              │
  │  TCP close                   │
  │ ──────────────────────────→ │
```

### CLI → LSP (プロセス起動)

- LSP は CLI が子プロセスとして起動
- 標準入出力を通じて LSP プロトコルで通信

## 環境変数

| 変数名 | 用途 | デフォルト |
| -------- | ---- | --------- |
| `UNITY_CLI_HOST` | Unity Editor の接続先ホスト | `127.0.0.1` |
| `UNITY_CLI_PORT` | Unity Editor の接続先ポート | `6400` |
| `UNITY_CLI_TIMEOUT_MS` | コマンドタイムアウト (ミリ秒) | ― |
| `UNITY_CLI_LSP_MODE` | LSP 起動モード | `off` |
| `UNITY_CLI_LSP_COMMAND` | LSP 実行コマンド | ― |
| `UNITY_CLI_LSP_BIN` | LSP 実行ファイルパス | ― |
| `UNITY_PROJECT_ROOT` | Unity プロジェクトルート | ― |

**互換性環境変数**: 旧 `UNITY_CLI_*` 系の環境変数は移行用エイリアスとしてサポートされています。新規設定は `UNITY_CLI_*` を利用してください。詳細は [migration-notes.md](./migration-notes.md) を参照してください。

## ビルドと配布

- **Rust CLI**: `cargo build --release` でビルド、`cargo install` で導入
- **UPM パッケージ**: Git URL 経由で Unity Package Manager から導入
- **LSP**: `dotnet build lsp/Server.csproj` でビルド（CLI が自動的に起動管理）
