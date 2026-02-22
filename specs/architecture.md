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
│  │  .NET 9       │                                          │
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
|----------|------|
| `src/main.rs` | エントリーポイント |
| `src/cli.rs` | clap によるサブコマンド定義 |
| `src/config.rs` | 環境変数・設定管理 |
| `src/transport.rs` | TCP 通信層 |
| `src/tool_catalog.rs` | 108 ツールのカタログ |
| `src/local_tools.rs` | ローカル実行ツール |
| `src/lsp.rs` | LSP プロセス管理 |
| `src/instances.rs` | 複数 Unity インスタンス管理 |

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

- **言語**: C# (.NET 9)
- **役割**:
  - シンボル検索・参照解析
  - インデックスの構築と更新
- **起動制御**: `UNITY_CLI_LSP_MODE` 環境変数で制御 (`off` | `auto` | `required`)

## 通信プロトコル

### CLI → Unity Editor (TCP)

- **プロトコル**: TCP (デフォルト `localhost:6400`)
- **フォーマット**: JSON（改行区切り）
- **認証**: なし（ローカル通信前提）

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
|--------|------|-----------|
| `UNITY_CLI_HOST` | Unity Editor の接続先ホスト | `localhost` |
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
