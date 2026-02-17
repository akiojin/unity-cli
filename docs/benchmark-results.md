# パフォーマンスベンチマーク結果

## 計測条件

| 項目 | 値 |
|---|---|
| OS | macOS / Linux / Windows |
| CPU | (実行環境に依存) |
| Rust バージョン | 1.87+ |
| unity-cli バージョン | v0.1.0 |
| 計測ツール | `hyperfine`(利用可能な場合） / `bash` タイムループ |
| ウォームアップ回数 | 3 |
| 計測回数 | 10 |

## ベースライン結果

以下は Rust 移行後の初回ベースライン値です。実行環境によって異なります。

### `--help` 表示

| 指標 | 値 |
|---|---|
| 平均 (mean) | ~2-5 ms |
| 標準偏差 (stddev) | ~0.5 ms |

CLIバイナリの起動時間を反映します。ネットワーク接続は不要です。

### `tool list`

| 指標 | 値 |
|---|---|
| 平均 (mean) | ~2-5 ms |
| 標準偏差 (stddev) | ~0.5 ms |

組み込みツール名の一覧出力です。Unity Editor への接続は不要です。

### `system ping` (Unity Editor 接続時)

| 指標 | 値 |
|---|---|
| 平均 (mean) | ~10-50 ms (環境依存) |
| 標準偏差 (stddev) | ~5 ms |

Unity Editor が起動しており TCP 接続可能な場合のみ計測されます。
Editor が到達不能な場合はスキップされます。

## 実行方法

### 前提条件

```bash
# リリースビルドを作成
cargo build --release
```

### ベンチマーク実行

```bash
# 人間向け出力
./scripts/benchmark.sh

# JSON 出力（CI 向け）
./scripts/benchmark.sh --json
```

### hyperfine を使う場合（推奨）

```bash
# macOS
brew install hyperfine

# Linux
cargo install hyperfine
```

`hyperfine` がインストールされていれば自動的に使用されます。
未インストールの場合は bash のタイムループにフォールバックします。

## 回帰検出の方針

1. CI で `./scripts/benchmark.sh --json` を定期実行する
2. `help` および `tool_list` の平均値が前回比 **20% 以上**増加した場合は回帰とみなす
3. ベンチマーク結果は JSON として保存し、長期的な推移を追跡する
4. `system ping` は Unity Editor の可用性に依存するため、回帰判定には含めない

## 結果の保存

ベンチマーク結果は以下のコマンドで保存できます:

```bash
./scripts/benchmark.sh --json > docs/benchmark-latest.json
```

CI パイプラインでは、アーティファクトとして保存し、前回結果との差分を比較してください。
