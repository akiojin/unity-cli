# 実装計画: CLI実行効率の最適化

**機能ID**: `SPEC-a7206673` | **ステータス**: 実装中

## 概要

LLMワークフロー中の `unity-cli` コマンド実行レイテンシを4フェーズで段階的に改善する。各フェーズはTDDで進め、品質ゲートを通過させる。

---

## フェーズ依存関係

```
Phase 1: unityd daemon ─────┐
                             ├─→ Phase 2: batch command（Phase 1に依存）
Phase 3: Unity queue drain ──┘   （Phase 3は独立だがPhase 2と連携可能）
Phase 4: binary optimization ───── （独立、低リスク）
```

---

## Phase 1: unityd デーモン（最優先）

**目的**: TCP接続を永続化するデーモンプロセスを導入し、コマンドごとの接続確立コストを排除する

**モデル**: 既存の `src/lspd.rs`（LSPデーモン）と同パターンで実装

### 設計方針

- `src/unityd.rs` を新規作成。`lspd.rs` のアーキテクチャ（start/stop/status/serve/ping）を踏襲
- CLIに `Unityd` サブコマンド（`UnitydCommand` enum: Start/Stop/Status/Serve）を追加
- デーモン ↔ クライアント間は既存の改行区切りJSON（`DaemonRequest` / `DaemonResponse`）
- デーモン内部に `ConnectionPool` を保持し、(host, port) ごとに `UnityClient` を再利用
- コマンド実行フローはユーザー設定なしで常に `auto` 相当（接続試行→失敗時フォールバック）で動作
- `UNITY_CLI_UNITYD` は互換のため設定されていても挙動に反映しない
- Unix: Unixドメインソケット、Windows: TCP（既存パターン）
- アイドルタイムアウト: デフォルト600秒

### TDDアプローチ

1. **RED**: `unityd start` → `unityd status` → running=true のテスト
2. **RED**: `unityd stop` → `unityd status` → running=false のテスト
3. **RED**: デーモン経由の `tool call` が結果を返すテスト
4. **RED**: デーモン未起動時のフォールバックテスト
5. **RED**: `UNITY_CLI_UNITYD=on/off/auto` を指定しても挙動が変わらないテスト
6. **RED**: アイドルタイムアウト後の自動終了テスト
7. **RED**: ConnectionPool の接続再利用テスト
8. **GREEN**: 最小実装で各テストを通過
9. **REFACTOR**: lspd.rs との共通部分を抽出

### 品質ゲート

```bash
cargo fmt --all -- --check
cargo clippy --all-targets -- -D warnings
cargo test --all-targets
```

---

## Phase 2: バッチコマンド（Phase 1 完了後）

**目的**: 複数コマンドを1回のCLI呼び出しで実行し、IPC往復回数を削減する

**依存**: Phase 1（デーモンのDaemonRequestにBatch型を追加）

### 設計方針

- `DaemonRequest` に `Batch { commands: Vec<SingleCommand> }` バリアントを追加
- CLIに `--batch` オプションまたは `batch` サブコマンドを追加
- セミコロン区切り文字列 → パースして `Vec<SingleCommand>` に変換
- JSON配列入力もサポート
- 結果は `Vec<DaemonResponse>` をJSON配列で返却
- ワンショットモードでもバッチ実行を可能にする（接続1本で連続送信）

### TDDアプローチ

1. **RED**: セミコロン区切りパーサーのユニットテスト
2. **RED**: JSON配列入力のパーサーテスト
3. **RED**: バッチ3コマンドの実行結果が配列で返るテスト
4. **RED**: バッチ内1コマンド失敗時に残りが継続するテスト
5. **GREEN**: 最小実装
6. **REFACTOR**: パーサーとエグゼキュータの分離

### 品質ゲート

```bash
cargo fmt --all -- --check
cargo clippy --all-targets -- -D warnings
cargo test --all-targets
```

---

## Phase 3: Unity側キュードレイン（独立）

**目的**: Unity Editorが1フレーム内で複数コマンドを処理し、コマンドあたりのフレーム待機コストを排除する

**依存**: なし（Phase 1/2 と独立で着手可能だが、バッチ機能との連携で効果が最大化）

### 設計方針

- `UnityCliBridge/Packages/unity-cli-bridge/` のTCPサーバー処理を修正
- 現在の「1フレーム1コマンド」を「キューが空になるまで連続処理」に変更
- フレーム予算（16ms）を超えたら次フレームへ繰り越し
- 既存の1コマンドずつの動作との後方互換性を維持

### TDDアプローチ

1. **RED**: 同時に3コマンドを送信し、1フレーム内で全結果が返るテスト
2. **RED**: フレーム予算超過時に次フレームへ繰り越すテスト
3. **GREEN**: キュードレインループの実装
4. **REFACTOR**: フレーム予算の設定可能化

### 品質ゲート

```bash
cargo test --all-targets
dotnet test lsp/Server.Tests.csproj
```

---

## Phase 4: バイナリ最適化（独立・低リスク）

**目的**: リリースビルドのバイナリサイズ削減と起動時間短縮

**依存**: なし（いつでも着手可能）

### 設計方針

- `Cargo.toml` に `[profile.release]` セクションを追加
- LTO: `lto = true`
- codegen-units: `codegen-units = 1`
- strip: `strip = true`
- opt-level: `opt-level = "s"` （サイズ最適化）
- panic: `panic = "abort"`

### TDDアプローチ

1. **RED**: 最適化前後のバイナリサイズ比較（手動計測）
2. **GREEN**: `[profile.release]` を設定
3. **REFACTOR**: opt-level の `"s"` vs `"z"` の比較検証

### 品質ゲート

```bash
cargo fmt --all -- --check
cargo clippy --all-targets -- -D warnings
cargo test --all-targets
cargo build --release
```

---

## 横断的な品質ゲート

全フェーズ完了後、以下を満たすことを確認:

```bash
cargo fmt --all -- --check
cargo clippy --all-targets -- -D warnings
cargo test --all-targets
dotnet test lsp/Server.Tests.csproj
```

---

## リスクと緩和策

| リスク | 影響 | 緩和策 |
|--------|------|--------|
| デーモンのstaleプロセス残留 | ポートバインド失敗 | PIDファイル + ソケットクリーンアップを `lspd.rs` パターンで実装 |
| ConnectionPool の接続リーク | リソース枯渇 | 接続数上限 + アイドル接続の自動切断 |
| Unity キュードレインのフレーム超過 | Editor のカクつき | フレーム予算チェック + 繰り越しロジック |
| LTO によるビルド時間増加 | CI遅延 | リリースビルドのみに適用（開発ビルドは影響なし） |
