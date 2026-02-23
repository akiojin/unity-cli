# タスク: CLI実行効率の最適化

**機能ID**: `SPEC-a7206673` | **ステータス**: 実装中

---

## Phase 1: unityd デーモン

### 基盤

- [ ] `src/unityd.rs` を新規作成（`lspd.rs` をモデルにスキャフォールド）
- [ ] `cli.rs` に `Unityd` サブコマンド（`UnitydCommand`: Start/Stop/Status/Serve）を追加
- [ ] `main.rs` に `Command::Unityd` のディスパッチを追加
- [ ] `UnitydMode` enum（`auto` 固定）を定義
- [ ] （N/A）`--daemon` グローバルオプション追加（常に `auto` 相当へ仕様変更のため不要）

### DaemonRequest / DaemonResponse

- [ ] `unityd::DaemonRequest` を定義（Tool / Status / Ping / Stop）
- [ ] `unityd::DaemonResponse` を定義（ok / result / error）
- [ ] ソケットパス / PIDファイルパスのヘルパー関数を実装

### デーモンサーバー

- [ ] `serve_forever()` を実装（UnixSocket / TCP リスナー、アイドルタイムアウト）
- [ ] `handle_request()` を実装（Ping / Status / Stop / Tool ディスパッチ）
- [ ] `handle_stream()` を実装（改行区切りJSON読み書き）
- [ ] PIDファイル書き込み・クリーンアップを実装

### クライアント

- [ ] `start_background()` を実装（バックグラウンドプロセス起動 + 起動待ち）
- [ ] `stop()` を実装（Stopリクエスト送信）
- [ ] `status()` を実装（Statusリクエスト送信）
- [ ] `ping()` を実装（Pingリクエスト送信）
- [ ] `call_tool()` を実装（デーモン経由のTool実行）

### ConnectionPool

- [ ] `ConnectionPool` 構造体を定義（`HashMap<(String, u16), UnityClient>`）
- [ ] 接続の取得・再利用ロジックを実装
- [ ] 切断検知・再接続ロジックを実装
- [ ] 接続数上限の管理を実装

### 自動モード

- [ ] コマンド実行フローを常に `auto` 相当（デーモン接続試行 → 失敗時フォールバック）に固定
- [ ] `UNITY_CLI_UNITYD` の値に依存せず、指定されても挙動を変えない

### テスト（TDD）

- [ ] RED: `unityd start` → `unityd status` で running=true
- [ ] RED: `unityd stop` → `unityd status` で running=false
- [ ] RED: デーモン経由 `call_tool` が結果を返す
- [ ] RED: デーモン未起動時のフォールバック
- [ ] RED: `UNITY_CLI_UNITYD=on/off/auto` を指定しても挙動が不変
- [ ] RED: アイドルタイムアウト後の自動終了
- [ ] RED: ConnectionPool の接続再利用
- [ ] GREEN: 全テストを通す最小実装
- [ ] REFACTOR: `lspd.rs` との共通部分抽出

---

## Phase 2: バッチコマンド

### パーサー

- [ ] セミコロン区切り文字列のバッチパーサーを実装
- [ ] JSON配列入力のバッチパーサーを実装
- [ ] `SingleCommand` 構造体を定義（tool_name, params）

### DaemonRequest拡張

- [ ] `DaemonRequest::Batch { commands: Vec<SingleCommand> }` バリアントを追加
- [ ] デーモン側の `handle_request()` にBatch処理を追加
- [ ] バッチ結果を `Vec<DaemonResponse>` で返却

### CLIインターフェース

- [ ] `batch` サブコマンドまたは `--batch` オプションを追加
- [ ] バッチ入力のバリデーションを実装

### ワンショットバッチ

- [ ] ワンショットモードでのバッチ実行（1接続で連続送信）を実装

### テスト（TDD）

- [ ] RED: セミコロン区切りパーサーのユニットテスト
- [ ] RED: JSON配列入力のパーサーテスト
- [ ] RED: バッチ3コマンドの結果が配列で返る
- [ ] RED: バッチ内1コマンド失敗時に残りが継続する
- [ ] GREEN: 全テストを通す最小実装
- [ ] REFACTOR: パーサーとエグゼキュータの分離

---

## Phase 3: Unity側キュードレイン

### C#実装

- [ ] `UnityCliBridge` のTCPサーバー処理でキュードレインループを実装
- [ ] フレーム予算（16ms）チェックを実装
- [ ] フレーム予算超過時の次フレーム繰り越しを実装
- [ ] 既存の1コマンド処理との後方互換性を維持

### テスト（TDD）

- [ ] RED: 同時3コマンド送信で1フレーム内に全結果が返る
- [ ] RED: フレーム予算超過時の繰り越し動作
- [ ] GREEN: 最小実装
- [ ] REFACTOR: フレーム予算の設定可能化

---

## Phase 4: バイナリ最適化

### Cargo.toml

- [ ] `[profile.release]` セクションを追加
- [ ] `lto = true` を設定
- [ ] `codegen-units = 1` を設定
- [ ] `strip = true` を設定
- [ ] `opt-level = "s"` を設定
- [ ] `panic = "abort"` を設定

### 検証

- [ ] 最適化前後のバイナリサイズを計測・比較
- [ ] `opt-level = "s"` vs `"z"` の比較検証
- [ ] `cargo build --release` でビルドが通ることを確認
- [ ] 全テストが通ることを確認

---

## ドキュメント更新

- [ ] `docs/development.md` にデーモン運用セクションを追加
- [ ] `docs/development.md` にバッチコマンドの使い方を追加
- [ ] スキル定義の更新（デーモン関連スキルの追加）
- [ ] CLAUDE.md の更新（必要に応じて）

---

## 品質ゲート（全フェーズ共通）

- [ ] `cargo fmt --all -- --check` 通過
- [ ] `cargo clippy --all-targets -- -D warnings` 通過
- [ ] `cargo test --all-targets` 通過
- [ ] `dotnet test lsp/Server.Tests.csproj` 通過
